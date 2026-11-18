#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     The character model of the Equipment window (BRIG-151): the player's figure as the paperdolls show it, scaled up
///     <see cref="SCALE" /> times with the point sampling of the UI pass, at the centre of the control and clipped to it
///     (Sabrael, 2026-11-18, Q5). The control owns the texture: a new one disposes the one before it.
/// </summary>
internal sealed class CharacterModelControl : UIElement
{
    public const int SCALE = 2;

    public CharacterModelControl()
    {
        Name = "model";
        IsHitTestVisible = false;
    }

    /// <summary>The figure, or null while no appearance gave one.</summary>
    public Texture2D? Texture { get; private set; }

    /// <summary>Shows <paramref name="texture" />, and disposes the texture before it.</summary>
    public void SetTexture(Texture2D? texture)
    {
        if (ReferenceEquals(texture, Texture))
            return;

        Texture?.Dispose();
        Texture = texture;
    }

    /// <summary>
    ///     The scaled figure of <paramref name="size" /> at the centre of <paramref name="area" />, clipped to
    ///     <paramref name="clip" />: the part of the texture that shows and where it goes. Null when nothing shows.
    /// </summary>
    internal static (Rectangle Source, Rectangle Destination)? Placement(Point size, Rectangle area, Rectangle clip)
    {
        var width = size.X * SCALE;
        var height = size.Y * SCALE;
        var full = new Rectangle(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
        var shown = Rectangle.Intersect(Rectangle.Intersect(full, area), clip);

        if (shown.Width <= 0 || shown.Height <= 0)
            return null;

        //whole texture px only, so the point sampling keeps each px square
        var left = (shown.Left - full.Left + SCALE - 1) / SCALE;
        var top = (shown.Top - full.Top + SCALE - 1) / SCALE;
        var right = (shown.Right - full.Left) / SCALE;
        var bottom = (shown.Bottom - full.Top) / SCALE;

        if (right <= left || bottom <= top)
            return null;

        var source = new Rectangle(left, top, right - left, bottom - top);
        var destination = new Rectangle(full.X + left * SCALE, full.Y + top * SCALE, source.Width * SCALE, source.Height * SCALE);

        return (source, destination);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        if (Texture is null || Placement(new Point(Texture.Width, Texture.Height), ScreenBounds, ClipRect) is not { } placement)
            return;

        spriteBatch.Draw(Texture, placement.Destination, placement.Source, Color.White);
    }

    public override void Dispose()
    {
        Texture?.Dispose();
        Texture = null;

        base.Dispose();
    }
}
