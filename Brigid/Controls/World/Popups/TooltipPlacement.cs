namespace Brigid.Controls.World.Popups;

/// <summary>
///     Shared cursor-anchored placement for hover tooltips: sits 15px down-right of the cursor, flips to the cursor's
///     left when the box would overflow the right edge, and clamps vertically to the virtual viewport. One home for the
///     rule so <see cref="ItemTooltipControl" /> and the from-scratch bank tooltip can't drift apart.
/// </summary>
internal static class TooltipPlacement
{
    public static (int X, int Y) CursorAnchored(int mouseX, int mouseY, int width, int height)
    {
        var rightX = mouseX + 15;
        var x = (rightX + width) <= ChaosGame.VIRTUAL_WIDTH ? rightX : mouseX - width;
        var y = Math.Clamp(mouseY + 15, 0, ChaosGame.VIRTUAL_HEIGHT - height);

        return (x, y);
    }
}
