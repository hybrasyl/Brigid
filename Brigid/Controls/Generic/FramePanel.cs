#region
using Brigid.Controls.Components;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A primitive framed box: the <see cref="DialogPalette" /> frame fill and border, or the theme's
///     <see cref="ThemeRoles.FramePanel" /> art when the panel is themed. Content goes in with <c>AddChild</c>.
///     <see cref="Components.CenteredModalPanel" />, the menu alerts and the Story view are framed with it.
/// </summary>
public class FramePanel : UIPanel
{
    public FramePanel()
    {
        BackgroundColor = DialogPalette.FrameFill;
        BorderColor = DialogPalette.FrameBorder;
    }

    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (TryDrawThemePart(spriteBatch, ThemeRoles.FramePanel, bounds))
            return;

        base.DrawChrome(spriteBatch, bounds);
    }
}
