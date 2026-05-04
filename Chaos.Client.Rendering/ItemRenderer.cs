#region
using Chaos.Client.Data;
using Chaos.Client.Rendering.Utility;
using DALib.Drawing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Rendering;

/// <summary>
///     Renders and caches item sprites for ground display. Separate from UiRenderer's permanent icon cache — these
///     textures are evicted on map change. Stores frame offset metadata (Left/Top) for proper visual centering.
///     Cache key includes both sprite ID and dye color.
/// </summary>
public sealed class ItemRenderer : IDisposable
{
    private readonly Dictionary<(int SpriteId, byte Color), ItemSprite?> SpriteCache = [];

    //keyed by (source sprite texture, paint height, packed argb) — gndattr water-tile tint applied to the lower portion of the sprite. Cleared on map change.
    private readonly Dictionary<(Texture2D Source, int PaintHeight, uint PackedColor), Texture2D> GroundTintCache = [];

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
    ///     Returns the ground item sprite for the given sprite ID and dye color. Applies palette dye if color is non-zero.
    /// </summary>
    public ItemSprite? GetSprite(int spriteId, byte color = 0)
    {
        var key = (spriteId, color);

        if (SpriteCache.TryGetValue(key, out var cached))
            return cached;

        var sprite = LoadSprite(spriteId, color);
        SpriteCache[key] = sprite;

        return sprite;
    }

    private static ItemSprite? LoadSprite(int spriteId, byte color)
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