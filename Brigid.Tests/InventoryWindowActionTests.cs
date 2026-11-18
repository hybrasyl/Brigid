#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Controls.World.Hud.Main;
using Brigid.Controls.World.Hud.Main.Windows;
using Brigid.Definitions;
using Brigid.Models;
using Brigid.Rendering;
using Brigid.Screens;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-150 phase 2: the header buttons, the gold bag and the lists of the Inventory window. C moves the items to the
///     first slots and a right click to the last slots, in their order, each move into an empty slot; H and E are disabled
///     while the grid shows every slot; Q and the coin button ask for their lists; a drag of the gold bag carries the gold
///     payload; the list modal shows its title, its rows in short form with the full amount for the tooltip, and the line
///     of an empty list.
/// </summary>
[Collection("FontEngine")]
public class InventoryWindowActionTests : IDisposable
{
    private readonly List<IDisposable> Owned = [];

    public InventoryWindowActionTests() => FontEngine.Initialize(0);

    public void Dispose()
    {
        foreach (var owned in Owned)
            owned.Dispose();
    }

    private static bool[] Occupied(string pattern) => pattern.Select(c => c == 'x').ToArray();

    //does the moves one after the other, as the server does, and checks that each one goes into an empty slot
    private static string Apply(string pattern, IReadOnlyList<(byte From, byte To)> moves)
    {
        var slots = pattern.ToCharArray();

        foreach (var (from, to) in moves)
        {
            Assert.Equal('x', slots[from - 1]);
            Assert.Equal('.', slots[to - 1]);
            (slots[from - 1], slots[to - 1]) = (slots[to - 1], slots[from - 1]);
        }

        return new string(slots);
    }

    [Fact]
    public void ACompactMovesTheItemsToTheFirstSlotsInTheirOrder()
    {
        var moves = InventoryCompact.Moves(Occupied(".x.xx"), false);

        Assert.Equal([((byte)2, (byte)1), ((byte)4, (byte)2), ((byte)5, (byte)3)], moves);
    }

    [Fact]
    public void ACompactToTheEndMovesTheItemsToTheLastSlotsInTheirOrder()
    {
        var moves = InventoryCompact.Moves(Occupied("x.x.."), true);

        Assert.Equal([((byte)3, (byte)5), ((byte)1, (byte)4)], moves);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(".....", false)]
    [InlineData("xxx..", false)]
    [InlineData("..xxx", true)]
    [InlineData("xxxxx", true)]
    public void ACompactInventoryNeedsNoMove(string pattern, bool toEnd) => Assert.Empty(InventoryCompact.Moves(Occupied(pattern), toEnd));

    [Theory]
    [InlineData("x.x.x.x.x.x.x.x.x.x")]
    [InlineData("....xxxx....x....xx")]
    [InlineData("xx......x.x......x.")]
    public void EveryMoveGoesIntoAnEmptySlotAndPacksTheItems(string pattern)
    {
        var count = pattern.Count(c => c == 'x');

        Assert.Equal(new string('x', count) + new string('.', pattern.Length - count), Apply(pattern, InventoryCompact.Moves(Occupied(pattern), false)));
        Assert.Equal(new string('.', pattern.Length - count) + new string('x', count), Apply(pattern, InventoryCompact.Moves(Occupied(pattern), true)));
    }

    private long Now;

    private (InventoryWindowControl Window, Inventory Items) NewWindow()
    {
        var items = new Inventory();
        items.SetSlot(2, 100, DisplayColor.Default, "Apple", true, 3, 0, 0);
        items.SetSlot(5, 200, DisplayColor.Default, "Sword", false, 1, 1000, 900);
        var window = new InventoryWindowControl(items, new PlayerAttributes(), (_, _) => null, () => Now);
        window.SetBounds(new Rectangle(6, 22, 298, 311));
        Owned.Add(window);

        return (window, items);
    }

    [Fact]
    public void CSwapsTheItemsToTheFirstSlots()
    {
        var (window, _) = NewWindow();
        var swaps = new List<(byte, byte)>();
        window.SwapRequested += (from, to) => swaps.Add((from, to));

        var click = new ClickEvent { Button = MouseButton.Left };
        window.CompactButton.OnClick(click);

        Assert.Equal([((byte)2, (byte)1), ((byte)5, (byte)2)], swaps);
        Assert.True(click.Handled);
    }

    [Fact]
    public void ARightClickOnCSwapsTheItemsToTheLastSlotsOfTheInventory()
    {
        var (window, _) = NewWindow();
        var swaps = new List<(byte, byte)>();
        window.SwapRequested += (from, to) => swaps.Add((from, to));

        var click = new ClickEvent { Button = MouseButton.Right };
        window.CompactButton.OnClick(click);

        Assert.Equal([((byte)5, (byte)Inventory.MAX_SLOTS), ((byte)2, (byte)(Inventory.MAX_SLOTS - 1))], swaps);
        Assert.True(click.Handled);
    }

    //the server changes the inventory after the moves: a second batch from the old slots puts the items out of order
    [Fact]
    public void ACompactWaitsUntilTheInventoryShowsTheCompactBefore()
    {
        var (window, items) = NewWindow();
        var swaps = new List<(byte, byte)>();
        window.SwapRequested += (from, to) => swaps.Add((from, to));

        window.Compact(false);
        window.Compact(false);
        window.Compact(true);
        Assert.Equal(2, swaps.Count);
        Assert.True(window.CompactWaits);

        //the server's answer: slot 1 and slot 2 hold the items, slot 5 is empty
        items.SetSlot(1, 100, DisplayColor.Default, "Apple", true, 3, 0, 0);
        items.SetSlot(2, 200, DisplayColor.Default, "Sword", false, 1, 1000, 900);
        items.ClearSlot(5);
        Assert.False(window.CompactWaits);

        window.Compact(true);
        Assert.Equal([((byte)2, (byte)1), ((byte)5, (byte)2), ((byte)2, (byte)Inventory.MAX_SLOTS), ((byte)1, (byte)(Inventory.MAX_SLOTS - 1))], swaps);
    }

    [Fact]
    public void ACompactThatTheServerDoesNotAnswerWaitsTwoSecondsAtMost()
    {
        var (window, _) = NewWindow();
        var swaps = 0;
        window.SwapRequested += (_, _) => swaps++;

        window.Compact(false);
        Now += 1999;
        window.Compact(false);
        Assert.Equal(2, swaps);

        Now += 1;
        window.Compact(false);
        Assert.Equal(4, swaps);
    }

    [Fact]
    public void ACompactWithNoMoveDoesNotWait()
    {
        var items = new Inventory();
        items.SetSlot(1, 100, DisplayColor.Default, "Apple", true, 3, 0, 0);
        var window = new InventoryWindowControl(items, new PlayerAttributes(), (_, _) => null, () => Now);
        Owned.Add(window);

        window.Compact(false);

        Assert.False(window.CompactWaits);
    }

    [Fact]
    public void AClearOfTheInventoryEndsTheWait()
    {
        var (window, items) = NewWindow();

        window.Compact(false);
        items.Clear();

        Assert.False(window.CompactWaits);
    }

    [Fact]
    public void HAndEAreDisabledWhileTheGridShowsEverySlot()
    {
        var (window, _) = NewWindow();

        Assert.True(InventoryWindowControl.SLOTS >= Inventory.MAX_SLOTS);
        Assert.False(window.HomeButton.Enabled);
        Assert.False(window.EndButton.Enabled);
        Assert.True(window.CompactButton.Enabled);
    }

    [Fact]
    public void QAndTheCoinButtonAskForTheirLists()
    {
        var (window, _) = NewWindow();
        var lists = new List<InventoryListKind>();
        window.ListRequested += lists.Add;

        window.QuestItemsButton.OnClick(new ClickEvent { Button = MouseButton.Left });
        window.CurrencyButton.OnClick(new ClickEvent { Button = MouseButton.Left });

        Assert.Equal([InventoryListKind.QuestItems, InventoryListKind.Currency], lists);
    }

    [Fact]
    public void TheButtonsAreInTheirPlaces()
    {
        var (window, _) = NewWindow();
        var layout = MainHudLayout.InventoryIn(298, 311);

        Assert.Equal(layout.Compact.Location, new Point(window.CompactButton.X, window.CompactButton.Y));
        Assert.Equal(layout.End.Location, new Point(window.EndButton.X, window.EndButton.Y));
        Assert.Equal(layout.QuestItems.Location, new Point(window.QuestItemsButton.X, window.QuestItemsButton.Y));
        Assert.Equal(layout.Currency.Location, new Point(window.CurrencyButton.X, window.CurrencyButton.Y));
        Assert.Equal(layout.GoldBag, new Rectangle(window.GoldBag.X, window.GoldBag.Y, window.GoldBag.Width, window.GoldBag.Height));
    }

    [Fact]
    public void ADragOfTheGoldBagCarriesTheGoldAndItsIcon()
    {
        var (window, _) = NewWindow();

        var drag = new DragStartEvent { Button = MouseButton.Left };
        window.GoldBag.OnDragStart(drag);

        var payload = Assert.IsType<GoldDragPayload>(drag.Payload);
        IDragIconPayload icon = payload;
        Assert.Null(icon.Icon);
        Assert.Equal(ThemeRoles.IconInventoryGoldBag, icon.IconRole);
        Assert.Equal(WorldScreen.RootDrop.Gold, WorldScreen.RootDropOf(payload));

        var right = new DragStartEvent { Button = MouseButton.Right };
        window.GoldBag.OnDragStart(right);
        Assert.Null(right.Payload);
    }

    [Fact]
    public void AClickOnTheGoldBagDoesNotReachTheWorld()
    {
        var (window, _) = NewWindow();
        var click = new ClickEvent { Button = MouseButton.Left };

        window.GoldBag.OnClick(click);

        Assert.True(click.Handled);
    }

    [Fact]
    public void GoldDroppedOnASlotIsNotASwap()
    {
        var (window, _) = NewWindow();
        var swaps = 0;
        window.SwapRequested += (_, _) => swaps++;

        var drop = new DragDropEvent { Payload = new GoldDragPayload() };
        window.SlotControls[0].OnDragDrop(drop);

        Assert.Equal(0, swaps);
        Assert.False(drop.Handled);
    }

    [Fact]
    public void ADragIconWithATextureHasNoRole()
    {
        IDragIconPayload item = new ItemDragPayload(1, null);

        Assert.Null(item.IconRole);
    }

    [Fact]
    public void ATextButtonTakesARightClickOnlyWithAHandler()
    {
        var button = new TextButton("C", 18, 14);
        Owned.Add(button);

        var unhandled = new ClickEvent { Button = MouseButton.Right };
        button.OnClick(unhandled);
        Assert.False(unhandled.Handled);

        var right = 0;
        button.RightClicked += () => right++;
        var handled = new ClickEvent { Button = MouseButton.Right };
        button.OnClick(handled);
        Assert.Equal(1, right);
        Assert.True(handled.Handled);

        button.SetEnabled(false);
        var disabled = new ClickEvent { Button = MouseButton.Right };
        button.OnClick(disabled);
        Assert.Equal(1, right);
        Assert.True(disabled.Handled);
    }

    [Theory]
    [InlineData(999_999u, "999,999")]
    [InlineData(1_234_567u, "1.2M")]
    [InlineData(12_345_678u, "12M")]
    [InlineData(1_234_567_890u, "1.2B")]
    public void AnAmountShowsInShortForm(uint amount, string expected)
    {
        Assert.Equal(expected, HudFormat.Amount(amount));
        Assert.Equal(expected, HudFormat.Gold(amount));
    }

    [Theory]
    [InlineData(999_999u, null)]
    [InlineData(1_000_000u, "1,000,000")]
    public void TheFullAmountIsForTheShortFormOnly(uint amount, string? expected)
    {
        Assert.Equal(expected, HudFormat.FullAmount(amount));
        Assert.Equal(expected is null ? null : $"{expected} gold", HudFormat.GoldTooltip(amount));
    }

    private InventoryListModal NewModal()
    {
        var modal = new InventoryListModal();
        Owned.Add(modal);

        return modal;
    }

    private static readonly Rectangle InventoryPanel = new(322, 28, 310, 339);

    [Theory]
    [InlineData(InventoryListKind.Currency, "Currencies", "No currencies yet.")]
    [InlineData(InventoryListKind.QuestItems, "Quest Items", "No quest items yet.")]
    public void AnEmptyListShowsItsTitleAndItsLine(InventoryListKind kind, string title, string empty)
    {
        var modal = NewModal();

        modal.Open(kind, [], InventoryPanel);

        Assert.True(modal.Visible);
        Assert.Equal(kind, modal.Kind);
        Assert.Equal(title, modal.FindChild<UILabel>("title")!.Text);
        Assert.Equal(empty, modal.EmptyShown);
    }

    [Fact]
    public void TheModalIsAtTheCentreOfTheInventoryPanelAndShowsTenRows()
    {
        var modal = NewModal();

        modal.Open(InventoryListKind.Currency, [], InventoryPanel);

        Assert.Equal(InventoryListModal.WIDTH, modal.Width);
        Assert.Equal(InventoryPanel.X + (InventoryPanel.Width - modal.Width) / 2, modal.X);
        Assert.Equal(InventoryPanel.Y + (InventoryPanel.Height - modal.Height) / 2, modal.Y);
        Assert.True(InventoryPanel.Contains(new Rectangle(modal.X, modal.Y, modal.Width, modal.Height)));
        //ten rows, as in the wireframe
        Assert.Equal(10, modal.Rows.Count());
    }

    [Fact]
    public void AListWithEntriesHasNoEmptyLine()
    {
        var modal = NewModal();
        InventoryListEntry[] entries = [new("Dream Shard", 500), new("Blood Shard", 1_234_567)];

        modal.Open(InventoryListKind.Currency, entries, InventoryPanel);

        Assert.Equal(entries, modal.Entries);
        Assert.Equal(string.Empty, modal.EmptyShown);
    }

    //the list binds its rows when it draws, so the test binds a row as the list does
    [Fact]
    public void ARowShowsTheNameAndTheAmountAndEverySecondRowHasAShade()
    {
        InventoryListEntry[] entries = [new("Dream Shard", 500), new("Blood Shard", 1_234_567), new("Wood Shard", 7)];
        var rows = entries.Select((entry, index) =>
                          {
                              var row = new InventoryListRow(200, InventoryListModal.ROW_HEIGHT);
                              Owned.Add(row);
                              row.Bind(new VirtualRow<InventoryListEntry> { Kind = VirtualRowKind.Item, Item = entry, Index = index });

                              return row;
                          })
                          .ToArray();

        Assert.All(rows, row => Assert.True(row.Visible));
        Assert.Equal(["Dream Shard", "Blood Shard", "Wood Shard"], rows.Select(row => row.NameShown));
        Assert.Equal(["500", "1.2M", "7"], rows.Select(row => row.AmountShown));
        Assert.Equal([null, "1,234,567", null], rows.Select(row => row.FullAmount));
        Assert.Equal([false, true, false], rows.Select(row => row.IsShaded));
    }

    [Fact]
    public void AnEmptyListSlotHidesTheRow()
    {
        var row = new InventoryListRow(200, InventoryListModal.ROW_HEIGHT);
        Owned.Add(row);
        row.Bind(new VirtualRow<InventoryListEntry> { Kind = VirtualRowKind.Item, Item = new("Blood Shard", 1_234_567), Index = 0 });

        row.Bind(new VirtualRow<InventoryListEntry> { Kind = VirtualRowKind.Empty });

        Assert.False(row.Visible);
        Assert.Null(row.FullAmount);
    }

    [Fact]
    public void CloseClosesTheModal()
    {
        var modal = NewModal();
        modal.Open(InventoryListKind.Currency, [], InventoryPanel);

        modal.FindChild<TextButton>("close")!.PerformClick();

        Assert.False(modal.Visible);
    }

    [Fact]
    public void EscapeClosesTheModal()
    {
        var modal = NewModal();
        modal.Open(InventoryListKind.QuestItems, [], InventoryPanel);

        var escape = new KeyDownEvent { Key = Keys.Escape };
        modal.OnKeyDown(escape);

        Assert.False(modal.Visible);
        Assert.True(escape.Handled);
    }
}
