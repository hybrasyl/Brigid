#region
using Brigid.Data;
using Brigid.Data.AssetPacks;
using Brigid.Rendering.Utility;
using DALib.Drawing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Renders and caches item sprites for ground display. Separate from UiRenderer's permanent icon cache — these
///     textures are evicted on map change. Stores frame offset metadata (Left/Top) for proper visual centering.
///     Cache key includes both sprite ID and dye color.
/// </summary>
public sealed class ItemRenderer : IDisposable
{
    //square footprint of the missing-item placeholder, in pixels — roughly a tile-sized checkerboard centered on the
    //ground tile so a referenced-but-absent item sprite is a visible marker instead of an invisible pickup.
    private const int MISSING_ITEM_SIZE = 28;

    private readonly Dictionary<(int SpriteId, byte Color), ItemSprite?> SpriteCache = [];

    //keyed by (source sprite texture, paint height, packed argb) — gndattr water-tile tint applied to the lower portion of the sprite. Cleared on map change.
    private readonly Dictionary<(Texture2D Source, int PaintHeight, uint PackedColor), Texture2D> GroundTintCache = [];

    //shared checkerboard sprite served for any item present in neither the pack nor legacy EPF. Not stored in
    //SpriteCache (which disposes every value on map change), so it can never be double-freed; rebuilt lazily after Clear.
    private ItemSprite? MissingItemSprite;

    public void Dispose() => Clear();

    /// <summary>
    ///     Disposes all cached textures and clears the cache. Call on map change.
    /// </summary>
    public void Clear()
    {
        foreach (var sprite in SpriteCache.Values)
            sprite?.Texture.Dispose();

        SpriteCache.Clear();
        GroundTintCache.DisposeAndClear();
        MissingItemSprite?.Texture.Dispose();
        MissingItemSprite = null;
    }

    /// <summary>
    ///     Lazily builds (and caches) the checkerboard placeholder served when an item sprite is present in neither the
    ///     <c>item_icons</c> pack nor the legacy EPF archive. Tightly cropped (no frame padding) so it centers on the tile.
    /// </summary>
    private ItemSprite GetMissingItemSprite()
    {
        if (MissingItemSprite is not null)
            return MissingItemSprite.Value;

        var texture = ImageUtil.BuildMissingPlaceholder(TextureConverter.Device, MISSING_ITEM_SIZE, MISSING_ITEM_SIZE);
        var sprite = new ItemSprite(texture, 0, 0);
        MissingItemSprite = sprite;

        return sprite;
    }

    /// <summary>
    ///     Draws a ground item sprite centered on the tile, using visual content bounds to ignore transparent padding.
    /// </summary>
    public void Draw(
        SpriteBatch batch,
        Camera camera,
        int spriteId,
        byte color,
        float tileCenterX,
        float tileCenterY,
        int groundPaintHeight,
        Color groundTintColor)
    {
        var sprite = GetSprite(spriteId, color);

        if (sprite is null)
            return;

        var texture = sprite.Value.Texture;

        var contentWidth = texture.Width - sprite.Value.FrameLeft;
        var contentHeight = texture.Height - sprite.Value.FrameTop;

        //integer division: odd content dimensions (e.g. gold piles 23x15, 30x17) would otherwise produce
        //fractional draw positions; with PointClamp + premultiplied alpha that drops the boundary row at the
        //rasterization edge.
        var contentCenterX = sprite.Value.FrameLeft + contentWidth / 2;
        var contentCenterY = sprite.Value.FrameTop + contentHeight / 2;
        var drawX = tileCenterX - contentCenterX;
        var drawY = tileCenterY - contentCenterY;
        var screenPos = camera.WorldToScreen(new Vector2(drawX, drawY));

        //items sit at the tile with the whole sprite visually "on the ground" — anchor the tint at the texture bottom so
        //the lower `paintHeight` pixels are submerged.
        var drawTexture = texture;

        if (groundPaintHeight > 0)
        {
            var key = (Source: texture, PaintHeight: groundPaintHeight, PackedColor: groundTintColor.PackedValue);

            if (!GroundTintCache.TryGetValue(key, out var groundTinted))
            {
                groundTinted = ImageUtil.BuildGroundTinted(
                    TextureConverter.Device,
                    texture,
                    groundPaintHeight,
                    texture.Height,
                    groundTintColor);
                GroundTintCache[key] = groundTinted;
            }

            drawTexture = groundTinted;
        }

        batch.Draw(drawTexture, screenPos, Color.White);
    }

    /// <summary>
    ///     Returns the ground item sprite for the given sprite ID and dye color. If an <c>item_icons</c> asset pack covers
    ///     the ID, renders from the pack PNG (running the find-and-replace dye pass for dyeable IDs); otherwise renders
    ///     from the legacy EPF + palette pipeline. Non-dyeable pack items collapse to <c>color = 0</c> in the cache so a
    ///     single decode is shared across every server-sent color byte for that ID.
    /// </summary>
    public ItemSprite? GetSprite(int spriteId, byte color = 0)
    {
        var pack = AssetPackRegistry.GetItemPack();
        var packCovers = (pack is not null) && pack.HasItem(spriteId);

        if (packCovers && !pack!.IsDyeable(spriteId))
            color = 0;

        var key = (spriteId, color);

        if (!SpriteCache.TryGetValue(key, out var sprite))
        {
            sprite = packCovers
                ? LoadSpriteFromPack(pack!, spriteId, color)
                : LoadSpriteFromLegacy(spriteId, color);

            //memoize the load, misses included — a cached null means "absent from both sources", so the expensive
            //legacy lookup runs at most once per (id, color) rather than every frame the missing item is on screen.
            SpriteCache[key] = sprite;
        }

        //terminal miss: serve the shared placeholder (never itself cached, so it can't be double-freed) so a
        //referenced-but-absent item id is a visible marker rather than an invisible ground pickup. spriteId 0 stays
        //null — it is the "no sprite" sentinel, not a missing asset.
        if (sprite is null && spriteId > 0)
            return GetMissingItemSprite();

        return sprite;
    }

    private static ItemSprite? LoadSpriteFromPack(ItemPack pack, int spriteId, byte color)
    {
        if (!pack.TryGetItemImage(spriteId, out var sourceImage) || (sourceImage is null))
            return null;

        using var owned = sourceImage;

        if ((color > 0) && DataContext.AislingDrawData.DyeColorTable.Contains(color))
        {
            using var dyed = ItemDyePass.ApplyDyeFindReplace(owned, DataContext.AislingDrawData.DyeColorTable[color]);
            var dyedTexture = TextureConverter.ToTexture2D(dyed);

            //modern PNGs are tightly cropped — no legacy frame Left/Top padding to compensate for
            return new ItemSprite(dyedTexture, 0, 0);
        }

        var texture = TextureConverter.ToTexture2D(owned);

        return new ItemSprite(texture, 0, 0);
    }

    private static ItemSprite? LoadSpriteFromLegacy(int spriteId, byte color)
    {
        var palettized = DataContext.PanelSprites.GetItemSprite(spriteId);

        if (palettized is null)
            return null;

        var palette = palettized.Palette;

        //apply dye color if specified
        if ((color > 0) && DataContext.AislingDrawData.DyeColorTable.Contains(color))
            palette = palette.Dye(DataContext.AislingDrawData.DyeColorTable[color]);

        using var image = Graphics.RenderImage(palettized.Entity, palette);

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (image is null)
            return null;

        var texture = TextureConverter.ToTexture2D(image);

        return new ItemSprite(texture, palettized.Entity.Left, palettized.Entity.Top);
    }

    /// <summary>
    ///     A ground item sprite texture with its EPF frame's Left/Top transparent padding offsets, used to compute visual
    ///     content bounds for tile-centered drawing.
    /// </summary>
    public readonly record struct ItemSprite(Texture2D Texture, int FrameLeft, int FrameTop);
}