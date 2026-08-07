#region
using Brigid.Data;
using Brigid.Data.AssetPacks;
using Brigid.Data.Models;
using Brigid.Rendering.Utility;
using DALib.Data;
using DALib.Definitions;
using DALib.Drawing;
using DALib.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Brigid.Rendering;

public sealed class MapRenderer : IDisposable
{
    private readonly Dictionary<int, SKImage> BgImageCache = [];
    private readonly Lock BgImageCacheLock = new();
    private readonly Dictionary<int, Texture2D> BgTextureCache = [];
    private readonly Dictionary<int, SKImage> FgImageCache = [];
    private readonly Lock FgImageCacheLock = new();
    private readonly Dictionary<int, Texture2D> FgTextureCache = [];

    //shared checkerboard placeholders served for a background/foreground tile id present in neither the static_tiles
    //pack nor legacy tileset. Kept out of the Bg/FgTextureCache (which dispose every value) so they can never be
    //double-freed; rebuilt lazily after Dispose. A referenced-but-absent tile becomes a visible marker, not a hole.
    private Texture2D? MissingBgTile;
    private Texture2D? MissingFgTile;

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

        MissingBgTile?.Dispose();
        MissingBgTile = null;
        MissingFgTile?.Dispose();
        MissingFgTile = null;
    }

    //lazily built once, reused for every missing background tile id; the caller (DrawBackground) only reaches
    //GetOrCreateBgTexture for bgIndex > 0, so this is always a real referenced tile.
    private Texture2D GetMissingBgTile()
        => MissingBgTile ??= ImageUtil.BuildMissingPlaceholder(TextureConverter.Device, CONSTANTS.TILE_WIDTH, CONSTANTS.TILE_HEIGHT);

    //lazily built once, reused for every missing foreground tile id; DrawSingleFgTile only reaches
    //GetOrCreateFgTexture for IsRenderedTileIndex ids. Half-tile wide, full-tile tall so it bottom-aligns like a wall.
    private Texture2D GetMissingFgTile()
        => MissingFgTile ??= ImageUtil.BuildMissingPlaceholder(TextureConverter.Device, CONSTANTS.HALF_TILE_WIDTH, CONSTANTS.HALF_TILE_HEIGHT * 2);

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
        if (((int)tile.LeftForeground).IsRenderedTileIndex())
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
        if (((int)tile.RightForeground).IsRenderedTileIndex())
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

    /// <summary>
    ///     Hit-tests the foreground sprites at a screen point and returns the tile of the frontmost foreground whose
    ///     rendered bounding box contains it. A tall foreground (a signpost/board) is drawn bottom-aligned and overhangs
    ///     the tiles above it, so this lets a click anywhere on the sprite resolve to its anchor tile even when the
    ///     overhung tiles carry their own foreground (e.g. walls). Bounding-box test, not per-pixel — good enough for
    ///     click targeting and independent of tile passability. <paramref name="screenX" />/<paramref name="screenY" /> are
    ///     viewport-relative (camera space), i.e. window coords minus the world viewport origin — same as ScreenToWorld.
    /// </summary>
    public bool TryHitTestForeground(MapFile mapFile, Camera camera, int screenX, int screenY, out int tileX, out int tileY)
    {
        tileX = 0;
        tileY = 0;

        var found = false;
        var bestDepth = int.MinValue;

        var (minX, minY, maxX, maxY) = camera.GetVisibleTileBounds(mapFile.Width, mapFile.Height, ForegroundExtraMargin);

        for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var depth = x + y;

                //only a frontmost (larger depth) sprite can override the current best, so skip anything that can't win
                if (depth <= bestDepth)
                    continue;

                var tile = mapFile.Tiles[x, y];
                var worldPos = Camera.TileToWorld(x, y, mapFile.Height);

                if ((((int)tile.LeftForeground).IsRenderedTileIndex()
                     && FgSpriteContains(camera, worldPos.X, worldPos.Y, tile.LeftForeground, screenX, screenY))
                    || (((int)tile.RightForeground).IsRenderedTileIndex()
                        && FgSpriteContains(camera, worldPos.X + CONSTANTS.HALF_TILE_WIDTH, worldPos.Y, tile.RightForeground, screenX, screenY)))
                {
                    bestDepth = depth;
                    tileX = x;
                    tileY = y;
                    found = true;
                }
            }

        return found;
    }

    private bool FgSpriteContains(Camera camera, float worldX, float worldY, int tileId, int screenX, int screenY)
    {
        if (!TryGetFgSpriteSize(tileId, out var width, out var height))
            return false;

        //foreground is bottom-aligned at the tile (mirrors DrawSingleFgTile)
        var fgWorldY = worldY + CONSTANTS.HALF_TILE_HEIGHT * 2 - height;
        var screenPos = camera.WorldToScreen(new Vector2(worldX, fgWorldY));

        return (screenX >= screenPos.X) && (screenX < screenPos.X + width) && (screenY >= screenPos.Y)
               && (screenY < screenPos.Y + height);
    }

    private bool TryGetFgSpriteSize(int tileId, out int width, out int height)
    {
        if (CyclingManager is not null && CyclingManager.FgOverrides.TryGetValue(tileId, out var cyclingRegion))
        {
            width = cyclingRegion.SourceRect.Width;
            height = cyclingRegion.SourceRect.Height;

            return true;
        }

        if (FgAtlas is not null && FgAtlas.TryGetRegion(tileId) is { } region)
        {
            width = region.SourceRect.Width;
            height = region.SourceRect.Height;

            return true;
        }

        var texture = GetOrCreateFgTexture(tileId);

        if (texture is not null)
        {
            width = texture.Width;
            height = texture.Height;

            return true;
        }

        width = 0;
        height = 0;

        return false;
    }

    private Texture2D? GetOrCreateBgTexture(int tileId)
    {
        if (BgTextureCache.TryGetValue(tileId, out var cached))
            return cached;

        //consult the static_tiles pack before legacy art so that pack-only floor ids (no legacy tileset entry) and
        //pack-replaced ids resolve on the runtime fallback path too, not just during preload.
        if (AssetPackRegistry.GetStaticTilePack() is { } pack && pack.TryGetFloorImage(tileId, out var packImage) && (packImage is not null))
        {
            using (packImage)
            {
                var packTexture = TextureConverter.ToTexture2D(packImage);
                BgTextureCache[tileId] = packTexture;

                return packTexture;
            }
        }

        var palettized = DataContext.Tiles.GetBackgroundTile(tileId);

        if (palettized is null)
            //terminal miss: tile absent from both the static_tiles pack and the legacy tileset. Serve the shared
            //checkerboard so a referenced-but-absent floor id is a visible marker instead of a hole in the map.
            return GetMissingBgTile();

        using var image = Graphics.RenderTile(palettized.Entity, palettized.Palette);
        var texture = TextureConverter.ToTexture2D(image);
        BgTextureCache[tileId] = texture;

        return texture;
    }

    private Texture2D? GetOrCreateFgTexture(int tileId)
    {
        if (FgTextureCache.TryGetValue(tileId, out var cached))
            return cached;

        //consult the static_tiles pack before legacy art so that pack-only wall ids (no legacy stc*.hpf) and
        //pack-replaced ids resolve on the runtime fallback path too — notably runtime-introduced door variants.
        if (AssetPackRegistry.GetStaticTilePack() is { } pack && pack.TryGetWallImage(tileId, out var packImage) && (packImage is not null))
        {
            using (packImage)
            {
                var packTexture = TextureConverter.ToTexture2D(packImage);
                FgTextureCache[tileId] = packTexture;

                return packTexture;
            }
        }

        var palettized = DataContext.Tiles.GetForegroundTile(tileId);

        if (palettized is null)
            //terminal miss: foreground tile absent from both the static_tiles pack and legacy hpf art. Serve the
            //shared checkerboard so a referenced-but-absent wall/object id is a visible marker instead of a gap.
            return GetMissingFgTile();

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

                if (((int)tile.LeftForeground).IsRenderedTileIndex())
                    uniqueFgTileIds.Add(tile.LeftForeground);

                if (((int)tile.RightForeground).IsRenderedTileIndex())
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

        //track tallest fg image across both phase 2.5 (pack-replaced) and phase 3 (legacy-rendered) so that
        //ForegroundExtraMargin reflects the full set of fg tiles drawn this map. without folding pack heights
        //in, a pack wall taller than every legacy hpf on the map would be undersized for culling and clip at
        //the viewport edge.
        var maxFgHeight = 0;

        //phase 2.5: static_tiles pack lookup. iterate the map-scanned primary id sets (not the legacy dict keys) so
        //that pack-only ids — tiles with no legacy tileset/hpf counterpart — are also resolved ("add"), not just
        //replaced. for an id that has legacy data, swap it for a pack-decoded SKImage and drop it from the dict that
        //drives phase 3 ("replace"); cycled ids are skipped because palette cycling overlays would visually overwrite
        //the pack PNG anyway. for a pack-only id (no legacy data) the cycling-table check is skipped entirely — its
        //GetPaletteNumber(id+1) has no legacy entry — and the pack image, if present, simply seeds the cache.
        var staticTilePack = AssetPackRegistry.GetStaticTilePack();

        if (staticTilePack is not null)
        {
            var bgLookup = DataContext.Tiles.BackgroundPaletteLookup;

            foreach (var tileId in primaryBgIds)
            {
                if (bgTileData.ContainsKey(tileId) && bgLookup.Table.GetCyclingEntries(bgLookup.Table.GetPaletteNumber(tileId + 1)) is not null)
                    continue;

                if (staticTilePack.TryGetFloorImage(tileId, out var packImage) && (packImage is not null))
                {
                    BgImageCache[tileId] = packImage;
                    bgTileData.Remove(tileId);
                }
            }

            var fgLookup = DataContext.Tiles.ForegroundPaletteLookup;

            foreach (var tileId in primaryFgIds)
            {
                if (compressedFgData.ContainsKey(tileId) && fgLookup.Table.GetCyclingEntries(fgLookup.Table.GetPaletteNumber(tileId + 1)) is not null)
                    continue;

                if (staticTilePack.TryGetWallImage(tileId, out var packImage) && (packImage is not null))
                {
                    FgImageCache[tileId] = packImage;
                    compressedFgData.Remove(tileId);

                    if (packImage.Height > maxFgHeight)
                        maxFgHeight = packImage.Height;
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