#region
using Brigid.Controls.Components;
using Brigid.Rendering;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     The content of the Inventory window of the new HUD (BRIG-150, BRIG-127 D32): a grid of 9 by 8 slots, slot 1 at the
///     top left and the numbers across each row, and a last line with the weight and the gold. The slots past the
///     inventory size of today (<see cref="Inventory.MAX_SLOTS" />) show dimmed. The window lays out from the size of its
///     content (<see cref="MainHudLayout.InventoryIn" />). A slot shows again when the
///     inventory changes it, and every slot when the inventory clears.
/// </summary>
internal sealed class InventoryWindowControl : HudWindowContent
{
    /// <summary>The slots of the grid.</summary>
    public const int SLOTS = MainHudLayout.INVENTORY_COLUMNS * MainHudLayout.INVENTORY_ROWS;

    private readonly Inventory Items;
    private readonly PlayerAttributes Attributes;
    private readonly Func<ushort, DisplayColor, IconTexture?> IconSource;
    private readonly InventorySlotControl[] Slots = new InventorySlotControl[SLOTS];
    private readonly UILabel WeightText;
    private readonly UILabel GoldText;

    /// <param name="items">The inventory.</param>
    /// <param name="attributes">The player's attributes, which hold the weight and the gold.</param>
    /// <param name="iconSource">The icon of an item sprite in a colour; null for the icons of <see cref="UiRenderer" />.</param>
    public InventoryWindowControl(Inventory items, PlayerAttributes attributes, Func<ushort, DisplayColor, IconTexture?>? iconSource = null)
    {
        Name = "inventory_window";
        Items = items;
        Attributes = attributes;
        IconSource = iconSource ?? IconOf;

        for (var i = 0; i < Slots.Length; i++)
        {
            var slot = new InventorySlotControl((byte)(i + 1), i < Inventory.MAX_SLOTS);
            slot.UseRequested += number => UseRequested?.Invoke(number);
            slot.SwapRequested += (from, to) => SwapRequested?.Invoke(from, to);
            slot.PointerChanged += OnPointerChanged;
            Slots[i] = slot;
            AddChild(slot);
        }

        WeightText = HudText.Label("weight", ThemeRoles.Text);
        AddChild(WeightText);
        GoldText = HudText.Label("gold", ThemeRoles.Text, HorizontalAlignment.Right);
        AddChild(GoldText);

        for (byte slot = 1; slot <= Inventory.MAX_SLOTS; slot++)
            ShowSlot(slot);

        ShowAttributes();

        Items.SlotChanged += OnSlotChanged;
        Items.Cleared += OnCleared;
        Attributes.Changed += ShowAttributes;
    }

    /// <summary>A double click on an item: the world screen uses it.</summary>
    public event Action<byte>? UseRequested;

    /// <summary>An item of the first slot dropped on the second slot: the world screen asks the server to swap them.</summary>
    public event Action<byte, byte>? SwapRequested;

    /// <summary>The pointer is on the item of a slot, or on no item (null): the world screen shows or hides the item tooltip.</summary>
    public event Action<byte?>? HoverChanged;

    /// <summary>The slot under the pointer, with or without an item, or null.</summary>
    public byte? HoveredSlot { get; private set; }

    /// <summary>The slot controls of the grid, slot 1 first.</summary>
    internal IReadOnlyList<InventorySlotControl> SlotControls => Slots;

    internal string WeightShown => WeightText.Text;

    internal string GoldShown => GoldText.Text;

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
        Items.Cleared -= OnCleared;
        Attributes.Changed -= ShowAttributes;

        base.Dispose();
    }

    protected override void LayOut(int width, int height)
    {
        var layout = MainHudLayout.InventoryIn(width, height);

        for (var i = 0; i < Slots.Length; i++)
            Slots[i].SetBounds(layout.SlotAt(i));

        WeightText.SetBounds(layout.Weight);
        GoldText.SetBounds(layout.Gold);
    }

    private void ShowSlot(byte slot)
    {
        var data = Items.GetSlot(slot);
        var icon = data.IsOccupied ? IconSource(data.Sprite, data.Color) : null;

        Slots[slot - 1].Show(icon, data.Stackable ? data.Count : 0);
    }

    private void OnSlotChanged(byte slot)
    {
        if (slot is 0 or > Inventory.MAX_SLOTS)
            return;

        ShowSlot(slot);

        //the tooltip follows the item under the pointer: a used or a new item changes it
        if (slot == HoveredSlot)
            RaiseHover();
    }

    private void OnCleared()
    {
        for (byte slot = 1; slot <= Inventory.MAX_SLOTS; slot++)
            ShowSlot(slot);

        if (HoveredSlot is not null)
            RaiseHover();
    }

    private void OnPointerChanged(byte slot, bool entered)
    {
        if (entered)
            HoveredSlot = slot;
        else if (HoveredSlot == slot)
            HoveredSlot = null;
        else
            return;

        RaiseHover();
    }

    private void RaiseHover() => HoverChanged?.Invoke(HoveredSlot is { } slot && Slots[slot - 1].Icon is not null ? slot : null);

    private void ShowAttributes()
    {
        WeightText.Text = $"Weight {HudFormat.Weight(Attributes.CurrentWeight, Attributes.MaxWeight)}";
        GoldText.Text = HudFormat.Gold(Attributes.Gold);
    }

    private static IconTexture? IconOf(ushort sprite, DisplayColor color) => UiRenderer.Instance?.GetItemIcon(sprite, color);
}
