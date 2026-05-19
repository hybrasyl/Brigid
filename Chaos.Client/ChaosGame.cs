#region
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Chaos.Client.Collections;
using DALib.Utility;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Networking;
using Chaos.Client.Networking.Definitions;
using Chaos.Client.Screens;
using Chaos.Client.Systems;
using Chaos.Cryptography;
using Chaos.DarkAges.Definitions;
using Chaos.Networking.Entities.Server;
using DALib.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SkiaSharp;
#endregion

namespace Chaos.Client;

public sealed class ChaosGame : Game
{
    public const int VIRTUAL_WIDTH = 640;
    public const int VIRTUAL_HEIGHT = 480;
    private const float ASPECT_RATIO = (float)VIRTUAL_WIDTH / VIRTUAL_HEIGHT;

    private readonly GraphicsDeviceManager Graphics;
    private readonly string MetaFilePath = Path.Combine(GlobalSettings.DataPath, "metafile");
    private readonly Dictionary<string, uint> MetaPendingChecksums = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ServerPacket> PacketBuffer = [];
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
    private int WindowSizeMultiplier = 1;
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

        ClientSettings.Load();

        Graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = VIRTUAL_WIDTH,
            PreferredBackBufferHeight = VIRTUAL_HEIGHT,
            PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8,
            SynchronizeWithVerticalRetrace = false
        };

        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
        InactiveSleepTime = TimeSpan.Zero;

        Connection = new ConnectionManager();
        Directory.CreateDirectory(MetaFilePath);
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

        Window.Title = "Darkages";
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
    }

    protected override void Draw(GameTime gameTime)
    {
        //render everything at virtual resolution
        GraphicsDevice.SetRenderTarget(RenderTarget);
        GraphicsDevice.Clear(Color.Black);
        Screens.Draw(SpriteBatch, gameTime);

        if (DebugOverlay.IsActive)
            DebugOverlay.DrawStats(SpriteBatch);

        //custom cursor — drawn in virtual space so it aligns with game content
        if (CursorTexture is not null)
        {
            var activeCursor = UseHandCursor && HandCursorTexture is not null ? HandCursorTexture : CursorTexture;
            var offsetX = UseHandCursor && HandCursorTexture is not null ? HandCursorOffsetX : CursorOffsetX;
            var offsetY = UseHandCursor && HandCursorTexture is not null ? HandCursorOffsetY : CursorOffsetY;

            SpriteBatch.Begin(samplerState: GlobalSettings.Sampler);
            SpriteBatch.Draw(activeCursor, new Vector2(InputBuffer.MouseX - offsetX, InputBuffer.MouseY - offsetY), Color.White);
            SpriteBatch.End();
        }

        //capture screenshot while the render target is still bound — DiscardContents may
        //invalidate pixel data after SetRenderTarget(null) on some drivers
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
        FontAtlas.Initialize(GraphicsDevice);
        UiRenderer.Instance = new UiRenderer(GraphicsDevice);

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
    /// <summary>
    ///     Cycles the window through integer multipliers of the virtual resolution (640x480).
    ///     Advances to the next multiplier if it fits on the current monitor, otherwise wraps to 1x.
    /// </summary>
    internal void CycleWindowSize()
    {
        var displayIndex = Sdl.SDL_GetWindowDisplayIndex(Window.Handle);

        if ((displayIndex < 0) || (Sdl.SDL_GetDisplayBounds(displayIndex, out var bounds) < 0))
            return;

        var nextMultiplier = WindowSizeMultiplier + 1;
        var nextWidth = VIRTUAL_WIDTH * nextMultiplier;
        var nextHeight = VIRTUAL_HEIGHT * nextMultiplier;

        if ((nextWidth > bounds.W) || (nextHeight > bounds.H))
        {
            nextMultiplier = 1;
            nextWidth = VIRTUAL_WIDTH;
            nextHeight = VIRTUAL_HEIGHT;
        }

        WindowSizeMultiplier = nextMultiplier;

        ResizingInProgress = true;

        //leave maximized state so the backbuffer resize actually shrinks the OS window
        if ((Sdl.SDL_GetWindowFlags(Window.Handle) & Sdl.SDL_WINDOW_MAXIMIZED) != 0)
            Sdl.SDL_RestoreWindow(Window.Handle);

        Graphics.PreferredBackBufferWidth = nextWidth;
        Graphics.PreferredBackBufferHeight = nextHeight;
        Graphics.ApplyChanges();
        ResizingInProgress = false;
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

    private void OnConnectionStateChanged(ConnectionState oldState, ConnectionState newState)
    {
        if (newState == ConnectionState.World)
            LatencyMonitor.Start(Connection.Client);
        else if (oldState == ConnectionState.World)
            LatencyMonitor.Stop();
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

        //freeze buffered input for this frame before anything reads it
        InputBuffer.Update(IsActive);

        //f11 — toggle debug overlay (handled globally before screen update)
        if (InputBuffer.WasKeyPressed(Keys.F11))
            DebugOverlay.Toggle();

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

            return Crc.Generate32(memoryStream.ToArray());
        } catch
        {
            return 0;
        }
    }

    private void HandleMetaData(MetaDataArgs args)
    {
        switch (args.MetaDataRequestType)
        {
            case MetaDataRequestType.AllCheckSums:
                HandleMetaDataCheckSums(args.MetaDataCollection);

                break;

            case MetaDataRequestType.DataByName:
                HandleMetaDataFileData(args.MetaDataInfo);

                break;
        }
    }

    private void HandleMetaDataCheckSums(ICollection<MetaDataInfo>? collection)
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

            if (localCheckSum != info.CheckSum)
                MetaPendingChecksums[info.Name] = info.CheckSum;
        }

        foreach (var name in MetaPendingChecksums.Keys)
            Connection.SendMetaDataRequest(MetaDataRequestType.DataByName, name);

        if (MetaPendingChecksums.Count == 0)
            OnMetaDataSyncComplete?.Invoke();
    }

    private void HandleMetaDataFileData(MetaDataInfo? info)
    {
        if (info is null || string.IsNullOrEmpty(info.Name) || (info.Data.Length == 0))
            return;

        File.WriteAllBytes(Path.Combine(MetaFilePath, info.Name), info.Data);
        MetaPendingChecksums.Remove(info.Name);

        if (MetaSyncStarted && (MetaPendingChecksums.Count == 0))
            OnMetaDataSyncComplete?.Invoke();
    }
    #endregion
}