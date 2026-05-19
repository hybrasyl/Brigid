#region
using Chaos.Client.Data;
using Chaos.Client.Data.AssetPacks;
using Chaos.Client.Rendering.Models;
using Chaos.Client.Rendering.Utility;
using DALib.Definitions;
using DALib.Drawing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Renders creature/NPC sprites from MpfFile data with a per-frame Texture2D cache. Cached frames are keyed by
///     (spriteId, frameIndex). Call <see cref="Clear" /> on map change to evict all cached textures.
/// </summary>
public sealed class CreatureRenderer : IDisposable
{
    private readonly Dictionary<(int SpriteId, int FrameIndex), SpriteFrame> FrameCache = [];
    private readonly Dictionary<Texture2D, Texture2D> GroupTintCache = [];
    private readonly Dictionary<Texture2D, Texture2D> HighlightTintCache = [];
    private readonly Dictionary<Texture2D, Texture2D> HitTintCache = [];

    //keyed by (source frame texture, paint height, packed argb) — one entry per unique gndattr/tile combo per frame. Cleared on map change.
    private readonly Dictionary<(Texture2D Source, int PaintHeight, uint PackedColor), Texture2D> GroundTintCache = [];

    //average of (CenterY - Top) across all frames, keyed by spriteId.
    //used by overlay positioning to derive a stable "sprite top" for each creature sprite.
    //uses the frame's visible top row, which differs from the bitmap top row when Top > 0 (transparent padding).
    private readonly Dictionary<int, int> AverageTopOffsetCache = [];

    //per-pack-creature union bbox in source-image coordinates: the tightest rect that contains every visible pixel
    //across all of this creature's pair-master frames. Every frame is cropped to this same rect so the anchor stays
    //stable across frames (preserves relative position when phase 2 introduces multi-frame animation). Computed
    //lazily on first frame request, cleared on map change.
    private readonly Dictionary<int, SKRectI> PackCreatureUnionBboxCache = [];

    /// <inheritdoc />
    public void Dispose() => Clear();

    /// <summary>
    ///     Disposes all cached frame and tint textures. Call on map change.
    /// </summary>
    public void Clear()
    {
        FrameCache.DisposeAndClear();
        AverageTopOffsetCache.Clear();
        PackCreatureUnionBboxCache.Clear();
        GroundTintCache.DisposeAndClear();
        ClearTintCaches();
    }

    /// <summary>
    ///     Disposes all cached highlight and group tint textures. Call when tint state changes (e.g., hovered entity changes).
    /// </summary>
    public void ClearTintCaches()
    {
        HighlightTintCache.DisposeAndClear();
        GroupTintCache.DisposeAndClear();
        HitTintCache.DisposeAndClear();
    }

    /// <summary>
    ///     Draws a creature sprite at the given tile center, applying the requested tint. Returns the screen-space Y of the
    ///     texture bottom edge, or 0 if the sprite could not be drawn.
    /// </summary>
    public int Draw(
        SpriteBatch batch,
        Camera camera,
        int spriteId,
        int frameIndex,
        bool flip,
        float tileCenterX,
        float tileCenterY,
        Vector2 visualOffset,
        EntityTintType tint,
        int groundPaintHeight,
        Color groundTintColor,
        float alpha = 1f)
    {
        var spriteFrame = GetFrame(spriteId, frameIndex);

        if (spriteFrame is null)
            return 0;

        var frame = spriteFrame.Value;

        var texCenterX = frame.CenterX - Math.Min(0, (int)frame.Left);
        var texCenterY = frame.CenterY - Math.Min(0, (int)frame.Top);
        var anchorX = flip ? frame.Texture.Width - texCenterX : texCenterX;

        var drawX = tileCenterX + visualOffset.X - anchorX;
        var drawY = tileCenterY + visualOffset.Y - texCenterY;
        var screenPos = camera.WorldToScreen(new Vector2(drawX, drawY));

        if (alpha > 0)
        {
            var effects = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            //ground tint baked into a per-(source, paintHeight, color) cache first; entity tint chains on top of the result.
            var sourceTexture = frame.Texture;

            if (groundPaintHeight > 0)
            {
                var key = (Source: frame.Texture, PaintHeight: groundPaintHeight, PackedColor: groundTintColor.PackedValue);

                if (!GroundTintCache.TryGetValue(key, out var groundTinted))
                {
                    groundTinted = ImageUtil.BuildGroundTinted(
                        TextureConverter.Device,
                        frame.Texture,
                        groundPaintHeight,
                        frame.CenterY,
                        groundTintColor);
                    GroundTintCache[key] = groundTinted;
                }

                sourceTexture = groundTinted;
            }

            var drawTexture = tint switch
            {
                EntityTintType.Highlight => HighlightTintCache.GetOrAdd(sourceTexture, static src => ImageUtil.BuildHoverTinted(TextureConverter.Device, src)),
                EntityTintType.Group     => GroupTintCache.GetOrAdd(sourceTexture, static src => ImageUtil.BuildGroupTinted(TextureConverter.Device, src)),
                EntityTintType.HitTint   => HitTintCache.GetOrAdd(sourceTexture, static src => ImageUtil.BuildHitTinted(TextureConverter.Device, src)),
                _                        => sourceTexture
            };

            batch.Draw(
                drawTexture,
                screenPos,
                null,
                Color.White * alpha,
                0f,
                Vector2.Zero,
                1f,
                effects,
                0f);
        }

        return (int)screenPos.Y + frame.Texture.Height;
    }

    /// <summary>
    ///     Returns the average height above the entity anchor reached by this sprite's frames, in pixels. Used by overlay
    ///     positioning to derive a stable "sprite top" that accounts for frame-to-frame variance without per-frame jitter.
    ///     Cached per spriteId after the first call. Returns 0 if the sprite cannot be loaded. For pack-served
    ///     creatures the value is simply the decoded image height (bottom-center anchor → entire image is above
    ///     anchor).
    /// </summary>
    public int GetAverageTopOffset(int spriteId)
    {
        if (AverageTopOffsetCache.TryGetValue(spriteId, out var cached))
            return cached;

        var pack = AssetPackRegistry.GetCreaturePack();

        if (pack is not null && pack.Covers(spriteId))
        {
            var packFrame = GetFrame(spriteId, 0);

            if (packFrame is null)
                return 0;

            var height = packFrame.Value.Texture.Height;
            AverageTopOffsetCache[spriteId] = height;

            return height;
        }

        var palettized = DataContext.CreatureSprites.GetCreatureSprite(spriteId);

        if (palettized is null)
            return 0;

        var mpf = palettized.Entity;

        if (mpf.Count == 0)
            return 0;

        var sum = 0;

        for (var i = 0; i < mpf.Count; i++)
        {
            var frame = mpf[i];
            sum += frame.CenterY - frame.Top;
        }

        var average = sum / mpf.Count;
        AverageTopOffsetCache[spriteId] = average;

        return average;
    }

    /// <summary>
    ///     Returns animation metadata for a creature sprite — walk/attack/standing frame ranges and total frame count. Returns
    ///     null if the sprite cannot be loaded. Probes the modern <c>creature_sprites</c> pack first; on miss falls
    ///     back to the legacy MPF metadata.
    /// </summary>
    public CreatureAnimInfo? GetAnimInfo(int spriteId)
    {
        var pack = AssetPackRegistry.GetCreaturePack();

        if (pack is not null && pack.Covers(spriteId))
            return SynthesizeStaticAnimInfo(pack.GetPairCount(spriteId));

        var palettized = DataContext.CreatureSprites.GetCreatureSprite(spriteId);

        if (palettized is null)
            return null;

        var mpf = palettized.Entity;

        return new CreatureAnimInfo(
            mpf.WalkFrameIndex,
            mpf.WalkFrameCount,
            mpf.AttackFrameIndex,
            mpf.AttackFrameCount,
            mpf.Attack2StartIndex,
            mpf.Attack2FrameCount,
            mpf.Attack3StartIndex,
            mpf.Attack3FrameCount,
            mpf.StandingFrameIndex,
            mpf.StandingFrameCount,
            mpf.OptionalAnimationFrameCount,
            mpf.OptionalAnimationProbability,
            mpf.AnimationIntervalMs,
            mpf.Count);
    }

    /// <summary>
    ///     Synthesizes single-frame <see cref="CreatureAnimInfo" /> for a pack-served creature. All animation
    ///     ranges (walk, attack, attack2, attack3, standing) collapse to a single frame so
    ///     <c>AnimationSystem.GetCreatureFrame</c> always returns frame 0 (or frame 1 for the second pair when both
    ///     <c>nw</c> and <c>es</c> masters exist). The legacy single-direction-sprite check
    ///     (<c>WalkFrameIndex + 2*WalkFrameCount + frame &gt;= TotalFrameCount</c>) yields the right behavior:
    ///     single-pair → reuse frame 0 for all directions, two-pair → frame 0 for N/W, frame 1 for E/S.
    /// </summary>
    private static CreatureAnimInfo SynthesizeStaticAnimInfo(int pairCount) =>
        new(
            WalkFrameIndex: 0,
            WalkFrameCount: 1,
            AttackFrameIndex: 0,
            AttackFrameCount: 1,
            Attack2StartIndex: 0,
            Attack2FrameCount: 0,
            Attack3StartIndex: 0,
            Attack3FrameCount: 0,
            StandingFrameIndex: 0,
            StandingFrameCount: 1,
            OptionalAnimationFrameCount: 0,
            OptionalAnimationProbability: 0,
            AnimationIntervalMs: 100,
            TotalFrameCount: pairCount);

    /// <summary>
    ///     Returns the rendered sprite frame for a creature at the given frame index. Returns null if the sprite or frame
    ///     cannot be loaded. Probes the modern <c>creature_sprites</c> pack first; on miss falls back to legacy MPF.
    /// </summary>
    public SpriteFrame? GetFrame(int spriteId, int frameIndex)
    {
        var key = (spriteId, frameIndex);

        if (FrameCache.TryGetValue(key, out var cached))
            return cached;

        var pack = AssetPackRegistry.GetCreaturePack();

        if (pack is not null && pack.Covers(spriteId))
        {
            var packed = LoadPackFrame(pack, spriteId, frameIndex);

            if (packed is not null)
                FrameCache[key] = packed.Value;

            return packed;
        }


        var palettized = DataContext.CreatureSprites.GetCreatureSprite(spriteId);

        if (palettized is null)
            return null;

        var mpf = palettized.Entity;

        if ((frameIndex < 0) || (frameIndex >= mpf.Count))
            return null;

        var mpfFrame = mpf[frameIndex];

        using var image = Graphics.RenderImage(mpfFrame, palettized.Palette);

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (image is null)
            return null;

        var texture = TextureConverter.ToTexture2D(image);

        var spriteFrame = new SpriteFrame(
            texture,
            mpfFrame.CenterX,
            mpfFrame.CenterY,
            mpfFrame.Left,
            mpfFrame.Top);

        FrameCache[key] = spriteFrame;

        return spriteFrame;
    }

    /// <summary>
    ///     Loads a frame from the modern <c>creature_sprites</c> pack. <paramref name="frameIndex" /> is interpreted
    ///     as the pair-index (0 = first authored pair, 1 = second authored pair); higher indices return null.
    ///     <para>
    ///     Auto-trims pack PNGs at load by computing the per-creature union bbox — the tightest rectangle that
    ///     contains every visible pixel across <em>all</em> of this creature's pair-master frames. Every frame is
    ///     cropped to that same rectangle, so the bottom-center anchor stays stable across frames even when
    ///     individual frames' silhouettes differ in size (e.g., an attack frame wider than the idle frame in a
    ///     future multi-frame pack). The union bbox is cached per spriteId and cleared on map change.
    ///     </para>
    /// </summary>
    private SpriteFrame? LoadPackFrame(CreaturePack pack, int spriteId, int frameIndex)
    {
        var pairCount = pack.GetPairCount(spriteId);

        if ((frameIndex < 0) || (frameIndex >= pairCount))
            return null;

        if (!PackCreatureUnionBboxCache.TryGetValue(spriteId, out var unionBbox))
        {
            unionBbox = ComputePackUnionBbox(pack, spriteId, pairCount);

            if (unionBbox.IsEmpty)
                return null;

            PackCreatureUnionBboxCache[spriteId] = unionBbox;
        }

        if (!pack.TryGetFrame(spriteId, frameIndex, out var image) || image is null)
            return null;

        using (image)
        {
            using var cropped = image.Subset(unionBbox);

            if (cropped is null)
                return null;

            var texture = TextureConverter.ToTexture2D(cropped);

            return new SpriteFrame(
                texture,
                CenterX: (short)(texture.Width / 2),
                CenterY: (short)texture.Height,
                Left: 0,
                Top: 0);
        }
    }

    /// <summary>
    ///     Computes the union of all pair-master frame bboxes for a pack-served creature. Returns an empty rect if
    ///     no frame contains any visible pixels (a degenerate, fully-transparent pack entry).
    /// </summary>
    private static SKRectI ComputePackUnionBbox(CreaturePack pack, int spriteId, int pairCount)
    {
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;

        for (var i = 0; i < pairCount; i++)
        {
            if (!pack.TryGetFrame(spriteId, i, out var image) || image is null)
                continue;

            using (image)
            {
                var bbox = FindAlphaBoundingBox(image);

                if (bbox.IsEmpty)
                    continue;

                if (bbox.Left < minX)
                    minX = bbox.Left;

                if (bbox.Top < minY)
                    minY = bbox.Top;

                if (bbox.Right > maxX)
                    maxX = bbox.Right;

                if (bbox.Bottom > maxY)
                    maxY = bbox.Bottom;
            }
        }

        if (maxX <= minX)
            return SKRectI.Empty;

        return new SKRectI(minX, minY, maxX, maxY);
    }

    /// <summary>
    ///     Scans a decoded <see cref="SKImage" /> for the tightest rectangle containing every pixel with non-zero
    ///     alpha. Used by the per-creature union-bbox computation; the result is in source-image coordinates.
    /// </summary>
    private static SKRectI FindAlphaBoundingBox(SKImage image)
    {
        using var bitmap = SKBitmap.FromImage(image);
        var width = bitmap.Width;
        var height = bitmap.Height;
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            if (bitmap.GetPixel(x, y).Alpha > 0)
            {
                if (x < minX)
                    minX = x;

                if (x > maxX)
                    maxX = x;

                if (y < minY)
                    minY = y;

                if (y > maxY)
                    maxY = y;
            }

        if (maxX < 0)
            return SKRectI.Empty;

        return new SKRectI(minX, minY, maxX + 1, maxY + 1);
    }

    

    

    

    /// <summary>
    ///     Returns the walk frame count for a creature sprite, or null if the sprite cannot be loaded.
    /// </summary>
    public int? GetWalkFrameCount(int spriteId)
    {
        var info = GetAnimInfo(spriteId);

        return info?.WalkFrameCount;
    }
}

/// <summary>
///     Creature animation metadata describing frame index ranges, counts, and idle timing for every animation state
///     (walk, attack, standing).
/// </summary>
public readonly record struct CreatureAnimInfo(
    byte WalkFrameIndex,
    byte WalkFrameCount,
    byte AttackFrameIndex,
    byte AttackFrameCount,
    byte Attack2StartIndex,
    byte Attack2FrameCount,
    byte Attack3StartIndex,
    byte Attack3FrameCount,
    byte StandingFrameIndex,
    byte StandingFrameCount,
    byte OptionalAnimationFrameCount,
    byte OptionalAnimationProbability,
    int AnimationIntervalMs,
    int TotalFrameCount)
{
    /// <summary>
    ///     The <see cref="MpfIdleType" /> that governs this creature's idle animation behavior.
    /// </summary>
    public MpfIdleType IdleType => MpfFile.DetectIdleType(StandingFrameCount, OptionalAnimationFrameCount);
}