#region
using System.Collections.Frozen;
using Brigid.Data;
using DALib.Data;
using DALib.Drawing;
using DALib.Extensions;
using DALib.Utility;
using SkiaSharp;
#endregion

namespace Brigid.Rendering;

public sealed class PaletteCyclingManager : IDisposable
{
    private const int VARIANT_KEY_STEP_MULTIPLIER = 256;
    private FrozenDictionary<int, CyclingSlot> BgSlots = FrozenDictionary<int, CyclingSlot>.Empty;
    private FrozenDictionary<int, AtlasRegion[]> BgVariantRegions = FrozenDictionary<int, AtlasRegion[]>.Empty;
    private FrozenDictionary<int, CyclingSlot> FgSlots = FrozenDictionary<int, CyclingSlot>.Empty;
    private FrozenDictionary<int, AtlasRegion[]> FgVariantRegions = FrozenDictionary<int, AtlasRegion[]>.Empty;
    private int LastTick;

    /// <summary>
    ///     Current atlas region overrides for cycling background tiles. Keyed by tile ID, updated each cycle advance. Checked
    ///     by MapRenderer before the default atlas lookup.
    /// </summary>
    public Dictionary<int, AtlasRegion> BgOverrides { get; } = [];

    /// <summary>
    ///     Current atlas region overrides for cycling foreground tiles. Keyed by tile ID, updated each cycle advance. Checked
    ///     by MapRenderer before the default atlas lookup.
    /// </summary>
    public Dictionary<int, AtlasRegion> FgOverrides { get; } = [];

    public void Dispose()
    {
        BgVariantRegions = FrozenDictionary<int, AtlasRegion[]>.Empty;
        FgVariantRegions = FrozenDictionary<int, AtlasRegion[]>.Empty;
        BgOverrides.Clear();
        FgOverrides.Clear();
        BgSlots = FrozenDictionary<int, CyclingSlot>.Empty;
        FgSlots = FrozenDictionary<int, CyclingSlot>.Empty;
    }

    private static bool AdvanceSlot(CyclingSlot slot)
    {
        slot.Counter++;

        if (slot.Counter < slot.Period)
            return false;

        slot.Counter = 0;
        slot.CurrentStep = (slot.CurrentStep + 1) % slot.TotalSteps;

        return true;
    }

    private static int ComputeTotalSteps(IReadOnlyList<PaletteCyclingEntry> entries)
    {
        var result = 1;

        foreach (var entry in entries)
        {
            var rangeLength = entry.EndIndex - entry.StartIndex + 1;
            result = Lcm(result, rangeLength);
        }

        return result;
    }

    private static int EncodeVariantKey(int tileId, int step) => -(tileId * VARIANT_KEY_STEP_MULTIPLIER + step);

    private static void ExpandAnimatedTiles(
        Func<int, TileAnimationEntry?> getAnimation,
        HashSet<int> registered,
        Dictionary<int, List<int>> tilesByPalette)
    {
        var expansions = new List<(int PaletteNumber, int TileId)>();

        foreach ((var paletteNumber, var tileIds) in tilesByPalette)
        {
            foreach (var tileId in tileIds)
            {
                var anim = getAnimation(tileId);

                if (anim is null)
                    continue;

                foreach (var frameTileId in anim.TileSequence)
                    if (registered.Add(frameTileId))
                        expansions.Add((paletteNumber, frameTileId));
            }
        }

        foreach ((var paletteNumber, var tileId) in expansions)
            tilesByPalette[paletteNumber]
                .Add(tileId);
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
            (a, b) = (b, a % b);

        return a;
    }

    private static int Lcm(int a, int b) => a / Gcd(a, b) * b;

    /// <summary>
    ///     Scans the map for tiles that use palette cycling and pre-renders all palette-shifted variants into the provided
    ///     image caches. Must be called before building tile atlases, since the atlas build consumes these images.
    /// </summary>
    public void PrepareVariants(
        MapFile mapFile,
        Dictionary<int, SKImage> bgImageCache,
        Lock bgImageCacheLock,
        Dictionary<int, SKImage> fgImageCache,
        Lock fgImageCacheLock)
    {
        var bgLookup = DataContext.Tiles.BackgroundPaletteLookup;
        var fgLookup = DataContext.Tiles.ForegroundPaletteLookup;

        var registeredBg = new HashSet<int>();
        var registeredFg = new HashSet<int>();
        var bgTilesByPalette = new Dictionary<int, List<int>>();
        var fgTilesByPalette = new Dictionary<int, List<int>>();

        for (var y = 0; y < mapFile.Height; y++)
        {
            for (var x = 0; x < mapFile.Width; x++)
            {
                var tile = mapFile.Tiles[x, y];

                if (tile.Background > 0)
                    TryRegisterTile(
                        tile.Background,
                        bgLookup,
                        registeredBg,
                        bgTilesByPalette);

                if (tile.LeftForeground.IsRenderedTileIndex())
                    TryRegisterTile(
                        tile.LeftForeground,
                        fgLookup,
                        registeredFg,
                        fgTilesByPalette);

                if (tile.RightForeground.IsRenderedTileIndex())
                    TryRegisterTile(
                        tile.RightForeground,
                        fgLookup,
                        registeredFg,
                        fgTilesByPalette);
            }
        }

        ExpandAnimatedTiles(DataContext.Tiles.GetBgAnimation, registeredBg, bgTilesByPalette);
        ExpandAnimatedTiles(DataContext.Tiles.GetFgAnimation, registeredFg, fgTilesByPalette);

        var bgSlots = new Dictionary<int, CyclingSlot>();
        var fgSlots = new Dictionary<int, CyclingSlot>();

        PreRenderVariantsToCache(
            bgLookup,
            bgTilesByPalette,
            bgImageCache,
            bgImageCacheLock,
            bgSlots,
            static tileId => DataContext.Tiles.GetBackgroundTile(tileId),
            static (entity, palette) => Graphics.RenderTile(entity, palette));

        PreRenderVariantsToCache(
            fgLookup,
            fgTilesByPalette,
            fgImageCache,
            fgImageCacheLock,
            fgSlots,
            static tileId => DataContext.Tiles.GetForegroundTile(tileId),
            static (entity, palette) => Graphics.RenderImage(entity.Decompress(), palette));

        BgSlots = bgSlots.ToFrozenDictionary();
        FgSlots = fgSlots.ToFrozenDictionary();
    }

    private static void PreRenderVariantsToCache<T>(
        PaletteLookup lookup,
        Dictionary<int, List<int>> tilesByPalette,
        Dictionary<int, SKImage> imageCache,
        Lock imageCacheLock,
        Dictionary<int, CyclingSlot> slots,
        Func<int, Palettized<T>?> getTile,
        Func<T, Palette, SKImage> render)
    {
        //collect all render work items: (encodedkey, entity, cycledpalette) — each is independent
        var workItems = new List<(int EncodedKey, T Entity, Palette CycledPalette)>();

        foreach ((var paletteNumber, var tileIds) in tilesByPalette)
        {
            if (tileIds.Count == 0)
                continue;

            var cycling = lookup.Table.GetCyclingEntries(paletteNumber);

            if (cycling is null)
                continue;

            if (!lookup.Palettes.TryGetValue(paletteNumber, out var originalPalette))
                continue;

            var totalSteps = ComputeTotalSteps(cycling);
            var period = cycling[0].Period;

            var entities = new Dictionary<int, T>();

            foreach (var tileId in tileIds)
            {
                var palettized = getTile(tileId);

                if (palettized is not null)
                    entities[tileId] = palettized.Entity;
            }

            //step 0 is the base tile already in imagecache — only add steps 1..n-1 as variants
            for (var step = 1; step < totalSteps; step++)
            {
                var cycledPalette = originalPalette;

                foreach (var entry in cycling)
                    cycledPalette = cycledPalette.Cycle((byte)entry.StartIndex, (byte)entry.EndIndex, step);

                foreach ((var tileId, var entity) in entities)
                    workItems.Add((EncodeVariantKey(tileId, step), entity, cycledPalette));
            }

            slots[paletteNumber] = new CyclingSlot
            {
                TotalSteps = totalSteps,
                Period = period,
                TileIds = [.. tileIds]
            };
        }

        //render all variants in parallel (cpu-only, thread-safe)
        Parallel.ForEach(
            workItems,
            item =>
            {
                var image = render(item.Entity, item.CycledPalette);

                using (imageCacheLock.EnterScope())
                    imageCache[item.EncodedKey] = image;
            });
    }

    /// <summary>
    ///     Resolves atlas regions for all pre-rendered cycling variants. Must be called after tile atlases are built.
    /// </summary>
    public void ResolveRegions(TextureAtlas? bgAtlas, TextureAtlas? fgAtlas)
    {
        if (bgAtlas is not null)
            BgVariantRegions = ResolveVariantRegions(bgAtlas, BgSlots, BgOverrides);

        if (fgAtlas is not null)
            FgVariantRegions = ResolveVariantRegions(fgAtlas, FgSlots, FgOverrides);
    }

    private static FrozenDictionary<int, AtlasRegion[]> ResolveVariantRegions(
        TextureAtlas atlas,
        FrozenDictionary<int, CyclingSlot> slots,
        Dictionary<int, AtlasRegion> overrides)
    {
        var variantRegions = new Dictionary<int, AtlasRegion[]>();

        foreach ((_, var slot) in slots)
        {
            foreach (var tileId in slot.TileIds)
            {
                var baseRegion = atlas.TryGetRegion(tileId);

                if (!baseRegion.HasValue)
                    continue;

                var regions = new AtlasRegion[slot.TotalSteps];
                regions[0] = baseRegion.Value;

                var allResolved = true;

                for (var step = 1; step < slot.TotalSteps; step++)
                {
                    var variantRegion = atlas.TryGetRegion(EncodeVariantKey(tileId, step));

                    if (!variantRegion.HasValue)
                    {
                        allResolved = false;

                        break;
                    }

                    regions[step] = variantRegion.Value;
                }

                if (!allResolved)
                    continue;

                variantRegions[tileId] = regions;
                overrides[tileId] = regions[0];
            }
        }

        return variantRegions.ToFrozenDictionary();
    }

    private static void TryRegisterTile(
        int tileId,
        PaletteLookup lookup,
        HashSet<int> registered,
        Dictionary<int, List<int>> tilesByPalette)
    {
        if (!registered.Add(tileId))
            return;

        var paletteNumber = lookup.Table.GetPaletteNumber(tileId + 1);
        var cycling = lookup.Table.GetCyclingEntries(paletteNumber);

        if (cycling is null)
        {
            registered.Remove(tileId);

            return;
        }

        if (!tilesByPalette.TryGetValue(paletteNumber, out var list))
        {
            list = [];
            tilesByPalette[paletteNumber] = list;
        }

        list.Add(tileId);
    }

    public void Update(int animationTick)
    {
        if (animationTick == LastTick)
            return;

        LastTick = animationTick;

        //bg path: swap atlas region pointers — zero gpu work
        foreach ((_, var slot) in BgSlots)
        {
            if (!AdvanceSlot(slot))
                continue;

            foreach (var tileId in slot.TileIds)
                if (BgVariantRegions.TryGetValue(tileId, out var regions))
                    BgOverrides[tileId] = regions[slot.CurrentStep];
        }

        //fg path: swap atlas region pointers — zero gpu work
        foreach ((_, var slot) in FgSlots)
        {
            if (!AdvanceSlot(slot))
                continue;

            foreach (var tileId in slot.TileIds)
                if (FgVariantRegions.TryGetValue(tileId, out var regions))
                    FgOverrides[tileId] = regions[slot.CurrentStep];
        }
    }

    private sealed class CyclingSlot
    {
        public int Counter;
        public int CurrentStep;
        public required int Period;
        public required int[] TileIds;
        public required int TotalSteps;
    }
}