#region
using Chaos.Client.Data;
using Chaos.Client.Data.AssetPacks;
using Chaos.Client.Data.Models;
using DALib.Data;
using DALib.Definitions;
using DALib.Drawing;
using DALib.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Chaos.Client.Rendering;

public sealed class MapRenderer : IDisposable
{
    private readonly Dictionary<int, SKImage> BgImageCache = [];
    private readonly Lock BgImageCacheLock = new();
    private readonly Dictionary<int, Texture2D> BgTextureCache = [];
    private readonly Dictionary<int, SKImage> FgImageCache = [];
    private readonly Lock FgImageCacheLock = new();
    private readonly Dictionary<int, Texture2D> FgTextureCache = [];
    
    private TextureAtlas? BgAtlas;
    private PaletteCyclingManager? CyclingManager;
    private TextureAtlas? FgAtlas;

    /// <summary>
    ///     Extra tile margin derived from the tallest foreground tile on the current map. Used by callers to expand visible
    ///     bounds for foreground culling.
    /// </summary>
    public int ForegroundExtraMargin { get; private set; }

    public void Dispose()
    {
        BgAtlas?.Dispose();
        BgAtlas = null;
        FgAtlas?.Dispose();
        FgAtlas = null;
        CyclingManager?.Dispose();
        CyclingManager = null;

        foreach (var texture in BgTextureCache.Values)
            texture.Dispose();

        foreach (var image in BgImageCache.Values)
            image.Dispose();

        foreach (var texture in FgTextureCache.Values)
            texture.Dispose();

        foreach (var image in FgImageCache.Values)
            image.Dispose();

        BgTextureCache.Clear();
        BgImageCache.Clear();
        FgTextureCache.Clear();
        FgImageCache.Clear();
    }

    private void BuildBgAtlas(GraphicsDevice device)
    {
        if (BgImageCache.Count == 0)
            return;

        var atlas = new TextureAtlas(
            device,
            PackingMode.Grid,
            CONSTANTS.TILE_WIDTH,
            CONSTANTS.TILE_HEIGHT);

        foreach ((var tileId, var image) in BgImageCache)
            atlas.Add(tileId, image);

        atlas.Build();

        //dispose source images — atlas has consumed their pixels
        foreach (var image in BgImageCache.Values)
            image.Dispose();

        BgImageCache.Clear();

        BgAtlas = atlas;
    }

    private void BuildFgAtlas(GraphicsDevice device)
    {
        if (FgImageCache.Count == 0)
            return;

        var atlas = new TextureAtlas(device, PackingMode.Shelf);

        foreach ((var tileId, var image) in FgImageCache)
            atlas.Add(tileId, image);

        atlas.Build();

        //dispose source images — atlas has consumed their pixels
        foreach (var image in FgImageCache.Values)
            image.Dispose();

        FgImageCache.Clear();

        FgAtlas = atlas;
    }

    /// <summary>
    ///     Convenience method that draws background + foreground without entity interleaving. Foreground uses simple y-major
    ///     order (correct for maps without entities).
    /// </summary>
    public void Draw(
        SpriteBatch spriteBatch,
        GraphicsDevice device,
        MapFile mapFile,
        Camera camera,
        int animationTick)
    {
        DrawBackground(
            spriteBatch,
            mapFile,
            camera,
            animationTick);

        (var fgMinX, var fgMinY, var fgMaxX, var fgMaxY)
            = camera.GetVisibleTileBounds(mapFile.Width, mapFile.Height, ForegroundExtraMargin);

        for (var y = fgMinY; y <= fgMaxY; y++)
        {
            for (var x = fgMinX; x <= fgMaxX; x++)
                DrawForegroundTile(
                    spriteBatch,
                    device,
                    mapFile,
                    camera,
                    x,
                    y,
                    animationTick);
        }
    }

    /// <summary>
    ///     Draws background tiles in y-major order (floor tiles, no overlap concerns).
    ///     Uses the background tile atlas when available for single-draw-call batching.
    /// </summary>
    public void DrawBackground(
        SpriteBatch spriteBatch,
        MapFile mapFile,
        Camera camera,
        int animationTick)
    {
        (var bgMinX, var bgMinY, var bgMaxX, var bgMaxY) = camera.GetVisibleTileBounds(mapFile.Width, mapFile.Height);

        for (var y = bgMinY; y <= bgMaxY; y++)
        {
            for (var x = bgMinX; x <= bgMaxX; x++)
            {
                int bgIndex = mapFile.Tiles[x, y].Background;

                if (bgIndex <= 0)
                    continue;

                bgIndex = ResolveAnimatedTileId(bgIndex, DataContext.Tiles.GetBgAnimation(bgIndex), animationTick);

                var worldPos = Camera.TileToWorld(x, y, mapFile.Height);
                var screenPos = camera.WorldToScreen(worldPos);

                if (((screenPos.X + CONSTANTS.TILE_WIDTH) <= 0)
                    || (screenPos.X >= camera.ViewportWidth)
                    || ((screenPos.Y + CONSTANTS.TILE_HEIGHT) <= 0)
                    || (screenPos.Y >= camera.ViewportHeight))
                    continue;

                //prefer atlas path — all bg tiles in a single texture enables spritebatch batching
                if (BgAtlas is not null)
                {
                    AtlasRegion? region;

                    //cycling tiles have pre-baked variants in the atlas — use the current step's region
                    if (CyclingManager is not null && CyclingManager.BgOverrides.TryGetValue(bgIndex, out var cyclingRegion))
                        region = cyclingRegion;
                    else
                        region = BgAtlas.TryGetRegion(bgIndex);

                    if (region.HasValue)
                    {
                        spriteBatch.Draw(
                            region.Value.Atlas,
                            screenPos,
                            region.Value.SourceRect,
                            Color.White);

                        continue;
                    }
                }

                //fallback to individual texture
                var bgTexture = GetOrCreateBgTexture(bgIndex);

                if (bgTexture is not null)
                    spriteBatch.Draw(bgTexture, screenPos, Color.White);
            }
        }
    }

    /// <summary>
    ///     Draws the foreground tiles (left + right) at a specific tile position. Called by the game screen during diagonal
    ///     stripe iteration for correct draw ordering.
    /// </summary>
    public void DrawForegroundTile(
        SpriteBatch spriteBatch,
        GraphicsDevice device,
        MapFile mapFile,
        Camera camera,
        int x,
        int y,
        int animationTick)
    {
        var tile = mapFile.Tiles[x, y];
        var worldPos = Camera.TileToWorld(x, y, mapFile.Height);

        //left foreground
        if (tile.LeftForeground.IsRenderedTileIndex())
        {
            var lfgTileId = ResolveAnimatedTileId(
                tile.LeftForeground,
                DataContext.Tiles.GetFgAnimation(tile.LeftForeground),
                animationTick);

            DrawSingleFgTile(
                spriteBatch,
                device,
                camera,
                lfgTileId,
                worldPos.X,
                worldPos.Y);
        }

        //right foreground
        if (tile.RightForeground.IsRenderedTileIndex())
        {
            var rfgTileId = ResolveAnimatedTileId(
                tile.RightForeground,
                DataContext.Tiles.GetFgAnimation(tile.RightForeground),
                animationTick);

            DrawSingleFgTile(
                spriteBatch,
                device,
                camera,
                rfgTileId,
                worldPos.X + CONSTANTS.HALF_TILE_WIDTH,
                worldPos.Y);
        }
    }

    private void DrawSingleFgTile(
        SpriteBatch spriteBatch,
        GraphicsDevice device,
        Camera camera,
        int tileId,
        float worldX,
        float worldY)
    {
        //try atlas path (cycling override → atlas → fallback)
        AtlasRegion? region = null;

        if (CyclingManager is not null && CyclingManager.FgOverrides.TryGetValue(tileId, out var fgCyclingRegion))
            region = fgCyclingRegion;
        else if (FgAtlas is not null)
            region = FgAtlas.TryGetRegion(tileId);

        if (region.HasValue)
        {
            var rect = region.Value.SourceRect;
            var fgWorldY = worldY + CONSTANTS.HALF_TILE_HEIGHT * 2 - rect.Height;
            var screenPos = camera.WorldToScreen(new Vector2(worldX, fgWorldY));

            if (IsOnScreen(
                    screenPos,
                    rect.Width,
                    rect.Height,
                    camera))
            {
                var screenBlend = IsTileScreenBlend(tileId);

                if (screenBlend)
                    device.BlendState = BlendStates.Screen;

                spriteBatch.Draw(
                    region.Value.Atlas,
                    screenPos,
                    rect,
                    Color.White);

                if (screenBlend)
                    device.BlendState = BlendState.AlphaBlend;
            }

            return;
        }

        //fallback to individual texture
        var texture = GetOrCreateFgTexture(tileId);

        if (texture is null)
            return;

        var fallbackWorldY = worldY + CONSTANTS.HALF_TILE_HEIGHT * 2 - texture.Height;
        var fallbackScreenPos = camera.WorldToScreen(new Vector2(worldX, fallbackWorldY));

        if (IsOnScreen(
                fallbackScreenPos,
                texture.Width,
                texture.Height,
                camera))
        {
            var screenBlend = IsTileScreenBlend(tileId);

            if (screenBlend)
                device.BlendState = BlendStates.Screen;

            spriteBatch.Draw(texture, fallbackScreenPos, Color.White);

            if (screenBlend)
                device.BlendState = BlendState.AlphaBlend;
        }
    }

    private Texture2D? GetOrCreateBgTexture(int tileId)
    {
        if (BgTextureCache.TryGetValue(tileId, out var cached))
            return cached;

        var palettized = DataContext.Tiles.GetBackgroundTile(tileId);

        if (palettized is null)
            return null;

        using var image = Graphics.RenderTile(palettized.Entity, palettized.Palette);
        var texture = TextureConverter.ToTexture2D(image);
        BgTextureCache[tileId] = texture;

        return texture;
    }

    private Texture2D? GetOrCreateFgTexture(int tileId)
    {
        if (FgTextureCache.TryGetValue(tileId, out var cached))
            return cached;

        var palettized = DataContext.Tiles.GetForegroundTile(tileId);

        if (palettized is null)
            return null;

        using var image = Graphics.RenderImage(palettized.Entity.Decompress(), palettized.Palette);
        var texture = TextureConverter.ToTexture2D(image);
        FgTextureCache[tileId] = texture;

        return texture;
    }

    private static bool IsOnScreen(
        Vector2 screenPos,
        int width,
        int height,
        Camera camera)
        => ((screenPos.X + width) > 0)
           && (screenPos.X < camera.ViewportWidth)
           && ((screenPos.Y + height) > 0)
           && (screenPos.Y < camera.ViewportHeight);

    private bool IsTileScreenBlend(int tileId)
    {
        var sotpIndex = tileId - 1;
        var sotpData = DataContext.Tiles.SotpData;

        if ((sotpIndex < 0) || (sotpIndex >= sotpData.Length))
            return false;

        return (sotpData[sotpIndex] & TileFlags.Transparent) != 0;
    }

    /// <summary>
    ///     Preloads all unique tiles used by the map into texture atlases, including palette cycling variants. Call once after
    ///     loading a new map.
    /// </summary>
    /// <remarks>
    ///     Archive reads are sequential (not thread-safe), but tile rendering is parallelized on the CPU. The resulting
    ///     images are packed into atlas pages (one GPU upload per page).
    /// </remarks>
    public void PreloadMapTiles(
        GraphicsDevice device,
        MapFile mapFile,
        Action<float>? onProgress = null,
        Func<int, IEnumerable<int>>? expandFgVariants = null)
    {
        var uniqueBgTileIds = new HashSet<int>();
        var uniqueFgTileIds = new HashSet<int>();

        //phase 1: scan map to collect unique tile ids (cheap, sequential)
        for (var y = 0; y < mapFile.Height; y++)
        {
            for (var x = 0; x < mapFile.Width; x++)
            {
                var tile = mapFile.Tiles[x, y];

                if (tile.Background > 0)
                    uniqueBgTileIds.Add(tile.Background);

                if (tile.LeftForeground.IsRenderedTileIndex())
                    uniqueFgTileIds.Add(tile.LeftForeground);

                if (tile.RightForeground.IsRenderedTileIndex())
                    uniqueFgTileIds.Add(tile.RightForeground);
            }
        }

        //expand caller-provided variants (e.g. door open/closed counterparts that can appear at runtime via
        //server DoorArgs packets but are not in the initial map). without this, those variants fall through to
        //GetOrCreateFgTexture, producing standalone Texture2Ds that some gpu drivers transiently display with
        //undefined contents.
        if (expandFgVariants is not null)
            foreach (var fgId in uniqueFgTileIds.ToArray())
                foreach (var variant in expandFgVariants(fgId))
                    uniqueFgTileIds.Add(variant);

        //snapshot primary tile ids before animation expansion. only primary ids are eligible for static_tiles pack
        //lookup — animation frames stay legacy to avoid mixed-frame visual glitches when a pack covers only the
        //base frame of an animated tile.
        var primaryBgIds = new HashSet<int>(uniqueBgTileIds);
        var primaryFgIds = new HashSet<int>(uniqueFgTileIds);

        //expand animated bg tiles: add all animation frame ids to the set
        var bgAnimEntries = new HashSet<TileAnimationEntry>(ReferenceEqualityComparer.Instance);

        foreach (var bgId in uniqueBgTileIds.ToArray())
        {
            var anim = DataContext.Tiles.GetBgAnimation(bgId);

            if (anim is null || !bgAnimEntries.Add(anim))
                continue;

            foreach (var frameTileId in anim.TileSequence)
                uniqueBgTileIds.Add(frameTileId);
        }

        //expand animated fg tiles: add all animation frame ids to the set
        var fgAnimEntries = new HashSet<TileAnimationEntry>(ReferenceEqualityComparer.Instance);

        foreach (var fgId in uniqueFgTileIds.ToArray())
        {
            var anim = DataContext.Tiles.GetFgAnimation(fgId);

            if (anim is null || !fgAnimEntries.Add(anim))
                continue;

            foreach (var frameTileId in anim.TileSequence)
                uniqueFgTileIds.Add(frameTileId);
        }

        onProgress?.Invoke(0.1f);

        //phase 2a: read bg tile data from archives sequentially (archive streams are not thread-safe)
        var bgTileData = new Dictionary<int, (Tile Tile, Palette Palette)>();

        foreach (var tileId in uniqueBgTileIds)
        {
            var palettized = DataContext.Tiles.GetBackgroundTile(tileId);

            if (palettized is not null)
                bgTileData[tileId] = (palettized.Entity, palettized.Palette);
        }

        //phase 2b: read compressed fg tile data from archives sequentially (not thread-safe)
        var compressedFgData = new Dictionary<int, (CompressedHpfFile Compressed, Palette Palette)>();

        foreach (var tileId in uniqueFgTileIds)
        {
            var palettized = DataContext.Tiles.GetForegroundTile(tileId);

            if (palettized is not null)
                compressedFgData[tileId] = (palettized.Entity, palettized.Palette);
        }

        //phase 2.5: static_tiles pack lookup. for each primary, non-cycled tile id, swap legacy archive data for
        //a pack-decoded SKImage by writing directly into the image cache and removing the id from the dict that
        //drives phase 3. cycled tiles are skipped because palette cycling overlays would visually overwrite the
        //pack PNG anyway, wasting a decode.
        var staticTilePack = AssetPackRegistry.GetStaticTilePack();

        if (staticTilePack is not null)
        {
            var bgLookup = DataContext.Tiles.BackgroundPaletteLookup;

            foreach (var tileId in bgTileData.Keys.ToArray())
            {
                if (!primaryBgIds.Contains(tileId))
                    continue;

                var paletteNumber = bgLookup.Table.GetPaletteNumber(tileId + 1);

                if (bgLookup.Table.GetCyclingEntries(paletteNumber) is not null)
                    continue;

                if (staticTilePack.TryGetFloorImage(tileId, out var packImage) && (packImage is not null))
                {
                    BgImageCache[tileId] = packImage;
                    bgTileData.Remove(tileId);
                }
            }

            var fgLookup = DataContext.Tiles.ForegroundPaletteLookup;

            foreach (var tileId in compressedFgData.Keys.ToArray())
            {
                if (!primaryFgIds.Contains(tileId))
                    continue;

                var paletteNumber = fgLookup.Table.GetPaletteNumber(tileId + 1);

                if (fgLookup.Table.GetCyclingEntries(paletteNumber) is not null)
                    continue;

                if (staticTilePack.TryGetWallImage(tileId, out var packImage) && (packImage is not null))
                {
                    FgImageCache[tileId] = packImage;
                    compressedFgData.Remove(tileId);
                }
            }
        }

        onProgress?.Invoke(0.4f);

        //phase 3: decompress + render all tiles in parallel (cpu-only, no archive access)
        Parallel.ForEach(
            bgTileData,
            kvp =>
            {
                var image = Graphics.RenderTile(kvp.Value.Tile, kvp.Value.Palette);

                using (BgImageCacheLock.EnterScope())
                    BgImageCache[kvp.Key] = image;
            });

        var maxFgHeight = 0;

        Parallel.ForEach(
            compressedFgData,
            kvp =>
            {
                var hpf = kvp.Value.Compressed.Decompress();
                var image = Graphics.RenderImage(hpf, kvp.Value.Palette);

                using (FgImageCacheLock.EnterScope())
                {
                    FgImageCache[kvp.Key] = image;

                    if (hpf.PixelHeight > maxFgHeight)
                        maxFgHeight = hpf.PixelHeight;
                }
            });

        onProgress?.Invoke(0.7f);

        //convert max pixel height to tile rows: each tile row = 14px
        ForegroundExtraMargin = (int)MathF.Ceiling(maxFgHeight / (float)CONSTANTS.HALF_TILE_HEIGHT);

        //pre-render palette cycling variants before atlas build
        CyclingManager = new PaletteCyclingManager();

        CyclingManager.PrepareVariants(
            mapFile,
            BgImageCache,
            BgImageCacheLock,
            FgImageCache,
            FgImageCacheLock);

        onProgress?.Invoke(0.85f);

        //build atlases from all preloaded pixel data (includes base + cycling variant frames)
        BuildBgAtlas(device);
        BuildFgAtlas(device);

        //resolve cycling variant regions from the built atlases
        CyclingManager.ResolveRegions(BgAtlas, FgAtlas);

        onProgress?.Invoke(1f);
    }

    /// <summary>
    ///     Resolves an animated tile to its current frame's tile ID. Returns the original ID if not animated.
    /// </summary>
    private static int ResolveAnimatedTileId(int tileId, TileAnimationEntry? anim, int animationTick)
    {
        if (anim is null)
            return tileId;

        var frameIndex = animationTick / (anim.AnimationIntervalMs / 100) % anim.TileSequence.Count;

        return anim.TileSequence[frameIndex];
    }

    public void UpdatePaletteCycling(int animationTick) => CyclingManager?.Update(animationTick);

}