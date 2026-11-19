#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Rendering;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     The content of the Inventory window of the new HUD (BRIG-150, BRIG-127 D32, D33): the C, H and E buttons on
///     the first line, a grid of 9 by 8 slots, slot 1 at the top left and the numbers across each row, and a last line
///     with Q, the weight, the gold and the gold bag, and the coin button. The slots past the inventory size of today
///     (<see cref="Inventory.MAX_SLOTS" />) show dimmed. C compacts the items to the first slots, and a right click to
///     the last slots, with one swap for each move (<see cref="InventoryCompact" />); a compact waits until the
///     inventory shows the moves of the compact before it. H and E go to the first and the last row or page; the grid
///     shows every slot of today, so they stay disabled until the inventory grows past 72. Q and the coin button ask
///     for the Quest Items and the Currency lists (<see cref="InventoryListModal" />). The window lays out from the size of its
///     content (<see cref="MainHudLayout.InventoryIn" />). A slot shows again when the
///     inventory changes it, and every slot when the inventory clears.
/// </summary>
internal sealed class InventoryWindowControl : HudWindowContent
{
    /// <summary>The slots of the grid.</summary>
    public const int SLOTS = MainHudLayout.INVENTORY_COLUMNS * MainHudLayout.INVENTORY_ROWS;

    //the longest wait for the slots of a compact: the server can refuse a move and send nothing for it
    private const long COMPACT_WAIT_MS = 2000;

    private readonly Inventory Items;
    private readonly PlayerAttributes Attributes;
    private readonly Func<ushort, DisplayColor, IconTexture?> IconSource;
    private readonly InventorySlotControl[] Slots = new InventorySlotControl[SLOTS];
    private readonly UILabel WeightText;
    private readonly UILabel GoldText;
    private readonly Func<long> Clock;
    private readonly HashSet<byte> CompactSlots = [];
    private long CompactSentAt;

    /// <param name="items">The inventory.</param>
    /// <param name="attributes">The player's attributes, which hold the weight and the gold.</param>
    /// <param name="iconSource">The icon of an item sprite in a colour; null for the icons of <see cref="UiRenderer" />.</param>
    /// <param name="clock">The time in milliseconds; null for <see cref="Environment.TickCount64" />.</param>
    public InventoryWindowControl(
        Inventory items,
        PlayerAttributes attributes,
        Func<ushort, DisplayColor, IconTexture?>? iconSource = null,
        Func<long>? clock = null)
    {
        Name = "inventory_window";
        Items = items;
        Attributes = attributes;
        IconSource = iconSource ?? IconOf;
        Clock = clock ?? (() => Environment.TickCount64);

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
        AddChild(GoldBag);

        CompactButton = AddButton("compact", "C");
        CompactButton.Clicked += () => Compact(false);
        CompactButton.RightClicked += () => Compact(true);
        HomeButton = AddButton("home", "H");
        HomeButton.SetEnabled(false);
        EndButton = AddButton("end", "E");
        EndButton.SetEnabled(false);
        QuestItemsButton = AddButton("quest_items", "Q");
        QuestItemsButton.Clicked += () => ListRequested?.Invoke(InventoryListKind.QuestItems);

        CurrencyButton = new IconButton(
            ThemeRoles.IconHudGold,
            new Rectangle(0, 0, MainHudLayout.INVENTORY_BUTTON_WIDTH, MainHudLayout.INVENTORY_BUTTON_HEIGHT))
        {
            Name = "currency"
        };
        CurrencyButton.Clicked += () => ListRequested?.Invoke(InventoryListKind.Currency);
        AddChild(CurrencyButton);

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

    /// <summary>Q or the coin button: the world screen opens the list over the window.</summary>
    public event Action<InventoryListKind>? ListRequested;

    /// <summary>The slot under the pointer, with or without an item, or null.</summary>
    public byte? HoveredSlot { get; private set; }

    /// <summary>The slot controls of the grid, slot 1 first.</summary>
    internal IReadOnlyList<InventorySlotControl> SlotControls => Slots;

    internal string WeightShown => WeightText.Text;

    internal string GoldShown => GoldText.Text;

    internal TextButton CompactButton { get; }

    internal TextButton HomeButton { get; }

    internal TextButton EndButton { get; }

    internal TextButton QuestItemsButton { get; }

    internal IconButton CurrencyButton { get; }

    internal GoldBagControl GoldBag { get; } = new();

    /// <summary>
    ///     Moves the items to the first slots (<paramref name="toEnd" /> false) or to the last slots of the inventory, in
    ///     their order, with one swap for each move. It does not merge stacks. The local inventory changes only when the
    ///     server answers, so a compact does nothing while the inventory has not shown each slot of the compact before it,
    ///     for <see cref="COMPACT_WAIT_MS" /> at most: a second batch from the old slots puts the items out of order.
    /// </summary>
    public void Compact(bool toEnd)
    {
        if ((CompactSlots.Count > 0) && (Clock() - CompactSentAt < COMPACT_WAIT_MS))
            return;

        var occupied = new bool[Inventory.MAX_SLOTS];

        for (var i = 0; i < occupied.Length; i++)
            occupied[i] = Items.GetSlot((byte)(i + 1)).IsOccupied;

        var moves = InventoryCompact.Moves(occupied, toEnd);
        CompactSlots.Clear();
        CompactSentAt = Clock();

        foreach (var (from, to) in moves)
        {
            CompactSlots.Add(from);
            CompactSlots.Add(to);
            SwapRequested?.Invoke(from, to);
        }
    }

    /// <summary>True while a compact waits for the inventory to show its slots.</summary>
    internal bool CompactWaits => (CompactSlots.Count > 0) && (Clock() - CompactSentAt < COMPACT_WAIT_MS);

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
        GoldBag.SetBounds(layout.GoldBag);
        Place(CompactButton, layout.Compact);
        Place(HomeButton, layout.Home);
        Place(EndButton, layout.End);
        Place(QuestItemsButton, layout.QuestItems);
        Place(CurrencyButton, layout.Currency);
    }

    private TextButton AddButton(string name, string text)
    {
        var button = new TextButton(text, MainHudLayout.INVENTORY_BUTTON_WIDTH, MainHudLayout.INVENTORY_BUTTON_HEIGHT) { Name = name };
        AddChild(button);

        return button;
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
        CompactSlots.Remove(slot);

        //the tooltip follows the item under the pointer: a used or a new item changes it
        if (slot == HoveredSlot)
            RaiseHover();
    }

    private void OnCleared()
    {
        for (byte slot = 1; slot <= Inventory.MAX_SLOTS; slot++)
            ShowSlot(slot);

        CompactSlots.Clear();

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
