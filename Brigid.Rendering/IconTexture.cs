#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     An icon texture paired with the pixel offset at which it should be drawn relative to its slot position. Legacy
///     sheet-sourced icons (from .epf files) are 31x31 and draw at the slot corner with no offset. A pack-sourced icon
///     (a .datf PNG) is centred in that 31x31 footprint (see <see cref="SlotOffset" />): a 32x32 icon draws shifted 1px
///     up and 1px left so its overrun lands on the slot's outer border padding rather than bleeding into adjacent slots.
/// </summary>
public readonly record struct IconTexture(Texture2D Texture, int OffsetX, int OffsetY)
{
    /// <summary>The width and height of a legacy icon, and of the slot footprint that it fills.</summary>
    public const int LEGACY_SLOT_SIZE = 31;

    /// <summary>The offset as a vector, for a control's <c>TextureOffset</c>.</summary>
    public Vector2 Offset => new(OffsetX, OffsetY);

    /// <summary>
    ///     The draw offset, on one axis, that centres an icon of <paramref name="size" /> pixels in the legacy 31-pixel
    ///     footprint. A 32-pixel icon gets -1; a smaller icon moves in.
    /// </summary>
    public static int SlotOffset(int size) => (int)Math.Floor((LEGACY_SLOT_SIZE - size) / 2.0);

    /// <summary>Wraps a pack-sourced (PNG) texture of any size with the offset that centres it in the slot.</summary>
    public static IconTexture FromPack(Texture2D texture) => new(texture, SlotOffset(texture.Width), SlotOffset(texture.Height));

    /// <summary>
    ///     Wraps a legacy-sourced (EPF) texture with zero offset.
    /// </summary>
    public static IconTexture Legacy(Texture2D texture) => new(texture, 0, 0);

    /// <summary>
    ///     Wraps a modern-sourced (PNG, 32x32) ability icon with the -1/-1 offset required to align into a legacy 31x31
    ///     slot. This is <see cref="FromPack" /> for a 32x32 icon; item icons use <see cref="FromPack" />.
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
