#region
using System.Buffers;
using Chaos.Client.Data;
using Chaos.Client.Data.AssetPacks;
using Chaos.Client.Rendering.Utility;
using Chaos.DarkAges.Definitions;
using DALib.Drawing;
using DALib.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Cached UI texture renderer. Deduplicates GPU textures across all UI consumers via a dictionary-backed cache. All
///     returned textures are <see cref="CachedTexture2D" /> whose Dispose is a no-op — only this renderer can release GPU
///     memory. Single instance owned by ChaosGame, exposed via <see cref="Instance" />.
/// </summary>
public sealed class UiRenderer : IDisposable
{
    private const int CHECKER_SIZE = 32;
    private const int CELL_SIZE = 4;
    private static readonly Color CheckerA = new(255, 0, 255); //neon purple
    private static readonly Color CheckerB = new(0, 255, 0); //neon green

    private const int MAX_ATLAS_ENTRY_SIZE = 512;

    private readonly Dictionary<string, CachedTexture2D> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Cache keys for which the stored texture was sourced from a modern .datf asset pack (not legacy EPF). Used to
    ///     tag icons with the <see cref="IconTexture.Modern" /> offset on retrieval.
    /// </summary>
    private readonly HashSet<string> ModernIconKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly GraphicsDevice Device;
    private readonly Dictionary<string, int> EpfFrameCounts = new(StringComparer.OrdinalIgnoreCase);
    private CachedTexture2D? MissingTextureField;
    private TextureAtlas? UiAtlas;

    public static UiRenderer? Instance { get; set; }

    /// <summary>
    ///     A neon green/purple checkerboard texture returned when an asset fails to load. 32x32, 4px cells.
    /// </summary>
    public Texture2D MissingTexture => MissingTextureField ??= ImageUtil.BuildCheckerCached(Device, CHECKER_SIZE, CELL_SIZE, CheckerA, CheckerB);

    public UiRenderer(GraphicsDevice device) => Device = device;

    public void Dispose() => Clear();

    /// <summary>
    ///     Builds a texture atlas from all currently cached UI textures. Textures larger than 512px in either dimension are
    ///     skipped. After building, CachedTexture2D entries have their AtlasRegion set so AtlasHelper.Draw() can route draws
    ///     through the atlas. Safe to call multiple times — rebuilds from scratch each time.
    /// </summary>
    public void BuildAtlas()
    {
        //dispose previous atlas and clear regions
        UiAtlas?.Dispose();
        UiAtlas = null;

        foreach (var texture in Cache.Values)
            texture.AtlasRegion = null;

        var atlas = new TextureAtlas(Device, PackingMode.Shelf);

        foreach ((var key, var texture) in Cache)
        {
            if ((texture.Width > MAX_ATLAS_ENTRY_SIZE) || (texture.Height > MAX_ATLAS_ENTRY_SIZE))
                continue;

            if (texture.IsDisposed)
                continue;

            atlas.Add(key, texture);
        }

        atlas.Build();

        //set atlasregion on each cached texture that was packed
        foreach ((var key, var texture) in Cache)
        {
            var region = atlas.TryGetRegion(key);

            if (region.HasValue)
                texture.AtlasRegion = region.Value;
        }

        UiAtlas = atlas;
    }

    public void Clear()
    {
        UiAtlas?.Dispose();
        UiAtlas = null;

        foreach (var texture in Cache.Values)
        {
            texture.AtlasRegion = null;
            texture.ForceDispose();
        }

        Cache.Clear();
        EpfFrameCounts.Clear();

        MissingTextureField?.ForceDispose();
        MissingTextureField = null;
    }

    private CachedTexture2D Convert(SKImage image)
        => TextureConverter.ConvertImage(image, static (d, w, h) => new CachedTexture2D(d, w, h));

    private CachedTexture2D CreateMissingTexture()
    {
        var pixels = new Color[CHECKER_SIZE * CHECKER_SIZE];

        for (var y = 0; y < CHECKER_SIZE; y++)
            for (var x = 0; x < CHECKER_SIZE; x++)
            {
                var cellX = x / CELL_SIZE;
                var cellY = y / CELL_SIZE;
                pixels[y * CHECKER_SIZE + x] = ((cellX + cellY) % 2) == 0 ? CheckerA : CheckerB;
            }

        var texture = new CachedTexture2D(Device, CHECKER_SIZE, CHECKER_SIZE);
        texture.SetData(pixels);

        return texture;
    }

    private CachedTexture2D CreateCooldownTintedTexture(Texture2D source, Color tint)
    {
        var count = source.Width * source.Height;
        var pixels = ArrayPool<Color>.Shared.Rent(count);

        try
        {
            source.GetData(pixels, 0, count);
            TextureConverter.Blend50Pixels(pixels, count, tint);

            var tinted = new CachedTexture2D(Device, source.Width, source.Height);
            tinted.SetData(pixels, 0, count);

            return tinted;
        } finally
        {
            ArrayPool<Color>.Shared.Return(pixels);
        }
    }

    private CachedTexture2D CreateDuotoneTintedTexture(Texture2D source, Color tint)
    {
        var count = source.Width * source.Height;
        var pixels = ArrayPool<Color>.Shared.Rent(count);

        try
        {
            source.GetData(pixels, 0, count);
            TextureConverter.LuminanceTintPixels(pixels, count, tint);

            var tinted = new CachedTexture2D(Device, source.Width, source.Height);
            tinted.SetData(pixels, 0, count);

            return tinted;
        } finally
        {
            ArrayPool<Color>.Shared.Return(pixels);
        }
    }

    /// <summary>
    ///     Returns the number of frames in an EPF file. Triggers a bulk-cache load if not yet loaded.
    /// </summary>
    public int GetEpfFrameCount(string fileName)
    {
        if (EpfFrameCounts.TryGetValue(fileName, out var count))
            return count;

        GetEpfTexture(fileName, 0);

        return EpfFrameCounts.GetValueOrDefault(fileName);
    }

    /// <summary>
    ///     Loads a single EPF frame from setoa.dat (GUI palette) and caches the resulting texture. The first call for a given
    ///     file bulk-caches all frames.
    /// </summary>
    public Texture2D GetEpfTexture(string fileName, int frameIndex)
    {
        var key = $"epf:{fileName}:{frameIndex}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var images = DataContext.UserControls.GetEpfImages(fileName);

        //bulk-cache all frames, then dispose all skimages
        for (var i = 0; i < images.Length; i++)
        {
            var frameKey = $"epf:{fileName}:{i}";

            if (!Cache.ContainsKey(frameKey))
                Cache[frameKey] = Convert(images[i]);

            images[i]
                .Dispose();
        }

        EpfFrameCounts[fileName] = images.Length;

        return Cache.GetValueOrDefault(key) ?? MissingTexture;
    }

    /// <summary>
    ///     Loads and caches a field image (EPF + matching PAL from setoa.dat). Used for world map backgrounds.
    /// </summary>
    public Texture2D GetFieldImage(string fieldName)
    {
        var key = $"field:{fieldName}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        using var image = DataContext.UserControls.GetFieldImage(fieldName);

        if (image is null)
            return MissingTexture;

        var texture = Convert(image);
        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Renders and caches a half-size (15x15) spell icon for the effect bar. Tries the modern .datf pack first,
    ///     falls back to the legacy EPF sheet. The 15x15 downscale happens on whichever source is used — no 1px offset
    ///     math needed at this size since the icon is being rescaled anyway.
    /// </summary>
    public Texture2D GetHalfSizeSpellIcon(byte iconId)
    {
        var key = $"spell_half:{iconId}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        const int HALF_ICON_SIZE = 15;

        SKImage? sourceImage = null;
        var ownedSource = false;

        //modern path: pack PNG decoded via SkiaSharp
        var pack = AssetPackRegistry.GetIconPack();

        if ((pack is not null) && pack.TryGetIconImage("spell", iconId, out var modern) && (modern is not null))
        {
            sourceImage = modern;
            ownedSource = true;
        }

        //legacy path: palettized EPF rendered to SKImage
        if (sourceImage is null)
        {
            var palettized = DataContext.PanelSprites.GetSpellIcon(iconId);

            if (palettized is null)
                return MissingTexture;

            sourceImage = Graphics.RenderImage(palettized.Entity, palettized.Palette);

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (sourceImage is null)
                return MissingTexture;

            ownedSource = true;
        }

        try
        {
            //2x2 box filter samples the top-left (HALF_ICON_SIZE*2)x(HALF_ICON_SIZE*2) of the source.
            //legacy 31x31 EPF drops 1px edge; modern 32x32 .datf drops 2px — imperceptible at 15x15.
            using var sourceBitmap = SKBitmap.FromImage(sourceImage);
            var srcPixels = sourceBitmap.Pixels;
            var halvedPixels = ImageUtil.DownsampleIcon(srcPixels, sourceBitmap.Width, HALF_ICON_SIZE, HALF_ICON_SIZE);

            using var halfBitmap = new SKBitmap(
                new SKImageInfo(
                    HALF_ICON_SIZE,
                    HALF_ICON_SIZE,
                    SKColorType.Rgba8888,
                    SKAlphaType.Premul));
            halfBitmap.Pixels = halvedPixels;

            using var halfImage = SKImage.FromBitmap(halfBitmap);
            var texture = Convert(halfImage);
            Cache[key] = texture;

            return texture;
        }
        finally
        {
            if (ownedSource)
                sourceImage.Dispose();
        }
    }

    /// <summary>
    ///     Renders and caches an inventory/equipment item icon.
    /// </summary>
    public Texture2D GetItemIcon(ushort spriteId, DisplayColor color = DisplayColor.Default)
    {
        var key = color == DisplayColor.Default ? $"item:{spriteId}" : $"item:{spriteId}:{(int)color}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var palettized = DataContext.PanelSprites.GetItemSprite(spriteId);

        if (palettized is not null && (color != DisplayColor.Default))
        {
            var dyedPalette = DataContext.AislingDrawData.ApplyDye(palettized.Palette, color);

            if (dyedPalette != palettized.Palette)
                palettized = new Palettized<EpfFrame>
                {
                    Entity = palettized.Entity,
                    Palette = dyedPalette
                };
        }

        var texture = RenderSprite(palettized);

        if (texture is null)
            return MissingTexture;

        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Loads a single EPF frame from national.dat (legend.pal) and caches the resulting texture.
    /// </summary>
    public Texture2D GetNationalEpfTexture(string fileName, int frameIndex = 0)
    {
        var key = $"nepf:{fileName}:{frameIndex}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        using var image = DataContext.UserControls.GetNationalEpfImage(fileName, frameIndex);

        if (image is null)
            return MissingTexture;

        var texture = Convert(image);
        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Loads a single SPF frame from national.dat and caches the resulting texture.
    /// </summary>
    public Texture2D GetNationalSpfTexture(string fileName, int frameIndex = 0)
    {
        var key = $"nspf:{fileName}:{frameIndex}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        using var image = DataContext.UserControls.GetNationalSpfImage(fileName, frameIndex);

        if (image is null)
            return MissingTexture;

        var texture = Convert(image);
        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Returns the texture for a specific image index within a named control of a ControlPrefabSet.
    /// </summary>
    public Texture2D GetPrefabTexture(string prefabSetName, string controlName, int imageIndex)
    {
        var key = $"prefab:{prefabSetName}:{controlName}:{imageIndex}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var prefabSet = DataContext.UserControls.Get(prefabSetName);

        if (prefabSet is null || !prefabSet.Contains(controlName))
            return MissingTexture;

        var prefab = prefabSet[controlName];

        if ((imageIndex < 0) || (imageIndex >= prefab.Images.Count))
            return MissingTexture;

        var texture = Convert(prefab.Images[imageIndex]);
        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Returns a cached nation badge texture for the given nation ID. Tries the modern .datf nation-badge pack
    ///     first, falls back to frame (<c>nationId - 1</c>) of the legacy <c>_nui_nat.spf</c>. Callers assign the
    ///     result to a <see cref="UIImage" />; the image scales to fit its placed bounds regardless of source
    ///     dimensions.
    /// </summary>
    public Texture2D GetNationBadge(byte nationId)
    {
        var key = $"nation:{nationId}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        //modern path: pack PNG decoded via SkiaSharp
        var pack = AssetPackRegistry.GetNationBadgePack();

        if ((pack is not null) && pack.TryGetBadgeImage(nationId, out var modernImage) && (modernImage is not null))
            try
            {
                var modernTex = Convert(modernImage);
                Cache[key] = modernTex;

                return modernTex;
            }
            finally
            {
                modernImage.Dispose();
            }

        //legacy path: frame (nationId - 1) of _nui_nat.spf
        using var legacyImage = DataContext.UserControls.GetSpfImage("_nui_nat.spf", nationId - 1);

        if (legacyImage is null)
            return MissingTexture;

        var legacyTex = Convert(legacyImage);
        Cache[key] = legacyTex;

        return legacyTex;
    }

    /// <summary>
    ///     Returns a cached legend-mark icon texture for the given icon ID. Tries the modern <c>.datf</c>
    ///     legend-mark-icon pack first, falls back to frame <paramref name="iconId" /> of the legacy
    ///     <c>legends.epf</c> sheet (palette 3 of <c>national.dat</c>). Unlike nation badges and ability icons, legend
    ///     mark IDs are <b>0-based</b> — the wire byte is used directly as the EPF frame index, so
    ///     <c>legend0000.png</c> replaces server icon ID 0.
    /// </summary>
    public Texture2D GetLegendMarkIcon(byte iconId)
    {
        var key = $"legend:{iconId}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        //modern path: pack PNG decoded via SkiaSharp
        var pack = AssetPackRegistry.GetLegendMarkIconPack();

        if ((pack is not null) && pack.TryGetLegendMarkImage(iconId, out var modernImage) && (modernImage is not null))
            try
            {
                var modernTex = Convert(modernImage);
                Cache[key] = modernTex;

                return modernTex;
            }
            finally
            {
                modernImage.Dispose();
            }

        //legacy path: frame iconId of legends.epf — GetEpfTexture caches under its own "epf:legends.epf:{i}" key,
        //so we don't re-cache under "legend:{iconId}". The legend: key is reserved for modern-pack results, which
        //means a pack registering mid-session (hypothetical; no hot-reload today) would still be picked up on the
        //first uncached lookup after registration.
        return GetEpfTexture("legends.epf", iconId);
    }

    /// <summary>
    ///     Returns a cached copy of <paramref name="source" /> with a 50/50 blend of
    ///     <paramref name="tint" /> applied, used for cooldown overlays on skill/spell icons.
    ///     Retail parity: <see cref="LegendColors.DimGray" /> (<c>legend.pal[0x18]</c>) is the
    ///     dim base layer of <c>SkillInvItemPane::Render</c> (<c>FUN_004991d0</c>);
    ///     <see cref="LegendColors.CornflowerBlue" /> (<c>legend.pal[0x58]</c>) is the upper-half
    ///     overlay of the same method and the full-icon tint of <c>SpellInvItemPane::Render</c>.
    ///     Keyed by <paramref name="sourceKey" /> + the packed tint color so the same source
    ///     key can be tinted multiple colors without collision.
    /// </summary>
    public Texture2D GetCooldownTintedTexture(string sourceKey, Texture2D source, Color tint)
    {
        var key = $"cd_{tint.PackedValue:X8}:{sourceKey}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var texture = ImageUtil.BuildCooldownTintedCached(Device, source, tint);
        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Returns a duotone (luminance-tinted) copy of a source texture, cached by (tint, sourceKey). The duotone
    ///     treatment converts each pixel to its Rec. 601 luminance and multiplies by the tint color — producing a
    ///     strong, recognizable state overlay (used for learnable/locked ability icons). Preserves alpha.
    /// </summary>
    public Texture2D GetDuotoneTintedTexture(string sourceKey, Texture2D source, Color tint)
    {
        var key = $"duo_{tint.PackedValue:X8}:{sourceKey}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var texture = CreateDuotoneTintedTexture(source, tint);
        Cache[key] = texture;

        return texture;
    }

    /// <summary>
    ///     Renders and caches a skill icon. Tries the modern .datf pack first (32x32 PNG, drawn with -1/-1 offset); on
    ///     miss, falls back to the legacy EPF sheet (31x31, zero offset). Callers should draw via
    ///     <see cref="IconTexture.Draw" /> so the offset is applied correctly regardless of source. Learnable and
    ///     Locked visual states are produced by tinting the result at draw time — there are no longer separate
    ///     accessors.
    /// </summary>
    public IconTexture GetSkillIcon(ushort spriteId) => GetAbilityIcon("skill", spriteId, DataContext.PanelSprites.GetSkillIcon);

    /// <summary>
    ///     Renders and caches a spell icon. See <see cref="GetSkillIcon" /> for the modern-first dispatch behavior.
    /// </summary>
    public IconTexture GetSpellIcon(ushort spriteId) => GetAbilityIcon("spell", spriteId, DataContext.PanelSprites.GetSpellIcon);

    /// <summary>
    ///     Shared modern-first dispatch for skill and spell icons. Legacy EPF lookup goes through the provided
    ///     delegate; modern PNG lookup goes through <see cref="AssetPackRegistry.GetIconPack" />.
    /// </summary>
    private IconTexture GetAbilityIcon(string prefix, ushort spriteId, Func<int, Palettized<EpfFrame>?> legacyLookup)
    {
        var key = $"{prefix}:{spriteId}";

        if (Cache.TryGetValue(key, out var cached))
            return ModernIconKeys.Contains(key) ? IconTexture.Modern(cached) : IconTexture.Legacy(cached);

        var pack = AssetPackRegistry.GetIconPack();

        if ((pack is not null) && pack.TryGetIconImage(prefix, spriteId, out var skImage) && (skImage is not null))
            try
            {
                var modernTex = Convert(skImage);
                Cache[key] = modernTex;
                ModernIconKeys.Add(key);

                return IconTexture.Modern(modernTex);
            }
            finally
            {
                skImage.Dispose();
            }

        var legacyTex = RenderSprite(legacyLookup(spriteId));

        if (legacyTex is null)
            return IconTexture.Legacy(MissingTexture);

        Cache[key] = legacyTex;

        return IconTexture.Legacy(legacyTex);
    }


    /// <summary>
    ///     Loads a single SPF frame from setoa.dat and caches the resulting texture.
    /// </summary>
    public Texture2D GetSpfTexture(string fileName, int frameIndex = 0)
    {
        var key = $"spf:{fileName}:{frameIndex}";

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        using var image = DataContext.UserControls.GetSpfImage(fileName, frameIndex);

        if (image is null)
            return MissingTexture;

        var texture = Convert(image);
        Cache[key] = texture;

        return texture;
    }

    private CachedTexture2D? RenderSprite(Palettized<EpfFrame>? palettized)
    {
        if (palettized is null)
            return null;

        using var image = Graphics.RenderImage(palettized.Entity, palettized.Palette);

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        return image is not null ? Convert(image) : null;
    }
}
