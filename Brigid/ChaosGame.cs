#region
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Brigid.Collections;
using DALib.Utility;
using Brigid.Controls.Generic;
using Brigid.Networking;
using Brigid.Networking.Definitions;
using Brigid.Screens;
using Brigid.Systems;
using Brigid.Utilities;
using Chaos.DarkAges.Definitions;
using DALib.Cryptography;
using DALib.Extensions;
using DALib.Networking.Wire;
using ServerPackets = DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SkiaSharp;
#endregion

namespace Brigid;

public sealed class ChaosGame : Game
{
    public const int VIRTUAL_WIDTH = 640;
    public const int VIRTUAL_HEIGHT = 480;
    private const float ASPECT_RATIO = (float)VIRTUAL_WIDTH / VIRTUAL_HEIGHT;

    //the window opens at this integer multiple of the virtual resolution (2 → 1280×960); the F-key cycle continues from here
    private const int DEFAULT_WINDOW_MULTIPLIER = 2;

    /// <summary>
    ///     Sets the window title to <c>Brigid {version}</c>, with <c> - {server}</c> appended once a server is
    ///     selected. The server suffix reuses <see cref="ConnectionManager.ServerName" /> — the friendly name the
    ///     lobby resolves (the same value the HUD's SZ_SERVER label shows), so retail reads "Dark Ages" and
    ///     Hybrasyl reads "Hybrasyl" without a duplicate host→name mapping here. Driven by <see cref="ConnectionManager.ServerNameChanged" />.
    /// </summary>
    private void UpdateWindowTitle()
    {
        var server = Connection.ServerName;
        var version = VersionInfo.Display;
        Window.Title = string.IsNullOrWhiteSpace(server) ? $"Brigid {version}" : $"Brigid {version} - {server}";
    }

    /// <summary>
    ///     Sets the SDL window (title-bar + taskbar) icon to the embedded Brigid logo. MonoGame's built-in Icon.bmp
    ///     loader can't parse our BMP and falls back to its default icon, so we decode a PNG with SkiaSharp and set the
    ///     icon directly via SDL. Best-effort: any missing resource/handle just leaves MonoGame's default in place.
    /// </summary>
    private void SetWindowIcon()
    {
        var window = Window.Handle;

        if (window == nint.Zero)
            return;

        using var stream = typeof(ChaosGame).Assembly.GetManifestResourceStream("BrigidIcon.png");

        if (stream is null)
            return;

        using var decoded = SKBitmap.Decode(stream);

        if (decoded is null)
            return;

        //straight (unpremultiplied) RGBA8888: byte order R,G,B,A matches SDL's ABGR8888 masks on little-endian
        var info = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var pixels = new byte[info.BytesSize];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);

        try
        {
            using var image = SKImage.FromBitmap(decoded);

            if (!image.ReadPixels(info, pin.AddrOfPinnedObject(), info.RowBytes, 0, 0))
                return;

            var surface = Sdl.SDL_CreateRGBSurfaceFrom(
                pin.AddrOfPinnedObject(),
                info.Width,
                info.Height,
                32,
                info.RowBytes,
                0x000000FFu,
                0x0000FF00u,
                0x00FF0000u,
                0xFF000000u);

            if (surface == nint.Zero)
                return;

            //SDL_SetWindowIcon copies the surface, so it's safe to free the surface and unpin the buffer right after
            Sdl.SDL_SetWindowIcon(window, surface);
            Sdl.SDL_FreeSurface(surface);
        } finally
        {
            pin.Free();
        }
    }

    private readonly GraphicsDeviceManager Graphics;
    private string MetaFilePath => Path.Combine(GlobalSettings.DataPath, "metafile");
    private readonly Dictionary<string, uint> MetaPendingChecksums = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IServerPacket> PacketBuffer = [];
    private int CursorOffsetX;
    private int CursorOffsetY;
    private Texture2D? CursorTexture;
    internal volatile bool GcRequested;
    private bool ScreenshotRequested;
    private int HandCursorOffsetX;
    private int HandCursorOffsetY;
    private Texture2D? HandCursorTexture;
    private bool MetaSyncStarted;
    private RenderTarget2D RenderTarget = null!;
    private bool ResizingInProgress;
    private CancellationTokenSource? LatencyPollCts;
    private int WindowSizeMultiplier;
    private SpriteBatch SpriteBatch = null!;

    /// <summary>
    ///     Input dispatcher that routes mouse and keyboard events to UI elements via hit-testing and focus routing.
    /// </summary>
    public InputDispatcher Dispatcher { get; private set; } = null!;

    /// <summary>
    ///     The screen manager that owns the active screen stack.
    /// </summary>
    public ScreenManager Screens { get; private set; } = null!;

    public bool UseHandCursor { get; set; }

    /// <summary>
    ///     Shared aisling renderer for compositing player/NPC equipment layers.
    /// </summary>
    public AislingRenderer AislingRenderer { get; } = new();

    /// <summary>
    ///     The connection manager that orchestrates lobby, login, and world connections.
    /// </summary>
    public ConnectionManager Connection { get; }

    /// <summary>
    ///     Shared creature sprite renderer with per-frame texture cache.
    /// </summary>
    public CreatureRenderer CreatureRenderer { get; } = new();

    /// <summary>
    ///     Shared spell/effect animation renderer with per-frame texture cache.
    /// </summary>
    public EffectRenderer EffectRenderer { get; } = new();

    /// <summary>
    ///     Shared item sprite renderer with frame offset metadata. Evicted on map change.
    /// </summary>
    public ItemRenderer ItemRenderer { get; } = new();

    /// <summary>
    ///     Manages sound effect and music playback.
    /// </summary>
    public SoundSystem SoundSystem { get; } = new();

    public static GraphicsDevice Device => TextureConverter.Device;

    public ChaosGame()
    {
        //sdl by default is polling all possible input devices
        //some devices apparently don't like to always respond in a timely manner
        //when this occurs it causes the entire application to hang
        //to remedy this, we use this to disable polling of extraneous devices
        Sdl.SDL_QuitSubSystem(
            Sdl.SDL_INIT_JOYSTICK
            | Sdl.SDL_INIT_GAMECONTROLLER
            | Sdl.SDL_INIT_HAPTIC
            | Sdl.SDL_INIT_SENSOR);

        //open at the user's saved window size (chosen in the launcher), falling back to the default multiple of 640×480.
        //the display can't be queried yet (no window exists), so this isn't clamped here — the launcher's selector is the
        //display-aware path, and the OS clamps an oversized window if the saved size no longer fits.
        var startupMultiplier = Math.Max(1, LauncherConfig.WindowMultiplier ?? DEFAULT_WINDOW_MULTIPLIER);
        WindowSizeMultiplier = startupMultiplier;

        Graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = VIRTUAL_WIDTH * startupMultiplier,
            PreferredBackBufferHeight = VIRTUAL_HEIGHT * startupMultiplier,
            PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8,
            SynchronizeWithVerticalRetrace = false
        };

        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
        InactiveSleepTime = TimeSpan.Zero;

        Connection = new ConnectionManager();
        //the friendly server name drives the window-title suffix; refresh from the write itself so no caller has to remember
        Connection.ServerNameChanged += UpdateWindowTitle;
        Connection.OnMetaData += HandleMetaData;
        Connection.OnWorldEntryComplete += () => Connection.SendMetaDataRequest(MetaDataRequestType.AllCheckSums);
        Connection.StateChanged += OnConnectionStateChanged;

        //wire state events to worldstate at startup so state is tracked
        //even during world entry (before worldscreen is created)
        WorldState.SubscribeTo(Connection);
        Connection.OnDisplayVisibleEntities += WorldState.AddOrUpdateVisibleEntities;
        Connection.OnDisplayAisling += WorldState.AddOrUpdateAisling;

        //removeentity wired in worldscreen — it needs to capture the creature sprite for
        //the death dissolve animation before removing the entity from worldstate.
        //fallback for non-world screens (e.g., during world entry before worldscreen exists).
        Connection.OnRemoveEntity += id =>
        {
            if (Screens.ActiveScreen is not WorldScreen)
                WorldState.RemoveEntity(id);
        };

        Connection.OnCreatureWalk += (
            id,
            oldX,
            oldY,
            dir) =>
        {
            var entity = WorldState.GetEntity(id);
            var walkFrames = entity is not null && (entity.SpriteId > 0) ? CreatureRenderer.GetWalkFrameCount(entity.SpriteId) : null;

            WorldState.HandleCreatureWalk(
                id,
                oldX,
                oldY,
                dir,
                walkFrames);
        };
        Connection.OnCreatureTurn += (id, dir) => WorldState.HandleCreatureTurn(id, dir);

        UpdateWindowTitle();
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
    }

    protected override void Draw(GameTime gameTime)
    {
        //world / background layer — rendered at virtual 640×480 resolution into the render target
        GraphicsDevice.SetRenderTarget(RenderTarget);
        GraphicsDevice.Clear(Color.Black);
        Screens.Draw(SpriteBatch, gameTime);

        if (DebugOverlay.IsActive)
            DebugOverlay.DrawStats(SpriteBatch);

        //capture screenshot while the render target is still bound — this grabs the world layer only (the stylized
        //"lod" capture is intentionally UI-free). DiscardContents may invalidate pixel data after SetRenderTarget(null).
        if (ScreenshotRequested)
        {
            ScreenshotRequested = false;
            SaveScreenshot();
        }

        //stretch the virtual 640×480 render target to fill the window. when 4:3 is locked this
        //fills perfectly; when the user has maximized to a non-4:3 window the image stretches
        //to cover the full backbuffer (by design — "maximize covers the whole screen").
        GraphicsDevice.SetRenderTarget(null);
        SpriteBatch.Begin(samplerState: GlobalSettings.Sampler);
        SpriteBatch.Draw(RenderTarget, GraphicsDevice.Viewport.Bounds, Color.White);
        SpriteBatch.End();

        //native layer — world-anchored overlays and the UI are drawn directly to the backbuffer at native resolution
        //so text is crisp rather than sharing the render target's point-upscale. Panel/overlay sprites point-scale
        //identically under the transform; FontEngine rasterizes glyphs at native size and cancels the transform per
        //glyph (see FontEngine.DrawLine). The screen issues its own passes; we just set the scale and draw the cursor.
        var pp = GraphicsDevice.PresentationParameters;
        var scaleX = (float)pp.BackBufferWidth / VIRTUAL_WIDTH;
        var scaleY = (float)pp.BackBufferHeight / VIRTUAL_HEIGHT;

        FontEngine.Instance.SetNativeScale(scaleX, scaleY);
        Screens.DrawNative(SpriteBatch, scaleX, scaleY);

        //custom cursor — topmost, in virtual space; the pass transform scales it to native like the rest of the UI
        if (CursorTexture is not null)
        {
            var activeCursor = UseHandCursor && HandCursorTexture is not null ? HandCursorTexture : CursorTexture;
            var offsetX = UseHandCursor && HandCursorTexture is not null ? HandCursorOffsetX : CursorOffsetX;
            var offsetY = UseHandCursor && HandCursorTexture is not null ? HandCursorOffsetY : CursorOffsetY;

            SpriteBatch.Begin(samplerState: GlobalSettings.Sampler, transformMatrix: Matrix.CreateScale(scaleX, scaleY, 1f));
            SpriteBatch.Draw(activeCursor, new Vector2(InputBuffer.MouseX - offsetX, InputBuffer.MouseY - offsetY), Color.White);
            SpriteBatch.End();
        }

        FontEngine.Instance.SetNativeScale(1f, 1f);

        base.Draw(gameTime);

        DebugOverlay.EndFrame();
    }

    protected override void EndDraw()
    {
        base.EndDraw();

        if (GcRequested)
        {
            GcRequested = false;

            GC.Collect(
                2,
                GCCollectionMode.Aggressive,
                true,
                true);

            GC.WaitForPendingFinalizers();
        }
    }

    public void RequestScreenshot() => ScreenshotRequested = true;

    private void SaveScreenshot()
    {
        var dataPath = GlobalSettings.DataPath;
        var highestNumber = 0;

        foreach (var file in Directory.EnumerateFiles(dataPath, "lod*.*"))
        {
            var name = Path.GetFileNameWithoutExtension(file);

            if ((name.Length >= 4) && int.TryParse(name.AsSpan(3), out var num) && (num > highestNumber))
                highestNumber = num;
        }

        var nextNumber = highestNumber + 1;
        var fileName = Path.Combine(dataPath, $"lod{nextNumber:D3}.png");

        var pixels = new Color[VIRTUAL_WIDTH * VIRTUAL_HEIGHT];
        RenderTarget.GetData(pixels);

        var imageInfo = new SKImageInfo(VIRTUAL_WIDTH, VIRTUAL_HEIGHT, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var sourceImage = SKImage.FromPixelCopy(
            imageInfo,
            MemoryMarshal.AsBytes(pixels.AsSpan()),
            VIRTUAL_WIDTH * 4);

        using var intermediary = ImageProcessor.PreserveNonTransparentBlacks(sourceImage);
        using var quantized = ImageProcessor.Quantize(QuantizerOptions.Default, intermediary);
        var palette = quantized.Palette;
        var indices = quantized.Entity.GetPalettizedPixelData(palette);

        var rgbPalette = new List<uint>(palette.Count);

        for (var i = 0; i < palette.Count; i++)
        {
            var c = palette[i];
            rgbPalette.Add(((uint)c.Red << 16) | ((uint)c.Green << 8) | c.Blue);
        }

        WritePalettizedPng(fileName, VIRTUAL_WIDTH, VIRTUAL_HEIGHT, indices, rgbPalette);
    }

    private static void WritePalettizedPng(string fileName, int width, int height, byte[] indices, List<uint> palette)
    {
        using var file = File.Create(fileName);

        //PNG signature
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        //IHDR — width, height, 8-bit indexed color
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; //bit depth
        ihdr[9] = 3; //color type: indexed
        WritePngChunk(file, "IHDR"u8, ihdr);

        //PLTE — RGB triplets
        var plte = new byte[palette.Count * 3];

        for (var i = 0; i < palette.Count; i++)
        {
            var rgb = palette[i];
            plte[i * 3] = (byte)(rgb >> 16);
            plte[i * 3 + 1] = (byte)(rgb >> 8);
            plte[i * 3 + 2] = (byte)rgb;
        }

        WritePngChunk(file, "PLTE"u8, plte);

        //IDAT — zlib-compressed scanlines with no-filter bytes
        using var idatBuffer = new MemoryStream();

        using (var zlib = new ZLibStream(idatBuffer, CompressionLevel.Optimal, true))
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0); //filter: none
                zlib.Write(indices, y * width, width);
            }

        WritePngChunk(file, "IDAT"u8, idatBuffer.ToArray());

        //IEND
        WritePngChunk(file, "IEND"u8, []);
    }

    private static void WritePngChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> buf = stackalloc byte[4];

        //chunk length (big-endian)
        BinaryPrimitives.WriteInt32BigEndian(buf, data.Length);
        stream.Write(buf);

        //chunk type
        stream.Write(type);

        //chunk data
        stream.Write(data);

        //CRC32 over type + data (PNG uses the standard CRC32 polynomial)
        var crc = 0xFFFFFFFFu;

        foreach (var b in type)
            crc = PngCrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);

        foreach (var b in data)
            crc = PngCrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);

        BinaryPrimitives.WriteUInt32BigEndian(buf, crc ^ 0xFFFFFFFF);
        stream.Write(buf);
    }

    private static readonly uint[] PngCrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (uint n = 0; n < 256; n++)
        {
            var c = n;

            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;

            table[n] = c;
        }

        return table;
    }

    private static (int X, int Y) FindCursorHotspot(Texture2D texture)
    {
        var pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);

        var hotX = texture.Width;
        var hotY = texture.Height;

        for (var y = 0; y < texture.Height; y++)
            for (var x = 0; x < texture.Width; x++)
                if (pixels[y * texture.Width + x].A > 0)
                {
                    if (x < hotX)
                        hotX = x;

                    if (y < hotY)
                        hotY = y;
                }

        return (hotX, hotY);
    }

    protected override void Initialize()
    {
        base.Initialize();

        //set the window icon here (not in the ctor) — the SDL window doesn't exist until Run() creates it, so
        //Window.Handle is only valid once base.Initialize() has run
        SetWindowIcon();

        Window.ClientSizeChanged += OnClientSizeChanged;
    }

    protected override void LoadContent()
    {
        SpriteBatch = new SpriteBatch(GraphicsDevice);

        RenderTarget = new RenderTarget2D(
            GraphicsDevice,
            VIRTUAL_WIDTH,
            VIRTUAL_HEIGHT,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24Stencil8);
        InputBuffer.Initialize();
        Dispatcher = new InputDispatcher();
        Screens = new ScreenManager(this);

        TextureConverter.Device = GraphicsDevice;
        //construct FontEngine up front so the per-frame native UI pass always has a non-null FontEngine.Instance, even
        //while the launcher is up (it draws via SystemFontText, not FontEngine). Starts on the default face; the saved
        //face is applied in FinishAssetInitialization once ClientSettings has loaded.
        FontEngine.Initialize();
        UiRenderer.Instance = new UiRenderer(GraphicsDevice);

        //the launcher (server select + asset path) renders without any DA assets and is shown on every normal launch;
        //the asset-dependent startup is deferred to FinishAssetInitialization until the user connects with a usable path.
        if (GlobalSettings.ShowLauncher)
            Screens.Switch(new LauncherScreen());
        else
            FinishAssetInitialization();
    }

    /// <summary>
    ///     Completes the asset-path-dependent startup once a usable asset path is known (resolved from config/env at
    ///     launch, or chosen in <see cref="LauncherScreen" />): loads the data context, client settings, fonts, and
    ///     cursor, then switches to the lobby/login screen. Called exactly once.
    /// </summary>
    public void FinishAssetInitialization()
    {
        GlobalSettings.InitializeAssetData();
        Directory.CreateDirectory(MetaFilePath);
        ClientSettings.Load();
        //LoadContent constructed FontEngine early (with the default face) so the launcher's frames have a non-null
        //Instance; now that ClientSettings is loaded, switch to the user's saved face before the lobby appears.
        FontEngine.Instance.SetActiveFont(ClientSettings.FontIndex);
        LoadCustomCursor();
        Screens.Switch(new LobbyLoginScreen());
    }

    private void LoadCustomCursor()
    {
        CursorTexture = UiRenderer.Instance!.GetEpfTexture("mouse.epf", 0);

        if (CursorTexture is not null)
        {
            IsMouseVisible = false;
            (CursorOffsetX, CursorOffsetY) = FindCursorHotspot(CursorTexture);
        }

        HandCursorTexture = UiRenderer.Instance.GetEpfTexture("mouse.epf", 1);

        if (HandCursorTexture is not null)
            (HandCursorOffsetX, HandCursorOffsetY) = FindCursorHotspot(HandCursorTexture);
    }

    #region Window Sizing
    /// <summary>Current window size as an integer multiple of the 640×480 virtual resolution.</summary>
    internal int CurrentWindowMultiplier => WindowSizeMultiplier;

    /// <summary>
    ///     Largest integer multiple of the virtual resolution whose window still fits the current display, or
    ///     <see cref="DEFAULT_WINDOW_MULTIPLIER" /> as a fallback when the display can't be queried (never below 1).
    /// </summary>
    internal int MaxWindowMultiplierForDisplay()
    {
        var displayIndex = Sdl.SDL_GetWindowDisplayIndex(Window.Handle);

        if ((displayIndex < 0) || (Sdl.SDL_GetDisplayBounds(displayIndex, out var bounds) < 0))
            return DEFAULT_WINDOW_MULTIPLIER;

        return Math.Max(1, Math.Min(bounds.W / VIRTUAL_WIDTH, bounds.H / VIRTUAL_HEIGHT));
    }

    /// <summary>
    ///     Resizes the window to <paramref name="multiplier" />× the 640×480 virtual resolution, clamped to what fits the
    ///     current display. Leaves any maximized state so the OS window actually resizes. Returns the applied multiplier.
    /// </summary>
    internal int SetWindowMultiplier(int multiplier)
    {
        var clamped = Math.Clamp(multiplier, 1, MaxWindowMultiplierForDisplay());

        WindowSizeMultiplier = clamped;

        ResizingInProgress = true;

        //leave maximized state so the backbuffer resize actually shrinks the OS window
        if ((Sdl.SDL_GetWindowFlags(Window.Handle) & Sdl.SDL_WINDOW_MAXIMIZED) != 0)
            Sdl.SDL_RestoreWindow(Window.Handle);

        Graphics.PreferredBackBufferWidth = VIRTUAL_WIDTH * clamped;
        Graphics.PreferredBackBufferHeight = VIRTUAL_HEIGHT * clamped;
        Graphics.ApplyChanges();
        ResizingInProgress = false;

        return clamped;
    }

    /// <summary>
    ///     Cycles the window through integer multipliers of the virtual resolution (640×480).
    ///     Advances to the next multiplier if it fits on the current monitor, otherwise wraps to 1×.
    /// </summary>
    internal void CycleWindowSize()
    {
        var next = WindowSizeMultiplier + 1;

        if (next > MaxWindowMultiplierForDisplay())
            next = 1;

        SetWindowMultiplier(next);
    }

    /// <summary>
    ///     Corrects the window size after a resize to enforce 4:3 aspect ratio.
    ///     Uses the larger dimension as the reference and adjusts the other.
    /// </summary>
/// <summary>
    ///     Returns the centered, integer-rounded rectangle of virtual 4:3 content inside a
    ///     backbuffer of the given size. Equal to the full backbuffer when it's already 4:3;
    ///     pillarboxes (wider windows) or letterboxes (taller windows) otherwise.
    /// </summary>
    

    private void OnClientSizeChanged(object? sender, EventArgs e)
    {
        if (ResizingInProgress)
            return;

        var width = Window.ClientBounds.Width;
        var height = Window.ClientBounds.Height;

        if ((width <= 0) || (height <= 0))
            return;

        //maximize button → fill the full monitor work area; skip 4:3 correction and let the
        //Draw path letterbox the 640×480 render target inside the non-4:3 window.
        var flags = Sdl.SDL_GetWindowFlags(Window.Handle);

        if ((flags & Sdl.SDL_WINDOW_MAXIMIZED) != 0)
            return;

        //determine corrected dimensions preserving 4:3
        var correctedWidth = (int)(height * ASPECT_RATIO);
        var correctedHeight = (int)(width / ASPECT_RATIO);

        int newWidth,
            newHeight;

        if (correctedWidth <= width)
        {
            //height is the constraining dimension
            newWidth = correctedWidth;
            newHeight = height;
        } else
        {
            //width is the constraining dimension
            newWidth = width;
            newHeight = correctedHeight;
        }

        if ((newWidth == width) && (newHeight == height))
            return;

        ResizingInProgress = true;

        Graphics.PreferredBackBufferWidth = newWidth;
        Graphics.PreferredBackBufferHeight = newHeight;
        Graphics.ApplyChanges();

        ResizingInProgress = false;
    }
    #endregion Window Sizing

    /// <summary>
    ///     Fired when all metadata files are up to date with the server.
    /// </summary>
    public event MetaDataSyncCompleteHandler? OnMetaDataSyncComplete;

    private const int LATENCY_POLL_INTERVAL_MS = 2000;

    private void OnConnectionStateChanged(ConnectionState oldState, ConnectionState newState)
    {
        if (newState == ConnectionState.World)
        {
            LatencyPollCts?.Cancel();
            LatencyPollCts?.Dispose();
            LatencyPollCts = new CancellationTokenSource();
            var token = LatencyPollCts.Token;
            _ = Task.Run(() => PollTcpLatencyAsync(token), token);
        } else if (oldState == ConnectionState.World)
        {
            LatencyPollCts?.Cancel();
            LatencyPollCts?.Dispose();
            LatencyPollCts = null;
            LatencyMonitor.Update(null);
        }
    }

    private async Task PollTcpLatencyAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            LatencyMonitor.Update(Connection.Client.TryGetTcpSmoothedRttMs(out var rttMs) ? rttMs : null);

            try
            {
                await Task.Delay(LATENCY_POLL_INTERVAL_MS, token);
            } catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    protected override void UnloadContent()
    {
        Window.ClientSizeChanged -= OnClientSizeChanged;
        CursorTexture?.Dispose();
        RenderTarget.Dispose();
        Screens.Dispose();
        Connection.Dispose();
        InputBuffer.Shutdown();
        CreatureRenderer.Dispose();
        AislingRenderer.Dispose();
        EffectRenderer.Dispose();
        ItemRenderer.Dispose();
        SoundSystem.Dispose();
        UiRenderer.Instance?.Dispose();
        UiRenderer.Instance = null;
        base.UnloadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        DebugOverlay.BeginFrame();

        //compute mouse coordinate transform. the render target is stretched to fill the
        //backbuffer, so the raw→virtual scale is per-axis — equal on both axes when 4:3 is
        //locked, unequal when the user has maximized to a non-4:3 window.
        var ppt = GraphicsDevice.PresentationParameters;
        var scaleX = (float)ppt.BackBufferWidth / VIRTUAL_WIDTH;
        var scaleY = (float)ppt.BackBufferHeight / VIRTUAL_HEIGHT;
        InputBuffer.SetVirtualScale(scaleX, scaleY);

        //keep text measurement in sync with the native draw size so right-aligned multi-glyph values land flush
        FontEngine.Instance.SetLayoutScale(scaleX, scaleY);

        //freeze buffered input for this frame before anything reads it
        InputBuffer.Update(IsActive);

        //f11 — toggle debug overlay (handled globally before screen update)
        if (InputBuffer.WasKeyPressed(Keys.F11))
            DebugOverlay.Toggle();

        //debug key layer — numpad acts as per-feature toggles, but only while the overlay is up. Gating the reads on
        //IsActive keeps the numpad free for normal use when debug is off.
        if (DebugOverlay.IsActive)
        {
            if (InputBuffer.WasKeyPressed(Keys.NumPad1))
                DebugOverlay.ShowUiBoxes ^= true;

            if (InputBuffer.WasKeyPressed(Keys.NumPad2))
                DebugOverlay.ShowUiNames ^= true;

            if (InputBuffer.WasKeyPressed(Keys.NumPad3))
                DebugOverlay.ShowWorld ^= true;

            if (InputBuffer.WasKeyPressed(Keys.NumPad4))
                DebugOverlay.ShowPerf ^= true;

            //numpad 0 — restore all sub-toggles (escape hatch after muting individual layers)
            if (InputBuffer.WasKeyPressed(Keys.NumPad0))
                DebugOverlay.ShowUiBoxes = DebugOverlay.ShowUiNames = DebugOverlay.ShowWorld = DebugOverlay.ShowPerf = true;
        }

        //f12 — screenshot
        if (InputBuffer.WasKeyPressed(Keys.F12))
            RequestScreenshot();

        DebugOverlay.Update(gameTime);

        //pump audio decodes and reset the same-frame dedup window before any handler can trigger sounds
        SoundSystem.Update();

        //drain and process network packets each frame
        PacketBuffer.Clear();
        Connection.ProcessPackets(PacketBuffer);

        Screens.Update(gameTime);

        base.Update(gameTime);
    }

    #region Metadata Sync
    private uint ComputeLocalMetaCheckSum(string name)
    {
        var filePath = Path.Combine(MetaFilePath, name);

        if (!File.Exists(filePath))
            return 0;

        try
        {
            using var fileStream = File.OpenRead(filePath);
            using var zlibStream = new ZLibStream(fileStream, CompressionMode.Decompress);
            using var memoryStream = new MemoryStream();

            zlibStream.CopyTo(memoryStream);

            //retail + Hybrasyl both send standard (inverted) CRC-32 — verified against the retail checksum routine
            return CRC32.Calculate(memoryStream.ToArray());
        } catch
        {
            return 0;
        }
    }

    private void HandleMetaData(ServerPackets.MetafilePacket pkt)
    {
        switch (pkt)
        {
            case ServerPackets.MetafileChecksumsPacket checksums:
                HandleMetaDataCheckSums(checksums.Entries);

                break;

            case ServerPackets.MetafileDataPacket data:
                HandleMetaDataFileData(data);

                break;
        }
    }

    private void HandleMetaDataCheckSums(IList<ServerPackets.MetafileEntry>? collection)
    {
        if (collection is null || (collection.Count == 0))
        {
            OnMetaDataSyncComplete?.Invoke();

            return;
        }

        MetaPendingChecksums.Clear();
        MetaSyncStarted = true;

        foreach (var info in collection)
        {
            var localCheckSum = ComputeLocalMetaCheckSum(info.Name);

            if (localCheckSum != info.Checksum)
                MetaPendingChecksums[info.Name] = info.Checksum;
        }

        foreach (var name in MetaPendingChecksums.Keys)
            Connection.SendMetaDataRequest(MetaDataRequestType.DataByName, name);

        if (MetaPendingChecksums.Count == 0)
            OnMetaDataSyncComplete?.Invoke();
    }

    private void HandleMetaDataFileData(ServerPackets.MetafileDataPacket info)
    {
        if (string.IsNullOrEmpty(info.Name) || (info.Data.Length == 0))
            return;

        File.WriteAllBytes(Path.Combine(MetaFilePath, info.Name), info.Data);
        MetaPendingChecksums.Remove(info.Name);

        if (MetaSyncStarted && (MetaPendingChecksums.Count == 0))
            OnMetaDataSyncComplete?.Invoke();
    }
    #endregion
}