#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     An icon texture paired with the pixel offset at which it should be drawn relative to its slot position. Legacy
///     sheet-sourced icons (from .epf files) are 31x31 and draw at the slot corner with no offset. Modern pack-sourced
///     icons (from .datf PNG files) are 32x32 and draw shifted 1px up and 1px left so their overrun lands on the slot's
///     outer border padding rather than bleeding into adjacent slots.
/// </summary>
public readonly record struct IconTexture(Texture2D Texture, int OffsetX, int OffsetY)
{
    /// <summary>
    ///     Wraps a legacy-sourced (EPF) texture with zero offset.
    /// </summary>
    public static IconTexture Legacy(Texture2D texture) => new(texture, 0, 0);

    /// <summary>
    ///     Wraps a modern-sourced (PNG, 32x32) texture with the -1/-1 offset required to align into a legacy 31x31 slot.
    /// </summary>
    public static IconTexture Modern(Texture2D texture) => new(texture, -1, -1);

    /// <summary>
    ///     Draws the icon at the given slot position, applying the baked offset. Optional tint multiplies the base
    ///     texture color.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, Vector2 slotPosition, Color? tint = null)
        => spriteBatch.Draw(
            Texture,
            slotPosition + new Vector2(OffsetX, OffsetY),
            tint ?? Color.White);

    /// <summary>
    ///     Draws the icon at an integer position, applying the baked offset.
    /// </summary>
    public void Draw(SpriteBatch spriteBatch, int x, int y, Color? tint = null)
        => Draw(spriteBatch, new Vector2(x, y), tint);
}
