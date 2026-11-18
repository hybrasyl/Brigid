#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Models;
using Brigid.Rendering;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     A slot of the Equipment window (BRIG-151): the <see cref="ThemeRoles.HudEquipmentSlot" /> part (else the inventory
///     slot part), the icon of the item, or the outline of an empty slot (<see cref="EquipmentSlotSpec.IconRole" />; the
///     built-in outline is dim, a theme's outline shows as the theme draws it). A slot that the wire does not have yet is
///     dimmed and takes nothing. A double click on an item raises <see cref="UnequipRequested" />, and a drag carries an
///     <see cref="EquipmentDragPayload" />; a single click does nothing (Sabrael, 2026-11-18, Q4). A drop of an item from
///     the Inventory window raises <see cref="EquipRequested" />: the server chooses the slot, as for a use of the item.
/// </summary>
internal sealed class EquipmentSlotControl : HudItemSlotControl
{
    private const float OUTLINE = 0.35f;

    public EquipmentSlotControl(EquipmentSlotSpec spec)
        : base(spec.Wire is not null)
    {
        Spec = spec;
        Name = $"slot_{spec.Id}";
    }

    public EquipmentSlotSpec Spec { get; }

    protected override string SlotRole => ThemeRoles.HudEquipmentSlot;

    protected override string FallbackSlotRole => ThemeRoles.HudInventorySlot;

    /// <summary>A double click on an item: take it off.</summary>
    public event Action<EquipmentSlot>? UnequipRequested;

    /// <summary>An item of the inventory slot dropped on this slot: use it, which equips it.</summary>
    public event Action<byte>? EquipRequested;

    /// <summary>The pointer came on the slot (true) or went off it (false).</summary>
    public event Action<EquipmentSlotControl, bool>? PointerChanged;

    /// <summary>Shows the item <paramref name="icon" />, or an empty slot for null.</summary>
    public void Show(IconTexture? icon) => SetIcon(icon);

    public override void OnDoubleClick(DoubleClickEvent e)
    {
        if ((e.Button != MouseButton.Left) || Icon is null || Spec.Wire is not { } wire)
            return;

        UnequipRequested?.Invoke(wire);
        e.Handled = true;
    }

    protected override bool TakesDrop(object? payload) => payload is ItemDragPayload;

    protected override object DragPayloadOf(IconTexture icon) => new EquipmentDragPayload(Spec.Wire!.Value, icon.Texture);

    //a drop of an item on the window ends here, so it does not go to the ground under the window
    protected override void HandleDrop(DragDropEvent e)
    {
        if (e.Payload is ItemDragPayload item)
        {
            if (Usable)
                EquipRequested?.Invoke(item.Slot);

            e.Handled = true;
        } else if (e.Payload is EquipmentDragPayload)
            e.Handled = true;
    }

    protected override void OnPointer(bool entered) => PointerChanged?.Invoke(this, entered);

    protected override void DrawEmpty(SpriteBatch spriteBatch, Rectangle bounds)
        => IconArt.TryDraw(spriteBatch, Spec.IconRole, bounds, ClipRect, builtInTint: Color.White * OUTLINE);
}
