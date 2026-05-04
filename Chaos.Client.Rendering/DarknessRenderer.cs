#region
using System.Runtime.InteropServices;
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.DarkAges.Definitions;
using DALib.Drawing;
using DALib.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Manages the darkness overlay system: light metadata parsing, HEA file loading, per-pixel texture generation, and
///     overlay drawing. Supports both flat color overlays and per-pixel HEA light maps.
/// </summary>
public sealed class DarknessRenderer : IDisposable
{
    private readonly GraphicsDevice Device;

    private float Alpha;
    private int CacheBaseY;
    private byte[,]? CachedLayer0;
    private byte[,]? CachedLayer1;
    private int CachedLayerIndex0 = -1;
    private int CachedLayerIndex1 = -1;
    private bool CacheValid;
    private short CurrentMapId;
    private string CurrentLightType = "default";
    private Color DarknessColor;
    private HeaFile? HeaFile;
    private bool IsDarkMap;
    private LightLevel? LastLightLevel;
    private int LastLightSourceHash;
    private int LastOffsetX = int.MinValue;
    private int LastOffsetY = int.MinValue;
    private LightMetadata? LightData;
    private Color[]? Pixels;
    private Texture2D? Texture;

    /// <summary>
    ///     Whether a per-pixel HEA light map is loaded for the current map.
    /// </summary>
    public bool HasHeaFile => HeaFile is not null;

    /// <summary>
    ///     Whether darkness is currently active (alpha > 0).
    /// </summary>
    public bool IsActive => Alpha > 0f;

    /// <summary>
    ///     True only when the map is dark, no graduated light metadata reduced the alpha, and the resulting
    ///     darkness color is pure black. The tab map uses this signal to switch into limited visibility mode.
    /// </summary>
    public bool IsFullBlackDark => IsDarkMap && (Alpha >= 1f) && (DarknessColor == Color.Black);

    /// <summary>
    ///     True when loaded light metadata explicitly assigns a light type to the current map. Gates all darkness
    ///     application: light level packets and the dark-map fallback only take effect when this is true. Maps without
    ///     an explicit metadata entry render without any darkness overlay regardless of the server-sent Darkness flag.
    /// </summary>
    private bool HasMetadataForCurrentMap => LightData?.MapLightTypes.ContainsKey(CurrentMapId) is true;

    public DarknessRenderer(GraphicsDevice device) => Device = device;

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeTexture();
        Pixels = null;
        CachedLayer0 = null;
        CachedLayer1 = null;
        CachedLayerIndex0 = -1;
        CachedLayerIndex1 = -1;
        CacheValid = false;
    }

    private void ComputePixelsFromCache(
        int heaOffsetX,
        int vpWidth,
        int vpHeight,
        byte ambientAlpha32)
    {
        var pixels = Pixels!;
        var darkR = DarknessColor.R;
        var darkG = DarknessColor.G;
        var darkB = DarknessColor.B;

        var l0Start = 0;
        var l0End = 0;
        var l1Start = 0;
        var l1End = 0;

        if (CachedLayerIndex0 >= 0)
        {
            l0Start = HeaFile!.Thresholds[CachedLayerIndex0];
            l0End = l0Start + HeaFile.GetLayerWidth(CachedLayerIndex0);
        }

        if (CachedLayerIndex1 >= 0)
        {
            l1Start = HeaFile!.Thresholds[CachedLayerIndex1];
            l1End = l1Start + HeaFile.GetLayerWidth(CachedLayerIndex1);
        }

        for (var vy = 0; vy < vpHeight; vy++)
        {
            for (var vx = 0; vx < vpWidth; vx++)
            {
                var heaX = heaOffsetX + vx;
                byte lightValue;

                if ((heaX < 0) || (heaX >= HeaFile!.ScanlineWidth))
                    lightValue = 0;
                else if ((CachedLayerIndex0 >= 0) && (heaX >= l0Start) && (heaX < l0End))
                    lightValue = CachedLayer0![vy, heaX - l0Start];
                else if ((CachedLayerIndex1 >= 0) && (heaX >= l1Start) && (heaX < l1End))
                    lightValue = CachedLayer1![vy, heaX - l1Start];
                else
                    lightValue = 0;

                var effective = Math.Max(ambientAlpha32, lightValue);
                var alpha = (byte)(255 - effective * 255 / 32);

                pixels[vy * vpWidth + vx] = new Color(
                    darkR,
                    darkG,
                    darkB,
                    alpha);
            }
        }
    }

    private void DecodeLayerRows(
        int layerIndex,
        int heaStartY,
        int startRow,
        int rowCount,
        byte[,] target)
    {
        var layerWidth = HeaFile!.GetLayerWidth(layerIndex);

        for (var row = startRow; row < (startRow + rowCount); row++)
        {
            var heaY = heaStartY + row;
            var rowSpan = MemoryMarshal.CreateSpan(ref target[row, 0], layerWidth);

            if ((heaY < 0) || (heaY >= HeaFile.ScanlineCount))
            {
                rowSpan.Clear();

                continue;
            }

            HeaFile.DecodeScanline(layerIndex, heaY, rowSpan);
        }
    }

    private (int LeftLayer, int RightLayer) DetermineViewportLayers(int heaOffsetX, int vpWidth)
    {
        var leftHeaX = Math.Max(0, heaOffsetX);
        var rightHeaX = Math.Min(HeaFile!.ScanlineWidth - 1, heaOffsetX + vpWidth - 1);

        if ((rightHeaX < 0) || (leftHeaX >= HeaFile.ScanlineWidth))
            return (-1, -1);

        var leftLayer = -1;
        var rightLayer = -1;

        for (var i = 0; i < HeaFile.LayerCount; i++)
        {
            var start = HeaFile.Thresholds[i];
            var end = start + HeaFile.GetLayerWidth(i);

            if ((leftHeaX >= start) && (leftHeaX < end))
                leftLayer = i;

            if ((rightHeaX >= start) && (rightHeaX < end))
                rightLayer = i;
        }

        return (leftLayer, rightLayer);
    }

    private void DisposeTexture()
    {
        Texture?.Dispose();
        Texture = null;
    }

    /// <summary>
    ///     Draws the darkness overlay. Uses per-pixel HEA texture if available, otherwise flat color.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, Rectangle viewport)
    {
        if (Alpha <= 0f)
            return;

        if (Texture is not null && !Texture.IsDisposed)
            spriteBatch.Draw(Texture, new Vector2(viewport.X, viewport.Y), Color.White);
        else
            RenderHelper.DrawRect(spriteBatch, viewport, DarknessColor * Alpha);
    }

    private void EnsureLayerCache(
        ref byte[,]? cache,
        out int cachedIndex,
        int neededIndex,
        int vpHeight)
    {
        if (neededIndex < 0)
        {
            cachedIndex = -1;

            return;
        }

        var layerWidth = HeaFile!.GetLayerWidth(neededIndex);

        if (cache is null || (cache.GetLength(0) != vpHeight) || (cache.GetLength(1) != layerWidth))
            cache = new byte[vpHeight, layerWidth];

        cachedIndex = neededIndex;
    }

    /// <summary>
    ///     Called when the server sends a LightLevel packet. The level is always cached so that a later metadata load
    ///     can refresh via <see cref="ReapplyLightLevel" />. The visual state is only updated when the current map has
    ///     an explicit entry in the light metadata; maps without metadata are left bright regardless of the packet.
    /// </summary>
    public void OnLightLevel(LightLevel lightLevel)
    {
        LastLightLevel = lightLevel;

        //skip visual updates until metadata for this map is available — once it loads, ReapplyLightLevel re-runs this
        //path with the cached level and the current state takes effect
        if (!HasMetadataForCurrentMap)
            return;

        var enumHex = ((byte)lightLevel).ToString("X");
        var key = $"{CurrentLightType}_{enumHex}".ToLowerInvariant();

        if (LightData!.LightProperties.TryGetValue(key, out var props) && (props.Alpha < 32))
        {
            Alpha = (32 - props.Alpha) / 32f;
            DarknessColor = new Color(props.R, props.G, props.B);
        } else if (IsDarkMap)
        {
            //dark map with no graduated entry for this level — pure black darkness
            Alpha = 1f;
            DarknessColor = Color.Black;
        } else
        {
            Alpha = 0f;
            DarknessColor = Color.Transparent;
        }

        //invalidate dirty tracking so pixels are recomputed with updated alpha/color
        LastOffsetX = int.MinValue;
        LastOffsetY = int.MinValue;

        if (HeaFile is null || (Alpha <= 0f))
        {
            DisposeTexture();
            CacheValid = false;
            CachedLayerIndex0 = -1;
            CachedLayerIndex1 = -1;
            CachedLayer0 = null;
            CachedLayer1 = null;
        }
    }

    /// <summary>
    ///     Called on map change. Looks up the map's light type and loads the HEA file if one exists. The
    ///     <c>MapFlags.Darkness</c> flag always produces pure black darkness on entry; light metadata only refines
    ///     this via <see cref="OnLightLevel" /> when the map has an explicit metadata entry.
    /// </summary>
    public void OnMapChanged(short mapId, bool isDarkMap)
    {
        IsDarkMap = isDarkMap;
        CurrentMapId = mapId;
        CurrentLightType = LightData?.MapLightTypes.TryGetValue(mapId, out var lightType) is true ? lightType : "default";

        //level is per-map state; the new map starts without a cached level until the server resends one
        LastLightLevel = null;

        //dark maps start dark immediately — light metadata can refine via OnLightLevel
        if (isDarkMap)
        {
            Alpha = 1f;
            DarknessColor = Color.Black;
        } else
        {
            Alpha = 0f;
            DarknessColor = Color.Transparent;
        }

        LastOffsetX = int.MinValue;
        LastOffsetY = int.MinValue;
        DisposeTexture();

        CacheValid = false;
        CachedLayerIndex0 = -1;
        CachedLayerIndex1 = -1;
        CachedLayer0 = null;
        CachedLayer1 = null;

        HeaFile = TryLoadHeaFile(mapId);
    }

    /// <summary>
    ///     Reapplies the last received light level. Called after metadata reload so the refreshed light type and
    ///     properties take effect. No-op when no level has been received for the current map.
    /// </summary>
    public void ReapplyLightLevel()
    {
        if (LastLightLevel is { } level)
            OnLightLevel(level);
    }

    private void RebuildFlatWithLights(Rectangle viewport, ReadOnlySpan<LightSource> sources)
    {
        var vpWidth = viewport.Width;
        var vpHeight = viewport.Height;

        if ((vpWidth <= 0) || (vpHeight <= 0))
            return;

        //dirty check — skip rebuild if light sources haven't changed
        if (LastOffsetX != int.MinValue)
            return;

        var pixelCount = vpWidth * vpHeight;

        if (Texture is null || Texture.IsDisposed || (Texture.Width != vpWidth) || (Texture.Height != vpHeight))
        {
            Texture?.Dispose();
            Texture = new Texture2D(Device, vpWidth, vpHeight);
        }

        if (Pixels is null || (Pixels.Length < pixelCount))
            Pixels = new Color[pixelCount];

        //fill with flat darkness — use same alpha encoding as computepixelsfromcache
        var ambientAlpha32 = (byte)(32 * (1f - Alpha));
        var flatAlpha = (byte)(255 - ambientAlpha32 * 255 / 32);

        var darkColor = new Color(
            DarknessColor.R,
            DarknessColor.G,
            DarknessColor.B,
            flatAlpha);

        for (var i = 0; i < pixelCount; i++)
            Pixels[i] = darkColor;

        //stamp light sources
        StampLightSources(sources, vpWidth, vpHeight);

        Texture.SetData(Pixels, 0, pixelCount);
        LastOffsetX = 0;
        LastOffsetY = 0;
    }

    private void RebuildTexture(Camera camera, Rectangle viewport, ReadOnlySpan<LightSource> sources)
    {
        if (HeaFile is null)
            return;

        int vpWidth,
            vpHeight;

        if (viewport != default)
        {
            vpWidth = viewport.Width;
            vpHeight = viewport.Height;
        } else
            return;

        var ambientAlpha32 = (byte)(32 * (1f - Alpha));

        var viewportTopLeft = camera.ScreenToWorld(Vector2.Zero);
        var worldOffsetX = (int)viewportTopLeft.X;
        var worldOffsetY = (int)viewportTopLeft.Y;

        //early return only if everything matches — viewport size changes (hud swap) must force a rebuild
        //even when the world offset happens to coincide with the previous frame's value
        if ((worldOffsetX == LastOffsetX)
            && (worldOffsetY == LastOffsetY)
            && Texture is not null
            && !Texture.IsDisposed
            && (Texture.Width == vpWidth)
            && (Texture.Height == vpHeight))
            return;

        var heaOffsetX = worldOffsetX + HeaFile.ScreenWidth;
        var heaOffsetY = worldOffsetY + HeaFile.ScreenHeight;

        if (Texture is null || Texture.IsDisposed || (Texture.Width != vpWidth) || (Texture.Height != vpHeight))
        {
            Texture?.Dispose();
            Texture = new Texture2D(Device, vpWidth, vpHeight);
            CacheValid = false;
        }

        var pixelCount = vpWidth * vpHeight;

        if (Pixels is null || (Pixels.Length < pixelCount))
            Pixels = new Color[pixelCount];

        //determine which layers the viewport overlaps
        (var leftLayer, var rightLayer) = DetermineViewportLayers(heaOffsetX, vpWidth);

        //capture old indices before ensurelayercache mutates them — needed for layersmatch check
        var prevLayerIndex0 = CachedLayerIndex0;
        var prevLayerIndex1 = CachedLayerIndex1;

        //allocate/reuse layer caches
        EnsureLayerCache(
            ref CachedLayer0,
            out CachedLayerIndex0,
            leftLayer,
            vpHeight);

        if (rightLayer != leftLayer)
            EnsureLayerCache(
                ref CachedLayer1,
                out CachedLayerIndex1,
                rightLayer,
                vpHeight);
        else
        {
            CachedLayerIndex1 = -1;
            CachedLayer1 = null;
        }

        //check if incremental update is possible
        var dy = heaOffsetY - CacheBaseY;
        var expectedIndex1 = leftLayer != rightLayer ? rightLayer : -1;
        var layersMatch = (prevLayerIndex0 == leftLayer) && (prevLayerIndex1 == expectedIndex1);
        var canIncrement = CacheValid && layersMatch && (Math.Abs(dy) < vpHeight);

        if (canIncrement && (dy != 0))
        {
            //shift cached rows and decode only new rows
            if (CachedLayerIndex0 >= 0)
                ShiftAndDecodeRows(
                    CachedLayer0!,
                    CachedLayerIndex0,
                    heaOffsetY,
                    dy,
                    vpHeight);

            if (CachedLayerIndex1 >= 0)
                ShiftAndDecodeRows(
                    CachedLayer1!,
                    CachedLayerIndex1,
                    heaOffsetY,
                    dy,
                    vpHeight);
        } else if (!canIncrement)
        {
            //full decode — layers changed, large shift, or first frame
            if (CachedLayerIndex0 >= 0)
                DecodeLayerRows(
                    CachedLayerIndex0,
                    heaOffsetY,
                    0,
                    vpHeight,
                    CachedLayer0!);

            if (CachedLayerIndex1 >= 0)
                DecodeLayerRows(
                    CachedLayerIndex1,
                    heaOffsetY,
                    0,
                    vpHeight,
                    CachedLayer1!);
        }

        //else: canincrement && dy == 0 → only x shifted within same layers, cache is valid as-is

        CacheBaseY = heaOffsetY;
        CacheValid = true;

        //compute pixels from cache
        ComputePixelsFromCache(
            heaOffsetX,
            vpWidth,
            vpHeight,
            ambientAlpha32);

        //stamp lantern/dynamic light sources via max-blend
        StampLightSources(sources, vpWidth, vpHeight);

        Texture.SetData(Pixels, 0, pixelCount);
        LastOffsetX = worldOffsetX;
        LastOffsetY = worldOffsetY;
    }

    /// <summary>
    ///     Reloads light metadata from disk. Call after metadata sync completes. Recomputes the current light type
    ///     from the fresh metadata so that stale "default" fallback from pre-sync OnMapChanged is corrected.
    /// </summary>
    public void ReloadMetadata()
    {
        LightData = DataContext.MetaFiles.GetLightMetadata();
        CurrentLightType = LightData?.MapLightTypes.TryGetValue(CurrentMapId, out var lightType) is true ? lightType : "default";
    }

    private void ShiftAndDecodeRows(
        byte[,] cache,
        int layerIndex,
        int newHeaOffsetY,
        int dy,
        int vpHeight)
    {
        var layerWidth = HeaFile!.GetLayerWidth(layerIndex);
        var absDy = Math.Abs(dy);

        if (dy > 0)
        {
            //camera moved down — shift rows up, decode new rows at bottom
            Array.Copy(
                cache,
                dy * layerWidth,
                cache,
                0,
                (vpHeight - absDy) * layerWidth);

            DecodeLayerRows(
                layerIndex,
                newHeaOffsetY,
                vpHeight - absDy,
                absDy,
                cache);
        } else
        {
            //camera moved up — shift rows down, decode new rows at top
            Array.Copy(
                cache,
                0,
                cache,
                absDy * layerWidth,
                (vpHeight - absDy) * layerWidth);

            DecodeLayerRows(
                layerIndex,
                newHeaOffsetY,
                0,
                absDy,
                cache);
        }
    }

    private void StampLightSources(ReadOnlySpan<LightSource> sources, int vpWidth, int vpHeight)
    {
        if (sources.Length == 0)
            return;

        var pixels = Pixels!;
        var darkR = DarknessColor.R;
        var darkG = DarknessColor.G;
        var darkB = DarknessColor.B;

        for (var i = 0; i < sources.Length; i++)
        {
            var source = sources[i];
            var mask = source.PixelMask;

            //mask rect centered on screen position
            var maskLeft = (int)source.ScreenPosition.X - mask.Width / 2;
            var maskTop = (int)source.ScreenPosition.Y - mask.Height / 2;

            //clip to viewport
            var startX = Math.Max(0, -maskLeft);
            var startY = Math.Max(0, -maskTop);
            var endX = Math.Min(mask.Width, vpWidth - maskLeft);
            var endY = Math.Min(mask.Height, vpHeight - maskTop);

            if ((startX >= endX) || (startY >= endY))
                continue;

            for (var my = startY; my < endY; my++)
            {
                var vpY = maskTop + my;
                var maskRowOffset = my * mask.Width;
                var pixelRowOffset = vpY * vpWidth;

                for (var mx = startX; mx < endX; mx++)
                {
                    var maskValue = mask.Pixels[maskRowOffset + mx];

                    if (maskValue == 0)
                        continue;

                    var vpX = maskLeft + mx;
                    var pixelIndex = pixelRowOffset + vpX;

                    //reverse the existing alpha to get the current effective 0-32 value
                    var currentAlpha = pixels[pixelIndex].A;
                    var currentEffective = (byte)(32 - currentAlpha * 32 / 255);

                    if (maskValue <= currentEffective)
                        continue;

                    //recompute the pixel with the brighter value
                    var newAlpha = (byte)(255 - maskValue * 255 / 32);

                    pixels[pixelIndex] = new Color(
                        darkR,
                        darkG,
                        darkB,
                        newAlpha);
                }
            }
        }
    }

    private static HeaFile? TryLoadHeaFile(short mapId)
    {
        var heaName = $"{mapId:D6}";

        if (!DatArchives.Seo.TryGetValue(heaName.WithExtension(".hea"), out var entry))
            return null;

        try
        {
            return HeaFile.FromEntry(entry);
        }
        //rule 6 exemption: corrupt asset -> graceful null fallback (no validate-before-parse path)
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Rebuilds the darkness overlay texture for the current viewport. Handles both HEA-based per-pixel
    ///     light maps and flat overlays with dynamic light sources. Call each frame before <see cref="Draw" />.
    /// </summary>
    public void Update(Camera camera, Rectangle viewport, ReadOnlySpan<LightSource> sources)
    {
        if (Alpha <= 0f)
            return;

        var hash = ComputeSourceHash(sources);

        if (hash != LastLightSourceHash)
        {
            LastLightSourceHash = hash;
            LastOffsetX = int.MinValue;
            LastOffsetY = int.MinValue;
        }

        if (HeaFile is not null)
            RebuildTexture(camera, viewport, sources);
        else if (sources.Length > 0)
            RebuildFlatWithLights(viewport, sources);
        else
        {
            //no sources, no HEA — fall back to flat-rect draw, release the texture
            DisposeTexture();
            Pixels = null;
        }
    }

    private static int ComputeSourceHash(ReadOnlySpan<LightSource> sources)
    {
        var hash = sources.Length;

        for (var i = 0; i < sources.Length; i++)
        {
            var src = sources[i];

            hash = HashCode.Combine(
                hash,
                (int)src.ScreenPosition.X,
                (int)src.ScreenPosition.Y,
                src.PixelMask.Width);
        }

        return hash;
    }
}