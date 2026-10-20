#region
using Brigid.Controls.Generic;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     The tooltip of an inventory item (BRIG-125): the item name, and the durability when the item has one. A
///     <see cref="TooltipFrame" />, so it takes the theme's <c>frame.popup</c> part. The name is in the theme's text
///     colour and wraps at 25 characters; the durability stays blue, because the colour carries meaning. It follows the
///     cursor while it is visible.
/// </summary>
public sealed class ItemTooltipControl : TooltipFrame
{
    private const int MAX_TEXT_WIDTH = 25 * TextRenderer.CHAR_WIDTH;

    private static readonly Color DurabilityColor = new(100, 149, 237);

    public ItemTooltipControl()
        : base(MAX_TEXT_WIDTH) => Name = "itemtooltip";

    public void Hide() => Visible = false;

    /// <summary>Puts the tooltip at the cursor again, in the viewport.</summary>
    public void UpdatePosition(int mouseX, int mouseY) => PlaceAtCursor(mouseX, mouseY);

    public void Show(
        string itemName,
        int currentDurability,
        int maxDurability,
        int mouseX,
        int mouseY)
    {
        SetLines(
            maxDurability > 0
                ? [TooltipLine.Themed(itemName, ThemeRoles.Text), TooltipLine.Literal($"{currentDurability}/{maxDurability}", DurabilityColor)]
                : [TooltipLine.Themed(itemName, ThemeRoles.Text)]);

        PlaceAtCursor(mouseX, mouseY);
        Visible = true;
    }
}
