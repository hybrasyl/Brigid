#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Models;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     A slot of the Inventory window (BRIG-150): the <see cref="ThemeRoles.HudInventorySlot" /> part, the icon of the item,
///     and the count of a stack of more than one at the bottom right (<see cref="HudItemSlotControl" /> draws the rest). On
///     an item a double click raises <see cref="UseRequested" />, as on the classic panel, and a drag carries an
///     <see cref="ItemDragPayload" />; a drop of an item from another slot raises <see cref="SwapRequested" />. A slot past
///     the inventory size of today is dimmed and takes no item and no drop. A drop of another payload goes on to the window.
/// </summary>
internal sealed class InventorySlotControl : HudItemSlotControl
{
    /// <param name="slot">The inventory slot, from 1.</param>
    /// <param name="usable">False for a slot past the inventory size of today.</param>
    public InventorySlotControl(byte slot, bool usable)
        : base(usable, showsCount: true)
    {
        Slot = slot;
        Name = $"slot_{slot}";
    }

    /// <summary>The inventory slot, from 1.</summary>
    public byte Slot { get; }

    protected override string SlotRole => ThemeRoles.HudInventorySlot;

    /// <summary>A double click on an item: use it.</summary>
    public event Action<byte>? UseRequested;

    /// <summary>An item of the first slot dropped on the second slot.</summary>
    public event Action<byte, byte>? SwapRequested;

    /// <summary>The pointer came on the slot (true) or went off it (false).</summary>
    public event Action<byte, bool>? PointerChanged;

    /// <summary>Shows the item <paramref name="icon" />, or an empty slot for null, with the count of a stack.</summary>
    /// <param name="count">The count to show; a count of 1 or less does not show.</param>
    public void Show(IconTexture? icon, uint count)
    {
        SetIcon(icon);
        ShowCount(Icon is not null ? count : 0);
    }

    public override void OnDoubleClick(DoubleClickEvent e)
    {
        if ((e.Button != MouseButton.Left) || Icon is null)
            return;

        UseRequested?.Invoke(Slot);
        e.Handled = true;
    }

    protected override bool TakesDrop(object? payload) => payload is ItemDragPayload;

    protected override object DragPayloadOf(IconTexture icon) => new ItemDragPayload(Slot, icon.Texture);

    //a drop of an item on the window ends here, so it does not go to the ground under the window
    protected override void HandleDrop(DragDropEvent e)
    {
        if (e.Payload is not ItemDragPayload item)
            return;

        if (Usable && (item.Slot != Slot))
            SwapRequested?.Invoke(item.Slot, Slot);

        e.Handled = true;
    }

    protected override void OnPointer(bool entered) => PointerChanged?.Invoke(Slot, entered);
}
