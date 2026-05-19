#region
using System.Diagnostics.CodeAnalysis;
using Chaos.Client.Data;
using Chaos.Client.Data.Repositories;
using Chaos.Client.Rendering.Utility;
using Microsoft.Extensions.Caching.Memory;
using Chaos.DarkAges.Definitions;
using DALib.Definitions;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Visual appearance data for rendering an aisling. Set sprite IDs to 0 to skip that layer.
/// </summary>
public record struct AislingAppearance
{
    public DisplayColor Accessory1Color { get; init; }
    public int Accessory1Sprite { get; init; }
    public DisplayColor Accessory2Color { get; init; }
    public int Accessory2Sprite { get; init; }
    public DisplayColor Accessory3Color { get; init; }
    public int Accessory3Sprite { get; init; }
    public DisplayColor ArmorColor { get; init; }
    public int ArmorSprite { get; init; }
    public int BodyColor { get; init; }
    public int BodySpriteId { get; init; }
    public DisplayColor BootsColor { get; init; }
    public int BootsSprite { get; init; }
    public int FaceSprite { get; init; }
    public required Gender Gender { get; init; }
    public DisplayColor HeadColor { get; init; }
    public int HeadSprite { get; init; }
    public DisplayColor OvercoatColor { get; init; }
    public int OvercoatSprite { get; init; }
    public DisplayColor? PantsColor { get; init; }
    public int ShieldSprite { get; init; }
    public int WeaponSprite { get; init; }
    internal char GenderPrefix => IsMale ? 'm' : 'w';

    internal bool IsMale => Gender == Gender.Male;
    internal KhanPalOverrideType OverrideType => IsMale ? KhanPalOverrideType.Male : KhanPalOverrideType.Female;
}

public readonly record struct AislingDrawParams(
    uint EntityId,
    AislingAppearance Appearance,
    int FrameIndex,
    bool Flip,
    bool IsFrontFacing,
    string AnimSuffix,
    int EmotionFrame,
    int GroundPaintHeight,
    Color GroundTintColor,
    float TileCenterX,
    float TileCenterY,
    Vector2 VisualOffset,
    EntityTintType Tint,
    bool IsDead,
    float Alpha = 1f);

/// <summary>
///     Renders aisling sprites by compositing body, equipment, and hair layers. All layers are positioned relative to body
///     center (BODY_CENTER_X, BODY_CENTER_Y). Horizontal flipping (for Down/Left directions) mirrors around the body
///     center.
///     <br />
///     Front-facing layer order (walk frames 5-9): f → acc1G → acc2G → body → pants → face → boots → headH → armor+arms →
///     headE → weaponW+P → shield → acc1C → acc2C
///     <br />
///     Back-facing layer order (walk frames 0-4): acc1G → acc2G → shield → body → face → pants → boots → armor+arms →
///     headE → headH → headF → weaponW+P → acc1C → acc2C
/// </summary>
public sealed class AislingRenderer : IDisposable
{
    private const int BODY_ID = 1;
    private const int PANTS_ID = 1;
    private const int MAX_MALE_HAIR_STYLE = 18;
    private const int MAX_FEMALE_HAIR_STYLE = 17;
    private const int MAX_HAIR_COLOR = 13;
    private const string WALK_ANIM = "01";
    private const string IDLE_ANIM = "04";
    public const int BODY_WIDTH = 57;
    public const int BODY_HEIGHT = 85;
    public const int LAYER_OFFSET_PADDING = 27;
    public const int COMPOSITE_WIDTH = BODY_WIDTH + LAYER_OFFSET_PADDING * 2;
    public const int COMPOSITE_HEIGHT = BODY_HEIGHT;
    public const int BODY_CENTER_X = BODY_WIDTH / 2;
    public const int BODY_CENTER_Y = BODY_HEIGHT / 2;

    //composite canvas anchor: body center within the full padded canvas (111x85).
    //canvas is padded by LAYER_OFFSET_PADDING (27px) on each side, so body center shifts right.
    public const int CANVAS_CENTER_X = BODY_CENTER_X + LAYER_OFFSET_PADDING;
    public const int CANVAS_CENTER_Y = 70;

    //epfs contain frames for 2 base directions only:
    //frames 0-4: up (away-facing)
    //frames 5-9: right (front-facing)
    //down = right frames + horizontal flip, left = up frames + horizontal flip
    private const int UP_IDLE_FRAME = 0;
    private const int RIGHT_IDLE_FRAME = 5;

    private static readonly HashSet<int> WALK_FRONT_FRAMES =
    [
        5,
        6,
        7,
        8,
        9
    ];

    //front-facing composite order (first drawn = back-most)
    public static readonly LayerSlot[] FRONT_ORDER =
    [
        LayerSlot.BodyB,
        LayerSlot.HeadF,
        LayerSlot.Acc1G,
        LayerSlot.Acc2G,
        LayerSlot.Acc3G,
        LayerSlot.Body,
        LayerSlot.Pants,
        LayerSlot.Face,
        LayerSlot.Emotion,
        LayerSlot.Boots,
        LayerSlot.HeadH,
        LayerSlot.Armor,
        LayerSlot.Arms,
        LayerSlot.HeadE,
        LayerSlot.WeaponW,
        LayerSlot.WeaponP,
        LayerSlot.Shield,
        LayerSlot.Acc1C,
        LayerSlot.Acc2C,
        LayerSlot.Acc3C
    ];

    //back-facing composite order
    public static readonly LayerSlot[] BACK_ORDER =
    [
        LayerSlot.BodyB,
        LayerSlot.Acc1G,
        LayerSlot.Acc2G,
        LayerSlot.Acc3G,
        LayerSlot.Shield,
        LayerSlot.Body,
        LayerSlot.Face,
        LayerSlot.Pants,
        LayerSlot.Boots,
        LayerSlot.Armor,
        LayerSlot.Arms,
        LayerSlot.HeadE,
        LayerSlot.HeadH,
        LayerSlot.HeadF,
        LayerSlot.WeaponW,
        LayerSlot.WeaponP,
        LayerSlot.Acc1C,
        LayerSlot.Acc2C,
        LayerSlot.Acc3C
    ];

    private const float GHOST_ALPHA = 0.50f;

    private readonly Dictionary<uint, CompositeEntry> CompositeCache = [];

    private readonly AislingDrawDataRepository DrawData = DataContext.AislingDrawData;
    private readonly Dictionary<Texture2D, Texture2D> GroupTintCache = [];
    private readonly Dictionary<Texture2D, Texture2D> HitTintCache = [];
    private readonly LayerInfo?[] RenderLayers = new LayerInfo?[(int)LayerSlot.Count];
    private readonly Dictionary<int, Texture2D> RestFemaleEmoteFrameCache = [];
    private readonly Dictionary<int, Texture2D> RestFemaleFrameCache = [];
    private readonly Dictionary<int, Texture2D> RestMaleEmoteFrameCache = [];
    private readonly Dictionary<int, Texture2D> RestMaleFrameCache = [];
    private readonly Dictionary<int, Texture2D> SwimFemaleFrameCache = [];

    private readonly Dictionary<int, Texture2D> SwimMaleFrameCache = [];
    private readonly Dictionary<Texture2D, Texture2D> TintedTextureCache = [];

    private static readonly TimeSpan LAYER_IMAGE_CACHE_SLIDING = TimeSpan.FromSeconds(30);
    private MemoryCache LayerImageCache = new(new MemoryCacheOptions());

    /// <inheritdoc />
    public void Dispose()
    {
        ClearCache();
        ClearRestCache();
        ClearSwimCache();
        ClearCompositeCache();
        ClearTintedCache();
        ClearLayerImageCache();
        ClearGroupTintCache();
        ClearHitTintCache();
    }

    /// <summary>
    ///     Clears the cached EPF files. Call on map change to free memory.
    /// </summary>
    public void ClearCache() => DrawData.ClearEpfCache();

    /// <summary>
    ///     Clears all cached composite textures. Call on map change or F5 refresh.
    /// </summary>
    public void ClearCompositeCache()
    {
        foreach (var entry in CompositeCache.Values)
            entry.Texture?.Dispose();

        CompositeCache.Clear();
    }

    private void DisposeCompositeTexture(Texture2D texture)
    {
        if (TintedTextureCache.Remove(texture, out var tinted))
            tinted.Dispose();

        if (GroupTintCache.Remove(texture, out var groupTinted))
            groupTinted.Dispose();

        if (HitTintCache.Remove(texture, out var hitTinted))
            hitTinted.Dispose();

        texture.Dispose();
    }

    /// <summary>
    ///     Disposes all cached group-tinted composite textures. Call when group membership changes.
    /// </summary>
    public void ClearGroupTintCache() => GroupTintCache.DisposeAndClear();

    /// <summary>
    ///     Disposes and clears the hit tint texture cache.
    /// </summary>
    public void ClearHitTintCache() => HitTintCache.DisposeAndClear();

    /// <summary>
    ///     Disposes all cached hover-highlight tinted textures. Call when the highlighted entity changes.
    /// </summary>
    public void ClearTintedCache() => TintedTextureCache.DisposeAndClear();

    /// <summary>
    ///     Disposes all cached per-layer SKImages. Call on map change or renderer disposal.
    /// </summary>
    /// <summary>
    ///     Disposes all cached per-layer SKImages. Call on map change or renderer disposal.
    /// </summary>
    public void ClearLayerImageCache()
    {
        var old = LayerImageCache;
        LayerImageCache = new MemoryCache(new MemoryCacheOptions());
        old.Dispose();
    }

    private SKImage? TryGetCachedLayerImage(in LayerCacheKey key)
        => LayerImageCache.TryGetValue(key, out SKImage? image) ? image : null;

    private void CacheLayerImage(in LayerCacheKey key, SKImage image)
    {
        var options = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(LAYER_IMAGE_CACHE_SLIDING)
            .RegisterPostEvictionCallback(static (_, value, _, _) => (value as IDisposable)?.Dispose());

        LayerImageCache.Set(key, image, options);
    }

    /// <summary>
    ///     Draws an aisling at the given position. Handles composite caching, ground tinting, and highlight/group tinting.
    ///     Returns screen-space texture bottom Y for hitbox calculation, or 0 if not drawn.
    /// </summary>
    public int Draw(SpriteBatch batch, Camera camera, in AislingDrawParams p)
    {
        if (!CompositeCache.TryGetValue(p.EntityId, out var cached)
            || (cached.Appearance != p.Appearance)
            || (cached.FrameIndex != p.FrameIndex)
            || (cached.Flip != p.Flip)
            || (cached.IsFrontFacing != p.IsFrontFacing)
            || (cached.AnimSuffix != p.AnimSuffix)
            || (cached.EmotionFrame != p.EmotionFrame)
            || (cached.GroundPaintHeight != p.GroundPaintHeight))
        {
            var appearance = p.Appearance;

            var texture = Render(
                in appearance,
                p.FrameIndex,
                out var contentBottomY,
                p.AnimSuffix,
                p.Flip,
                p.IsFrontFacing,
                p.EmotionFrame);

            if (texture is null)
                return 0;

            if (p.GroundPaintHeight > 0)
            {
                using var scope = new PixelBufferScope(texture);
                ImageUtil.ApplyGroundTint(scope.Pixels, scope.Width, scope.Height, p.GroundPaintHeight, CANVAS_CENTER_Y, p.GroundTintColor);
                scope.CommitTo(texture);
            }

            if (cached.Texture is not null)
                DisposeCompositeTexture(cached.Texture);

            cached = new CompositeEntry
            {
                Appearance = p.Appearance,
                FrameIndex = p.FrameIndex,
                Flip = p.Flip,
                IsFrontFacing = p.IsFrontFacing,
                AnimSuffix = p.AnimSuffix,
                EmotionFrame = p.EmotionFrame,
                GroundPaintHeight = p.GroundPaintHeight,
                Texture = texture,
                ContentBottomY = contentBottomY
            };
            CompositeCache[p.EntityId] = cached;
        }

        if (cached.Texture is null)
            return 0;

        var drawTexture = cached.Texture;

        var baseX = p.TileCenterX + p.VisualOffset.X - CANVAS_CENTER_X;
        var baseY = p.TileCenterY + p.VisualOffset.Y - CANVAS_CENTER_Y;
        var screenPos = camera.WorldToScreen(new Vector2(baseX, baseY));

        var finalTexture = p.Tint switch
        {
            EntityTintType.Highlight => TintedTextureCache.GetOrAdd(drawTexture, static src => ImageUtil.BuildHoverTinted(TextureConverter.Device, src)),
            EntityTintType.Group     => GroupTintCache.GetOrAdd(drawTexture, static src => ImageUtil.BuildGroupTinted(TextureConverter.Device, src)),
            EntityTintType.HitTint   => HitTintCache.GetOrAdd(drawTexture, static src => ImageUtil.BuildHitTinted(TextureConverter.Device, src)),
            _                        => drawTexture
        };

        //dead aislings render as translucent ghosts via uniform alpha; transparent wins over dead at the call site
        //so we never stack both modulations here.
        var effectiveAlpha = p.IsDead ? p.Alpha * GHOST_ALPHA : p.Alpha;
        batch.Draw(finalTexture, screenPos, Color.White * effectiveAlpha);

        return (int)screenPos.Y + cached.ContentBottomY;
    }

    /// <summary>
    ///     Returns the X draw offset for a layer type. Weapons (w/p) and accessories (c/g) are shifted left by 27px relative
    ///     to the body center to align correctly.
    /// </summary>
    public static int GetLayerOffsetX(char typeLetter) => typeLetter is 'w' or 'p' or 'c' or 'g' ? -27 : 0;

    

    

    /// <summary>
    ///     Returns a tinted (blue-shifted) copy of a layer texture, caching it for reuse.
    /// </summary>
    

    /// <summary>
    ///     Returns true if a frame index represents a front-facing direction for the given animation suffix.
    /// </summary>
    public static bool IsFrontFacing(int frameIndex, string animSuffix)
        => animSuffix switch
        {
            "01" => WALK_FRONT_FRAMES.Contains(frameIndex),
            "02" => frameIndex is 2 or 3,
            "03" => frameIndex is 1 or 4 or 5 or 8 or 9,
            _    => frameIndex >= 5
        };

    /// <summary>
    ///     Removes a single entity's cached composite. Call when an entity leaves the map.
    /// </summary>
    public void RemoveCachedEntity(uint entityId)
    {
        if (CompositeCache.Remove(entityId, out var removed) && removed.Texture is not null)
            DisposeCompositeTexture(removed.Texture);
    }

    #region Palette Resolution
    private Palette? ResolvePalette(
        PaletteLookup lookup,
        int spriteId,
        DisplayColor dyeColor,
        KhanPalOverrideType overrideType)
    {
        var paletteNumber = lookup.Table.GetPaletteNumber(spriteId, overrideType);

        if (paletteNumber >= 1000)
            paletteNumber -= 1000;

        if (!lookup.Palettes.TryGetValue(paletteNumber, out var basePalette))
            return null;

        return DrawData.ApplyDye(basePalette, dyeColor);
    }
    #endregion

    /// <summary>
    ///     Per-entity cached composite: stores the appearance state at render time alongside the composited texture, enabling
    ///     cache invalidation when any visual parameter changes.
    /// </summary>
    private record struct CompositeEntry
    {
        public AislingAppearance Appearance { get; init; }
        public int FrameIndex { get; init; }
        public bool Flip { get; init; }

        // ReSharper disable once MemberHidesStaticFromOuterClass
        public bool IsFrontFacing { get; init; }
        public string AnimSuffix { get; init; }
        public int EmotionFrame { get; init; }
        public int GroundPaintHeight { get; init; }
        public Texture2D? Texture { get; init; }
        public int ContentBottomY { get; init; }
    }

    //layer image is NOT owned by this struct — lifetime is managed by LayerImageCache (LRU + sliding expiration
    //eviction disposes). Render's finally block no longer disposes per-layer images.
    private readonly record struct LayerInfo
    {
        public SKImage Image { get; }
        public char TypeLetter { get; }

        public LayerInfo(SKImage image, char typeLetter)
        {
            Image = image;
            TypeLetter = typeLetter;
        }
    }
    
    [SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Local")]
    private readonly record struct LayerCacheKey(
        char TypeLetter,
        int SpriteId,
        int ColorCode,
        bool IsMale,
        KhanPalOverrideType PaletteOverride,
        string AnimSuffix,
        int FrameIndex,
        int IdleFallbackFrame);


    #region Composited Rendering (paperdoll/preview path)
    /// <summary>
    ///     Composites all layers into a single image. Used for paperdoll and character creation preview.
    /// </summary>
    private static SKImage? Composite(LayerInfo?[] layers, LayerSlot[] order, bool flipHorizontal, out int contentBottomY)
    {
        var width = COMPOSITE_WIDTH;
        var height = COMPOSITE_HEIGHT;
        contentBottomY = 0;

        using var bitmap = new SKBitmap(width, height);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);

            if (flipHorizontal)
                canvas.Scale(
                    -1,
                    1,
                    BODY_CENTER_X + LAYER_OFFSET_PADDING,
                    0);

            //layers are all drawn at Y=0, so each layer's bottom row is its image height. the deepest layer
            //defines the content bottom — equipment extending below the feet anchor pushes the hitbox down,
            //nothing below the feet leaves the hitbox at the feet.
            foreach (var slot in order)
            {
                if (layers[(int)slot] is not { } info)
                    continue;

                var offsetX = GetLayerOffsetX(info.TypeLetter) + LAYER_OFFSET_PADDING;
                canvas.DrawImage(info.Image, offsetX, 0);

                if (info.Image.Height > contentBottomY)
                    contentBottomY = info.Image.Height;
            }
        }

        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>
    ///     Renders a full aisling with all visible equipment layers into a single composited texture. Used by both world
    ///     rendering (via Draw, which caches the result per entity) and paperdoll/preview contexts.
    /// </summary>
    public Texture2D? Render(
        in AislingAppearance appearance,
        int frameIndex,
        string animSuffix = WALK_ANIM,
        bool flipHorizontal = false,
        bool? isFrontFacing = null,
        int emotionFrame = -1)
        => Render(
            in appearance,
            frameIndex,
            out _,
            animSuffix,
            flipHorizontal,
            isFrontFacing,
            emotionFrame);

    public Texture2D? Render(
        in AislingAppearance appearance,
        int frameIndex,
        out int contentBottomY,
        string animSuffix = WALK_ANIM,
        bool flipHorizontal = false,
        bool? isFrontFacing = null,
        int emotionFrame = -1)
    {
        contentBottomY = 0;
        var layers = RenderLayers;
        Array.Clear(layers, 0, layers.Length);

        try
        {
            var isFront = isFrontFacing ?? IsFrontFacing(frameIndex, animSuffix);

            var idleFallbackFrame = animSuffix == IDLE_ANIM ? isFront ? RIGHT_IDLE_FRAME : UP_IDLE_FRAME : -1;

            RenderAllLayers(
                layers,
                in appearance,
                frameIndex,
                animSuffix,
                idleFallbackFrame);

            var emotionsEpf = DrawData.EmotionsEpf;

            if (isFront && (emotionFrame >= 0) && emotionsEpf is not null && (emotionFrame < emotionsEpf.Count))
            {
                //sentinel typeLetter '!' prevents collision with the face layer ('o') which uses the same palette.
                var emotionKey = new LayerCacheKey(
                    '!',
                    0,
                    appearance.BodyColor,
                    false,
                    KhanPalOverrideType.None,
                    string.Empty,
                    emotionFrame,
                    -1);

                var cachedEmotion = TryGetCachedLayerImage(in emotionKey);

                if (cachedEmotion is not null)
                    layers[(int)LayerSlot.Emotion] = new LayerInfo(cachedEmotion, 'o');
                else if (DrawData.BodyPalettes.TryGetValue(appearance.BodyColor, out var palette))
                {
                    var frame = emotionsEpf[emotionFrame];
                    var image = Graphics.RenderImage(frame, palette);

                    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                    if (image is not null)
                    {
                        CacheLayerImage(in emotionKey, image);
                        layers[(int)LayerSlot.Emotion] = new LayerInfo(image, 'o');
                    }
                }
            }

            if (!layers[(int)LayerSlot.Body].HasValue && !layers[(int)LayerSlot.BodyB].HasValue)
                return null;

            var order = isFront ? FRONT_ORDER : BACK_ORDER;

            using var composite = Composite(layers, order, flipHorizontal, out contentBottomY);

            return composite is not null ? TextureConverter.ToTexture2D(composite) : null;
        } finally
        {
            //layer SKImages are owned by LayerImageCache — do not dispose them here, just release references.
            Array.Clear(layers, 0, layers.Length);
        }
    }

    /// <summary>
    ///     Renders a character creation preview (body + hair layers only).
    /// </summary>
    public Texture2D? RenderPreview(
        Gender gender,
        byte hairStyle,
        DisplayColor hairColor,
        int directionIndex = 3,
        int walkFrame = 0)
    {
        var isMale = gender == Gender.Male;
        var maxHairStyle = isMale ? MAX_MALE_HAIR_STYLE : MAX_FEMALE_HAIR_STYLE;

        if (hairStyle > maxHairStyle)
            return null;

        if ((int)hairColor > MAX_HAIR_COLOR)
            return null;

        var appearance = new AislingAppearance
        {
            Gender = gender,
            HeadSprite = hairStyle,
            HeadColor = hairColor
        };

        var baseFrame = directionIndex is 0 or 3 ? UP_IDLE_FRAME : RIGHT_IDLE_FRAME;
        var frameIndex = baseFrame + Math.Clamp(walkFrame, 0, 4);
        var flip = directionIndex is 2 or 3;

        return Render(
            in appearance,
            frameIndex,
            WALK_ANIM,
            flip);
    }
    #endregion

    #region Layer Rendering (composited path)
    private void RenderAllLayers(
        LayerInfo?[] layers,
        in AislingAppearance appearance,
        int frameIndex,
        string anim,
        int idleFallbackFrame = -1)
    {
        var bodySpriteId = appearance.BodySpriteId > 0 ? appearance.BodySpriteId : BODY_ID;

        layers[(int)LayerSlot.BodyB] = RenderEquipLayer(
            'b',
            bodySpriteId,
            DisplayColor.Default,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);

        if (bodySpriteId == BODY_ID)
            layers[(int)LayerSlot.Body] = RenderBodyPaletteLayer(
                'm',
                BODY_ID,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

        if (appearance.PantsColor.HasValue)
            layers[(int)LayerSlot.Pants] = RenderEquipLayer(
                'n',
                PANTS_ID,
                appearance.PantsColor.Value,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

        if (appearance.FaceSprite > 0)
            layers[(int)LayerSlot.Face] = RenderBodyPaletteLayer(
                'o',
                appearance.FaceSprite,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

        if (appearance.BootsSprite > 0)
            layers[(int)LayerSlot.Boots] = RenderEquipLayer(
                'l',
                appearance.BootsSprite,
                appearance.BootsColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

        if (appearance.HeadSprite > 0)
        {
            layers[(int)LayerSlot.HeadH] = RenderEquipLayer(
                'h',
                appearance.HeadSprite,
                appearance.HeadColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.HeadE] = RenderEquipLayer(
                'e',
                appearance.HeadSprite,
                appearance.HeadColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.HeadF] = RenderEquipLayer(
                'f',
                appearance.HeadSprite,
                appearance.HeadColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        }

        if (appearance.OvercoatSprite > 0)
            RenderArmorLayers(
                layers,
                appearance.OvercoatSprite,
                appearance.OvercoatColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        else if (appearance.ArmorSprite > 0)
            RenderArmorLayers(
                layers,
                appearance.ArmorSprite,
                appearance.ArmorColor,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

        if (appearance.WeaponSprite > 0)
        {
            layers[(int)LayerSlot.WeaponW] = RenderEquipLayer(
                'w',
                appearance.WeaponSprite,
                DisplayColor.Default,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.WeaponP] = RenderEquipLayer(
                'p',
                appearance.WeaponSprite,
                DisplayColor.Default,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        }

        if (appearance.ShieldSprite > 0)
            layers[(int)LayerSlot.Shield] = RenderEquipLayer(
                's',
                appearance.ShieldSprite,
                DisplayColor.Default,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

        if (appearance.Accessory1Sprite > 0)
        {
            layers[(int)LayerSlot.Acc1C] = RenderEquipLayer(
                'c',
                appearance.Accessory1Sprite,
                appearance.Accessory1Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.Acc1G] = RenderEquipLayer(
                'g',
                appearance.Accessory1Sprite,
                appearance.Accessory1Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        }

        if (appearance.Accessory2Sprite > 0)
        {
            layers[(int)LayerSlot.Acc2C] = RenderEquipLayer(
                'c',
                appearance.Accessory2Sprite,
                appearance.Accessory2Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.Acc2G] = RenderEquipLayer(
                'g',
                appearance.Accessory2Sprite,
                appearance.Accessory2Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        }

        if (appearance.Accessory3Sprite > 0)
        {
            layers[(int)LayerSlot.Acc3C] = RenderEquipLayer(
                'c',
                appearance.Accessory3Sprite,
                appearance.Accessory3Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);

            layers[(int)LayerSlot.Acc3G] = RenderEquipLayer(
                'g',
                appearance.Accessory3Sprite,
                appearance.Accessory3Color,
                in appearance,
                frameIndex,
                anim,
                idleFallbackFrame);
        }
    }

    private void RenderArmorLayers(
        LayerInfo?[] layers,
        int spriteId,
        DisplayColor color,
        in AislingAppearance appearance,
        int frameIndex,
        string anim,
        int idleFallbackFrame = -1)
    {
        var isOverType = spriteId >= 1000;
        var adjustedId = isOverType ? spriteId - 1000 : spriteId;
        var bodyLetter = isOverType ? 'i' : 'u';
        var armsLetter = isOverType ? 'j' : 'a';

        layers[(int)LayerSlot.Armor] = RenderEquipLayer(
            bodyLetter,
            adjustedId,
            color,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);

        layers[(int)LayerSlot.Arms] = RenderEquipLayer(
            armsLetter,
            adjustedId,
            color,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);
    }

    private LayerInfo? RenderBodyPaletteLayer(
        char typeLetter,
        int spriteId,
        in AislingAppearance appearance,
        int frameIndex,
        string anim,
        int idleFallbackFrame = -1)
    {
        if (spriteId <= 0)
            return null;

        var cacheKey = new LayerCacheKey(
            typeLetter,
            spriteId,
            appearance.BodyColor,
            appearance.IsMale,
            KhanPalOverrideType.None,
            anim,
            frameIndex,
            idleFallbackFrame);

        var cachedImage = TryGetCachedLayerImage(in cacheKey);

        if (cachedImage is not null)
            return new LayerInfo(cachedImage, typeLetter);

        (var epf, var resolvedFrame) = ResolveLayerEpf(
            typeLetter,
            spriteId,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);

        if (epf is null || (resolvedFrame < 0))
            return null;

        var frame = epf[resolvedFrame];

        if (!DrawData.BodyPalettes.TryGetValue(appearance.BodyColor, out var palette))
            return null;

        var image = Graphics.RenderImage(frame, palette);

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (image is null)
            return null;

        CacheLayerImage(in cacheKey, image);

        return new LayerInfo(image, typeLetter);
    }

    private LayerInfo? RenderEquipLayer(
        char typeLetter,
        int spriteId,
        DisplayColor dyeColor,
        in AislingAppearance appearance,
        int frameIndex,
        string anim,
        int idleFallbackFrame = -1)
    {
        if (spriteId <= 0)
            return null;

        // Shields always use the male override since the EPF file is loaded from khanmns.
        var paletteOverride = typeLetter == 's' ? KhanPalOverrideType.Male : appearance.OverrideType;

        var cacheKey = new LayerCacheKey(
            typeLetter,
            spriteId,
            (int)dyeColor,
            appearance.IsMale,
            paletteOverride,
            anim,
            frameIndex,
            idleFallbackFrame);

        var cachedImage = TryGetCachedLayerImage(in cacheKey);

        if (cachedImage is not null)
            return new LayerInfo(cachedImage, typeLetter);

        (var epf, var resolvedFrame) = ResolveLayerEpf(
            typeLetter,
            spriteId,
            in appearance,
            frameIndex,
            anim,
            idleFallbackFrame);

        if (epf is null || (resolvedFrame < 0))
            return null;

        var frame = epf[resolvedFrame];

        //empty-frame marker — equipment has no visual at this animation pose.
        //Return null so the layer is skipped during composition; body and other layers still
        //render normally so the body animation keeps its timing/progression.
        if ((frame.PixelWidth == 0) || (frame.PixelHeight == 0))
            return null;

        var lookup = DrawData.GetPaletteLookup(typeLetter);

        var palette = ResolvePalette(
            lookup,
            spriteId,
            dyeColor,
            paletteOverride);

        if (palette is null)
            return null;

        var image = Graphics.RenderImage(frame, palette);

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (image is null)
            return null;

        CacheLayerImage(in cacheKey, image);

        return new LayerInfo(image, typeLetter);
    }
    #endregion

    #region Swimming
    /// <summary>
    ///     Gets the max frame width across all swimming frames for a gender. Used for consistent horizontal centering.
    /// </summary>
    public int GetSwimMaxFrameWidth(bool isFemale)
    {
        var data = DrawData.GetSwimData(isFemale);

        return data?.MaxFrameWidth ?? 0;
    }

    /// <summary>
    ///     Gets the total number of swimming frames available for a gender. Returns 0 if no swimming sprite is available.
    /// </summary>
    public int GetSwimFrameCount(bool isFemale)
    {
        var data = DrawData.GetSwimData(isFemale);

        return data?.Epf.Count ?? 0;
    }

    /// <summary>
    ///     Gets a cached swimming frame texture by index. Returns null if unavailable.
    /// </summary>
    public Texture2D? GetSwimFrame(bool isFemale, int frameIndex)
    {
        var data = DrawData.GetSwimData(isFemale);

        if (data is not { } swim)
            return null;

        if ((frameIndex < 0) || (frameIndex >= swim.Epf.Count))
            return null;

        var cache = isFemale ? SwimFemaleFrameCache : SwimMaleFrameCache;

        if (cache.TryGetValue(frameIndex, out var cached))
            return cached;

        var frame = swim.Epf[frameIndex];

        using var image = Graphics.RenderImage(frame, swim.Palette);

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (image is null)
            return null;

        var texture = TextureConverter.ToTexture2D(image);
        cache[frameIndex] = texture;

        return texture;
    }

    /// <summary>
    ///     Draws a single swimming frame for an aisling. Handles horizontal centering via maxWidth, flip compensation, and
    ///     CANVAS_CENTER_Y-based vertical anchoring. Returns the screen-space Y of the texture bottom, or 0 if the frame
    ///     texture is unavailable.
    /// </summary>
    public int DrawSwimming(
        SpriteBatch batch,
        Camera camera,
        bool isFemale,
        int swimFrame,
        bool flip,
        float tileCenterX,
        float tileCenterY,
        Vector2 visualOffset)
    {
        var texture = GetSwimFrame(isFemale, swimFrame);

        if (texture is null)
            return 0;

        var maxWidth = GetSwimMaxFrameWidth(isFemale);
        var drawX = tileCenterX + visualOffset.X - maxWidth / 2;

        //when flipped, compensate for the difference between maxwidth and actual texture width
        //so the flip pivot stays at the center of maxwidth rather than the center of the texture
        if (flip)
            drawX += maxWidth - texture.Width;

        var drawY = tileCenterY + visualOffset.Y - CANVAS_CENTER_Y;
        var screenPos = camera.WorldToScreen(new Vector2(drawX, drawY));
        var effects = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

        batch.Draw(
            texture,
            screenPos,
            null,
            Color.White,
            0f,
            Vector2.Zero,
            1f,
            effects,
            0f);

        return (int)screenPos.Y + texture.Height;
    }

    /// <summary>
    ///     Clears all cached swimming frame textures.
    /// </summary>
    public void ClearSwimCache()
    {
        foreach (var tex in SwimMaleFrameCache.Values)
            tex.Dispose();

        foreach (var tex in SwimFemaleFrameCache.Values)
            tex.Dispose();

        SwimMaleFrameCache.Clear();
        SwimFemaleFrameCache.Clear();
    }
    #endregion

    #region Rest Position
    /// <summary>
    ///     Gets a cached rest position frame texture. Base SPFs have 2 frames: frame 0 = away (Up/Left), frame 1 = front
    ///     (Right/Down). Emote SPFs have 42 front-facing frames matching emot01.epf indices.
    /// </summary>
    public Texture2D? GetRestFrame(bool isFemale, RestPosition restPos, bool isFrontFacing, int emoteFrame = -1)
    {
        var pos = (int)restPos;

        if (pos is < 1 or > 3)
            return null;

        //front-facing with active emote: use the emote spf composite
        if (isFrontFacing && (emoteFrame >= 0))
        {
            var emoteSpf = DrawData.GetRestSpf(isFemale, pos, true);

            if (emoteSpf is not null && (emoteFrame < emoteSpf.Count))
            {
                var emoteCache = isFemale ? RestFemaleEmoteFrameCache : RestMaleEmoteFrameCache;
                var emoteCacheKey = pos * 100 + emoteFrame;

                if (emoteCache.TryGetValue(emoteCacheKey, out var emoteCached))
                    return emoteCached;

                using var emoteImage = SpfRenderer.RenderFrame(emoteSpf, emoteFrame);

                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (emoteImage is null)
                    return null;

                var emoteTexture = TextureConverter.ToTexture2D(emoteImage);
                emoteCache[emoteCacheKey] = emoteTexture;

                return emoteTexture;
            }
        }

        //base rest sprite
        var spf = DrawData.GetRestSpf(isFemale, pos, false);

        if (spf is null || (spf.Count < 2))
            return null;

        var frameIndex = isFrontFacing ? 1 : 0;
        var cacheKey = pos * 10 + frameIndex;
        var cache = isFemale ? RestFemaleFrameCache : RestMaleFrameCache;

        if (cache.TryGetValue(cacheKey, out var cached))
            return cached;

        using var image = SpfRenderer.RenderFrame(spf, frameIndex);

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (image is null)
            return null;

        var texture = TextureConverter.ToTexture2D(image);
        cache[cacheKey] = texture;

        return texture;
    }

    /// <summary>
    ///     Draws a rest position sprite (Kneel/Lay/Sprawl) for an aisling. When front-facing with an active emote, renders the
    ///     full body+emote composite from the "e" SPF variant. Otherwise renders the base rest SPF. Emotes are only visible
    ///     from the front — away-facing always shows the plain rest sprite.
    /// </summary>
    public int DrawResting(
        SpriteBatch batch,
        Camera camera,
        bool isFemale,
        RestPosition restPos,
        bool isFrontFacing,
        bool flip,
        float tileCenterX,
        float tileCenterY,
        Vector2 visualOffset,
        int emoteFrame = -1)
    {
        var texture = GetRestFrame(isFemale, restPos, isFrontFacing, emoteFrame);

        if (texture is null)
            return 0;

        var drawX = tileCenterX + visualOffset.X - BODY_CENTER_X;
        var drawY = tileCenterY + visualOffset.Y - CANVAS_CENTER_Y;

        //flip compensation: SpriteEffects.FlipHorizontally mirrors around texture.Width/2,
        //but the canonical SPF canvas is 2*BODY_CENTER_X wide. shift to keep the canvas
        //center as the mirror axis instead of the bounding-box center.
        if (flip)
            drawX += 2 * BODY_CENTER_X - texture.Width;

        var screenPos = camera.WorldToScreen(new Vector2(drawX, drawY));
        var effects = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

        batch.Draw(
            texture,
            screenPos,
            null,
            Color.White,
            0f,
            Vector2.Zero,
            1f,
            effects,
            0f);

        return (int)screenPos.Y + texture.Height;
    }

    /// <summary>
    ///     Clears all cached rest position frame textures.
    /// </summary>
    public void ClearRestCache()
    {
        foreach (var tex in RestMaleFrameCache.Values)
            tex.Dispose();

        foreach (var tex in RestFemaleFrameCache.Values)
            tex.Dispose();

        foreach (var tex in RestMaleEmoteFrameCache.Values)
            tex.Dispose();

        foreach (var tex in RestFemaleEmoteFrameCache.Values)
            tex.Dispose();

        RestMaleFrameCache.Clear();
        RestFemaleFrameCache.Clear();
        RestMaleEmoteFrameCache.Clear();
        RestFemaleEmoteFrameCache.Clear();
    }
    #endregion

    #region Helpers
    /// <summary>
    ///     Loads the EPF for a layer, handling idle animation ("04") frame wrapping and fallback to walk animation ("01") when
    ///     the requested animation file doesn't exist.
    /// </summary>
    private (EpfView? Epf, int FrameIndex) ResolveLayerEpf(
        char typeLetter,
        int spriteId,
        in AislingAppearance appearance,
        int frameIndex,
        string anim,
        int idleFallbackFrame)
    {
        // Shields always load from the male archive (khanmns.dat) regardless of gender.
        // Retail hardcodes the filename prefix to 'm' for the shield slot; khanwns.dat
        // only contains vestigial entries for 3 sprite IDs that the paperdoll builder
        // never requests.
        var useMale = typeLetter == 's' || appearance.IsMale;
        var genderPrefix = useMale ? 'm' : 'w';

        var fileName = $"{genderPrefix}{typeLetter}{spriteId:D3}{anim}";
        var epf = TryLoadEpf(typeLetter, useMale, fileName);

        if ((anim == IDLE_ANIM) && epf is not null && (epf.Count >= 2) && (idleFallbackFrame >= 0))
        {
            var framesPerDir = epf.Count / 2;
            var dirBase = idleFallbackFrame == RIGHT_IDLE_FRAME ? framesPerDir : 0;
            frameIndex = dirBase + frameIndex % framesPerDir;
        } else if ((epf is null || (frameIndex >= epf.Count)) && (idleFallbackFrame >= 0))
        {
            fileName = $"{genderPrefix}{typeLetter}{spriteId:D3}{WALK_ANIM}";
            epf = TryLoadEpf(typeLetter, useMale, fileName);
            frameIndex = idleFallbackFrame;
        }

        if (epf is null || (frameIndex >= epf.Count))
            return (null, -1);

        return (epf, frameIndex);
    }

    private EpfView? TryLoadEpf(char typeLetter, bool isMale, string fileName) => DrawData.GetEquipmentEpf(typeLetter, isMale, fileName);

    /// <summary>
    ///     Returns the maximum idle animation frame count (per direction) across all equipped layers that have an idle ("04")
    ///     EPF file. Returns 0 if no layers have idle animations.
    /// </summary>
    public int GetIdleAnimFrameCount(in AislingAppearance appearance)
    {
        var maxFrames = 0;

        CheckIdleEpf(
            ref maxFrames,
            'c',
            appearance.Accessory1Sprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'g',
            appearance.Accessory1Sprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'c',
            appearance.Accessory2Sprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'g',
            appearance.Accessory2Sprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'c',
            appearance.Accessory3Sprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'g',
            appearance.Accessory3Sprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'h',
            appearance.HeadSprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'e',
            appearance.HeadSprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'f',
            appearance.HeadSprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'w',
            appearance.WeaponSprite,
            in appearance);

        CheckIdleEpf(
            ref maxFrames,
            'p',
            appearance.WeaponSprite,
            in appearance);

        return maxFrames;
    }

    private void CheckIdleEpf(
        ref int maxFrames,
        char typeLetter,
        int spriteId,
        in AislingAppearance appearance)
    {
        if (spriteId <= 0)
            return;

        var fileName = $"{appearance.GenderPrefix}{typeLetter}{spriteId:D3}{IDLE_ANIM}";
        var epf = TryLoadEpf(typeLetter, appearance.IsMale, fileName);

        if (epf is not null && (epf.Count >= 2))
            maxFrames = Math.Max(maxFrames, epf.Count / 2);
    }

    /// <summary>
    ///     Returns true if the displayed armor/overcoat supports the given body animation. Some armor sprites lack
    ///     ability-specific animation frames and must fall back to the default body animation.
    /// </summary>
    public bool HasArmorAnimation(in AislingAppearance appearance, BodyAnimation anim)
    {
        var spriteId = appearance.OvercoatSprite > 0
            ? appearance.OvercoatSprite
            : appearance.ArmorSprite > 0
                ? appearance.ArmorSprite
                : 0;

        return DataContext.AislingDrawData.AbilityAnimations.IsAbilityAnimationAllowed(anim, spriteId);
    }
    #endregion
}