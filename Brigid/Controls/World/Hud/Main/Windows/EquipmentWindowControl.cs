#region
using Brigid.Rendering;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     The content of the Equipment window of the new HUD (BRIG-151, BRIG-127 D31): the slots around the character model
///     (<see cref="EquipmentSlots" />), and Personal View at the right. It reads <see cref="Equipment" />: a slot shows
///     again when the equipment changes or clears it. A double click on an item or a drag of it onto the Inventory window
///     takes it off; a drop of an inventory item on a slot equips it. The window lays out from the size of its content
///     (<see cref="MainHudLayout.EquipmentIn" />).
/// </summary>
internal sealed class EquipmentWindowControl : HudWindowContent
{
    private readonly Equipment Items;
    private readonly Func<ushort, DisplayColor, IconTexture?> IconSource;
    private readonly EquipmentSlotControl[] Slots;

    /// <param name="items">The equipment.</param>
    /// <param name="attributes">The player's attributes, which Personal View shows.</param>
    /// <param name="iconSource">The icon of an item sprite in a colour; null for the icons of <see cref="UiRenderer" />.</param>
    public EquipmentWindowControl(Equipment items, PlayerAttributes attributes, Func<ushort, DisplayColor, IconTexture?>? iconSource = null)
    {
        Name = "equipment_window";
        Items = items;
        IconSource = iconSource ?? HudItemSlotControl.ItemIcon;
        Slots = new EquipmentSlotControl[EquipmentSlots.All.Length];

        for (var i = 0; i < Slots.Length; i++)
        {
            var slot = new EquipmentSlotControl(EquipmentSlots.All[i]);
            slot.UnequipRequested += wire => UnequipRequested?.Invoke(wire);
            slot.EquipRequested += inventorySlot => EquipRequested?.Invoke(inventorySlot);
            slot.PointerChanged += OnPointerChanged;
            Slots[i] = slot;
            AddChild(slot);
        }

        Model = new CharacterModelControl();
        AddChild(Model);

        PersonalView = new PersonalViewControl(attributes);
        PersonalView.PinsChanged += pins => PinsChanged?.Invoke(pins);
        AddChild(PersonalView);

        ShowAll();

        Items.SlotChanged += OnSlotChanged;
        Items.SlotCleared += OnSlotChanged;
        Items.Cleared += OnCleared;
    }

    /// <summary>A double click on an item, or a drag of it onto the Inventory window: the world screen takes it off.</summary>
    public event Action<EquipmentSlot>? UnequipRequested;

    /// <summary>An item of the inventory slot dropped on a slot: the world screen uses it, which equips it.</summary>
    public event Action<byte>? EquipRequested;

    /// <summary>The pointer is on a slot, or on none (null): the world screen shows or hides the tooltip.</summary>
    public event Action<EquipmentSlotSpec?>? HoverChanged;

    /// <summary>A stat of Personal View was pinned or unpinned: the world screen saves the pins for the character.</summary>
    public event Action<IReadOnlyList<PersonalStat>>? PinsChanged;

    /// <summary>The slot under the pointer, or null.</summary>
    public EquipmentSlotSpec? HoveredSlot { get; private set; }

    internal IReadOnlyList<EquipmentSlotControl> SlotControls => Slots;

    internal CharacterModelControl Model { get; }

    internal PersonalViewControl PersonalView { get; }

    /// <summary>Shows the player's figure (<see cref="AislingRenderer.RenderSouthIdle" />); the window owns it.</summary>
    public void SetModel(Texture2D? figure) => Model.SetTexture(figure);

    /// <summary>Shows the saved pins of the character in Personal View, with no <see cref="PinsChanged" />.</summary>
    public void SetPins(IReadOnlyList<PersonalStat> pins) => PersonalView.SetPins(pins);

    /// <summary>The pointer is no longer on a slot: the window closed under it.</summary>
    public void ClearHover()
    {
        if (HoveredSlot is null)
            return;

        HoveredSlot = null;
        HoverChanged?.Invoke(null);
    }

    public override void Dispose()
    {
        Items.SlotChanged -= OnSlotChanged;
        Items.SlotCleared -= OnSlotChanged;
        Items.Cleared -= OnCleared;

        base.Dispose();
    }

    protected override void LayOut(int width, int height)
    {
        var layout = MainHudLayout.EquipmentIn(width, height);

        for (var i = 0; i < Slots.Length; i++)
            Slots[i].SetBounds(layout.Slots[i]);

        Model.SetBounds(layout.Model);
        PersonalView.SetBounds(layout.Stats);
    }

    private void ShowAll()
    {
        foreach (var slot in Slots)
            Show(slot);
    }

    private void Show(EquipmentSlotControl slot)
    {
        var data = slot.Spec.Wire is { } wire ? Items.GetSlot(wire) : null;

        slot.Show(data is { } item ? IconSource(item.Sprite, item.Color) : null);
    }

    private void OnSlotChanged(EquipmentSlot wire)
    {
        var index = EquipmentSlots.IndexOf(wire);

        if (index < 0)
            return;

        Show(Slots[index]);

        //the tooltip follows the item under the pointer
        if (HoveredSlot == Slots[index].Spec)
            HoverChanged?.Invoke(HoveredSlot);
    }

    private void OnCleared()
    {
        ShowAll();

        if (HoveredSlot is not null)
            HoverChanged?.Invoke(HoveredSlot);
    }

    private void OnPointerChanged(EquipmentSlotControl slot, bool entered)
    {
        if (entered)
            HoveredSlot = slot.Spec;
        else if (HoveredSlot == slot.Spec)
            HoveredSlot = null;
        else
            return;

        HoverChanged?.Invoke(HoveredSlot);
    }

}
