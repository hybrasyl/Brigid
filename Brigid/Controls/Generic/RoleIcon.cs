#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     An icon that draws the new art of its <c>icon.*</c> role (<see cref="IconArt" />: the <c>ui_theme</c> part, else
///     Brigid's own image) in its bounds, on every host, themed or not. With no art for the role it draws nothing. It does
///     not take the mouse; its host does.
/// </summary>
public sealed class RoleIcon : UIElement
{
    public RoleIcon() => IsHitTestVisible = false;

    /// <summary>The icon role to draw, or null for none.</summary>
    public string? Role { get; set; }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        if ((ClipRect.Width <= 0) || (ClipRect.Height <= 0) || Role is null)
            return;

        IconArt.TryDraw(spriteBatch, Role, new Rectangle(ScreenX, ScreenY, Width, Height), ClipRect);
    }
}
