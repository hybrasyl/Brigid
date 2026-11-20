#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Models;
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
///     the last slots, with one swap for each move (<see cref="InventoryCompact" />). It sends one move at a time,
///     and plans the next move from the slots that the inventory shows when the server answered the move before it. H and E go to the first and the last row or page; the grid
///     shows every slot of today, so they stay disabled until the inventory grows past 72. Q and the coin button ask
///     for the Quest Items and the Currency lists (<see cref="InventoryListModal" />). The window lays out from the size of its
///     content (<see cref="MainHudLayout.InventoryIn" />). A slot shows again when the
///     inventory changes it, and every slot when the inventory clears.
/// </summary>
internal sealed class InventoryWindowControl : HudWindowContent
{
    /// <summary>The slots of the grid.</summary>
    public const int SLOTS = MainHudLayout.INVENTORY_COLUMNS * MainHudLayout.INVENTORY_ROWS;

    //the longest wait for the slots of a compact move: the server can refuse a move and send nothing for it
    private const long COMPACT_WAIT_MS = 2000;

    private readonly Inventory Items;
    private readonly PlayerAttributes Attributes;
    private readonly Func<ushort, DisplayColor, IconTexture?> IconSource;
    private readonly InventorySlotControl[] Slots = new InventorySlotControl[SLOTS];
    private readonly UILabel WeightText;
    private readonly UILabel GoldText;
    private readonly Func<long> Clock;
    private readonly Func<object?> ActiveDrag;
    private readonly HashSet<byte> CompactSlots = [];
    private long CompactSentAt;
    private bool? CompactToEnd;

    /// <param name="items">The inventory.</param>
    /// <param name="attributes">The player's attributes, which hold the weight and the gold.</param>
    /// <param name="iconSource">The icon of an item sprite in a colour; null for the icons of <see cref="UiRenderer" />.</param>
    /// <param name="clock">The time in milliseconds; null for <see cref="Environment.TickCount64" />.</param>
    /// <param name="activeDrag">The payload of the drag that is active, or null; null for the dispatcher's.</param>
    public InventoryWindowControl(
        Inventory items,
        PlayerAttributes attributes,
        Func<ushort, DisplayColor, IconTexture?>? iconSource = null,
        Func<long>? clock = null,
        Func<object?>? activeDrag = null)
    {
        ActiveDrag = activeDrag ?? (() => InputDispatcher.Instance?.ActiveDragPayload);
        Name = "inventory_window";
        Items = items;
        Attributes = attributes;
        IconSource = iconSource ?? HudItemSlotControl.ItemIcon;
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

    /// <summary>An item of the Equipment window dropped on the window (BRIG-151): the world screen takes it off.</summary>
    public event Action<EquipmentSlot>? UnequipRequested;

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
    ///     their order, with one swap for each move. It does not merge stacks. The server holds the new layout at once, and
    ///     the local inventory shows it only when the server answers, so the compact sends one move at a time: a batch
    ///     planned from the old slots, or a second press, moved items that the player did not see there. A press does
    ///     nothing while a move waits, for <see cref="COMPACT_WAIT_MS" /> at most.
    /// </summary>
    public void Compact(bool toEnd)
    {
        if (CompactWaits)
            return;

        CompactToEnd = toEnd;
        SendNextCompactMove();
    }

    /// <summary>True while a move of a compact waits for the inventory to show its slots.</summary>
    internal bool CompactWaits => (CompactToEnd is not null) && (Clock() - CompactSentAt < COMPACT_WAIT_MS);

    //the first move from the slots that the inventory shows now; the compact ends when no move is left
    private void SendNextCompactMove()
    {
        CompactSlots.Clear();

        if (CompactToEnd is not { } toEnd)
            return;

        var occupied = new bool[Inventory.MAX_SLOTS];

        for (var i = 0; i < occupied.Length; i++)
            occupied[i] = Items.GetSlot((byte)(i + 1)).IsOccupied;

        var moves = InventoryCompact.Moves(occupied, toEnd);

        if (moves.Count == 0)
        {
            CompactToEnd = null;

            return;
        }

        var (from, to) = moves[0];
        CompactSentAt = Clock();
        CompactSlots.Add(from);
        CompactSlots.Add(to);
        SwapRequested?.Invoke(from, to);
    }

    /// <summary>The pointer is no longer on a slot: the window closed under it.</summary>
    public void ClearHover()
    {
        if (HoveredSlot is null)
            return;

        HoveredSlot = null;
        HoverChanged?.Invoke(null);
    }

    //a drop of an equipped item anywhere on the window takes it off; the server puts it in the first free slot
    public override void OnDragDrop(DragDropEvent e)
    {
        if (e.Payload is not EquipmentDragPayload equipped)
            return;

        UnequipRequested?.Invoke(equipped.Slot);
        e.Handled = true;
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

        if (CompactSlots.Remove(slot) && (CompactSlots.Count == 0))
            SendNextCompactMove();

        //the tooltip follows the item under the pointer: a used or a new item changes it
        if (slot == HoveredSlot)
            RaiseHover();
    }

    private void OnCleared()
    {
        for (byte slot = 1; slot <= Inventory.MAX_SLOTS; slot++)
            ShowSlot(slot);

        CompactSlots.Clear();
        CompactToEnd = null;

        if (HoveredSlot is not null)
            RaiseHover();
    }

    //a drag that starts on a slot hides the tooltip of that slot at once: the pointer is still on it
    public override void Update(GameTime gameTime)
    {
        if ((HoveredSlot is not null) && (ActiveDrag() is not null))
        {
            HoveredSlot = null;
            RaiseHover();
        }

        base.Update(gameTime);
    }

    private void OnPointerChanged(byte slot, bool entered)
    {
        //during a drag no item tooltip shows, as on the classic inventory: the drag icon is what the player looks at
        if (ActiveDrag() is not null)
        {
            if (HoveredSlot is null)
                return;

            HoveredSlot = null;
            RaiseHover();

            return;
        }

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

}
