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
    //the space between the border art and the content
    private const int FRAME_GAP = 3;

    //the flat frame is a 1 px border
    private const int FLAT_BORDER = 1;

    /// <summary>
    ///     The width of the frame border: 1 px for the flat frame, or the slice of the <see cref="ThemeRoles.FramePanel" />
    ///     art when the panel is themed. The icy-steel frame, for example, has an 11 px border with its corner rivets.
    /// </summary>
    public int FrameBorder => TryGetThemeBorder(ThemeRoles.FramePanel, out var border) ? Math.Max(FLAT_BORDER, border) : FLAT_BORDER;

    /// <summary>
    ///     The distance from the panel edge to its content: <paramref name="padding" /> for the flat frame, or more when the
    ///     themed border art is wider, so the content keeps clear of it.
    /// </summary>
    public int InsetFor(int padding) => ComputeInset(padding, FrameBorder);

    /// <summary>The inset rule: the padding, or the border width and a small gap when the border is wider.</summary>
    internal static int ComputeInset(int padding, int border) => Math.Max(padding, border + FRAME_GAP);

    public FramePanel()
    {
        BackgroundColor = DialogPalette.FrameFill;
        BorderColor = DialogPalette.FrameBorder;
    }

    //the flat border is part of the look
    protected internal override Color? DrawnBorderColor => BorderColor is { } border ? ThemeColor(ThemeRoles.Border, border) : null;

    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (TryDrawThemePart(spriteBatch, ThemeRoles.FramePanel, bounds))
            return;

        base.DrawChrome(spriteBatch, bounds);
    }
}
