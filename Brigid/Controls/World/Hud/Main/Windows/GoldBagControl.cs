#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Models;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     The gold bag of the Inventory window (BRIG-150): the <see cref="ThemeRoles.IconInventoryGoldBag" /> icon. A press
///     and a move start a drag with a <see cref="GoldDragPayload" />, which drops or gives gold as the classic gold bag
///     does. A click does nothing and does not reach the world under the window.
/// </summary>
internal sealed class GoldBagControl : UIPanel
{
    private readonly RoleIcon Icon = new() { Name = "icon", Role = ThemeRoles.IconInventoryGoldBag };

    public GoldBagControl()
    {
        Name = "gold_bag";
        AddChild(Icon);
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        Icon.SetBounds(new Rectangle(0, 0, bounds.Width, bounds.Height));
    }

    public override void OnClick(ClickEvent e) => e.Handled = true;

    public override void OnDragStart(DragStartEvent e)
    {
        if (e.Button == MouseButton.Left)
            e.Payload = new GoldDragPayload();
    }
}
