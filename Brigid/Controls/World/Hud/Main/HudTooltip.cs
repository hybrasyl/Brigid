#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     The one tooltip of the new HUD: under a box of the top bar, or over a control of the bottom area. It builds its lines
///     again at each update while it shows, so a value that changes under the mouse (the latency, the clock) stays current,
///     and it places itself again, so a new face or window size keeps it on the screen.
/// </summary>
internal sealed class HudTooltip : TooltipFrame
{
    private UIElement? Anchor;
    private bool Below;
    private Func<IReadOnlyList<TooltipLine>>? Source;
    private IReadOnlyList<TooltipLine> Shown = [];

    public HudTooltip()
        : base(MainHudLayout.TOOLTIP_TEXT_WIDTH)
    {
        Name = "hud_tooltip";
        ZIndex = 1;
    }

    /// <summary>Shows the lines of <paramref name="lines" /> under <paramref name="anchor" />.</summary>
    public void ShowBelow(UIElement anchor, Func<IReadOnlyList<TooltipLine>> lines) => Show(anchor, lines, true);

    /// <summary>Shows the lines of <paramref name="lines" /> over <paramref name="anchor" />.</summary>
    public void ShowAbove(UIElement anchor, Func<IReadOnlyList<TooltipLine>> lines) => Show(anchor, lines, false);

    /// <summary>True while the host draws the tooltip itself, after the focused chat input.</summary>
    internal bool HoldDraw { get; set; }

    public override void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
    {
        if (!HoldDraw)
            base.Draw(spriteBatch);
    }

    public void Hide()
    {
        Visible = false;
        Anchor = null;
        Source = null;
    }

    public override void Update(GameTime gameTime)
    {
        if (Visible && Source is not null)
            Refresh();

        base.Update(gameTime);
    }

    private void Show(UIElement anchor, Func<IReadOnlyList<TooltipLine>> lines, bool below)
    {
        (Anchor, Source, Below) = (anchor, lines, below);
        Shown = [];
        Refresh();
        Visible = true;
    }

    //a change of the lines fits the frame again; the place follows the frame and the anchor
    private void Refresh()
    {
        var lines = Source!();

        if (!lines.SequenceEqual(Shown))
        {
            Shown = lines;
            SetLines(lines);
        }

        var anchor = Anchor!.ScreenBounds;

        (X, Y) = Below
            ? TooltipPlacement.Below(anchor, Width, Height, MainHudLayout.TOOLTIP_GAP)
            : TooltipPlacement.Above(anchor, Width, Height, MainHudLayout.TOOLTIP_GAP);
    }
}
