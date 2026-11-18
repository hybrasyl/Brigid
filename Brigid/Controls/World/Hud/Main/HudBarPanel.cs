#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     A surface of the main HUD: the top bar, the bottom area or a strip beside the world. It draws the theme part of its
///     role, else the flat frame fill and border, so the flat borders of the four surfaces make one frame around the world.
///     It takes the mouse, so a click on a surface does not reach the world under it.
/// </summary>
internal class HudBarPanel : UIPanel
{
    /// <summary>The theme part that the surface draws.</summary>
    internal string Role { get; }

    public HudBarPanel(string role, Rectangle bounds)
    {
        Role = role;
        SetBounds(bounds);
        BackgroundColor = DialogPalette.FrameFill;
        BorderColor = DialogPalette.FrameBorder;
    }

    //the flat border is part of the look
    protected internal override Color? DrawnBorderColor => BorderColor is null ? null : ThemeColor(ThemeRoles.Border);

    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (TryDrawThemePart(spriteBatch, Role, bounds))
            return;

        base.DrawChrome(spriteBatch, bounds);
    }
}
