#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Static draw helper that transparently handles atlas-backed textures. If the texture is a CachedTexture2D with an
///     AtlasRegion set, draws from the atlas using the region's source rectangle. Otherwise falls back to normal draw.
/// </summary>
public static class AtlasHelper
{
    public static void Draw(
        SpriteBatch spriteBatch,
        Texture2D? texture,
        Vector2 position,
        Color color)
    {
        if (texture is null)
            return;

        if (texture is CachedTexture2D { AtlasRegion: { } region })
            spriteBatch.Draw(
                region.Atlas,
                position,
                region.SourceRect,
                color);
        else
            spriteBatch.Draw(texture, position, color);
    }

    public static void Draw(
        SpriteBatch spriteBatch,
        Texture2D? texture,
        Vector2 position,
        Rectangle? sourceRect,
        Color color)
    {
        if (texture is null)
            return;

        if (texture is CachedTexture2D { AtlasRegion: { } region })
        {
            var finalRect = sourceRect.HasValue
                ? new Rectangle(
                    region.SourceRect.X + sourceRect.Value.X,
                    region.SourceRect.Y + sourceRect.Value.Y,
                    sourceRect.Value.Width,
                    sourceRect.Value.Height)
                : region.SourceRect;

            spriteBatch.Draw(
                region.Atlas,
                position,
                finalRect,
                color);
        } else
            spriteBatch.Draw(
                texture,
                position,
                sourceRect,
                color);
    }

    public static void Draw(
        SpriteBatch spriteBatch,
        Texture2D? texture,
        Vector2 position,
        Rectangle? sourceRect,
        Color color,
        float rotation,
        Vector2 origin,
        float scale,
        SpriteEffects effects,
        float layerDepth)
    {
        if (texture is null)
            return;

        if (texture is CachedTexture2D { AtlasRegion: { } region })
        {
            var finalRect = sourceRect.HasValue
                ? new Rectangle(
                    region.SourceRect.X + sourceRect.Value.X,
                    region.SourceRect.Y + sourceRect.Value.Y,
                    sourceRect.Value.Width,
                    sourceRect.Value.Height)
                : region.SourceRect;

            spriteBatch.Draw(
                region.Atlas,
                position,
                finalRect,
                color,
                rotation,
                origin,
                scale,
                effects,
                layerDepth);
        } else
            spriteBatch.Draw(
                texture,
                position,
                sourceRect,
                color,
                rotation,
                origin,
                scale,
                effects,
                layerDepth);
    }
}