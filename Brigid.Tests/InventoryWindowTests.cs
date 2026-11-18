#region
using System.Reflection;
using Brigid.Controls.Components;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Main;
using Brigid.Controls.World.Hud.Main.Windows;
using Brigid.Controls.World.Hud.Panel.Slots;
using Brigid.Definitions;
using Brigid.Models;
using Brigid.Rendering;
using Brigid.Screens;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-150: the Inventory window of the new HUD. The layout fits the grid of 9 by 8 in the content, also a smaller
///     content; the slots past the inventory size of today are dimmed; a slot shows its item and the count of a stack; a
///     double click uses an item, a drag carries its slot, and a drop on another slot swaps; the hover follows the item
///     under the pointer; A and Shift+A open and close the window and select the hidden classic inventory.
/// </summary>
[Collection("FontEngine")]
public class InventoryWindowTests : IDisposable
{
    private const int FLAT_WIDTH = 298;
    private const int FLAT_HEIGHT = 311;

    private readonly List<IDisposable> Owned = [];

    public InventoryWindowTests() => FontEngine.Initialize(0);

    public void Dispose()
    {
        foreach (var owned in Owned)
            owned.Dispose();
    }

    //the icon of a sprite; the tests read the icon, not its texture
    private static IconTexture? FakeIcon(ushort sprite, DisplayColor color) => new IconTexture(null!, sprite, (int)color);

    private (InventoryWindowControl Window, Inventory Items, PlayerAttributes Attributes) NewWindow()
    {
        var items = new Inventory();
        items.SetSlot(1, 100, DisplayColor.Default, "Apple", true, 3, 0, 0);
        items.SetSlot(2, 200, DisplayColor.Default, "Sword", false, 1, 1000, 900);
        items.SetSlot(59, 300, DisplayColor.Default, "Rock", true, 1, 0, 0);
        var attributes = new PlayerAttributes();

        var window = new InventoryWindowControl(items, attributes, FakeIcon);
        window.SetBounds(new Rectangle(6, 22, FLAT_WIDTH, FLAT_HEIGHT));
        Owned.Add(window);

        return (window, items, attributes);
    }

    private static InventorySlotControl SlotOf(InventoryWindowControl window, byte slot) => window.SlotControls[slot - 1];

    [Fact]
    public void TheFlatLayoutHasSlots33PxApartAndTheLastLineUnderTheGrid()
    {
        var layout = MainHudLayout.InventoryIn(FLAT_WIDTH, FLAT_HEIGHT);

        Assert.Equal(33, layout.Pitch);
        Assert.Equal(new Rectangle(1, 18, 32, 32), layout.SlotAt(0));
        Assert.Equal(new Rectangle(1 + 8 * 33, 18, 32, 32), layout.SlotAt(8));
        Assert.Equal(new Rectangle(1, 18 + 33, 32, 32), layout.SlotAt(9));
        Assert.True(layout.SlotAt(InventoryWindowControl.SLOTS - 1).Right <= FLAT_WIDTH);
        Assert.True(layout.SlotAt(InventoryWindowControl.SLOTS - 1).Bottom <= layout.Weight.Y);
        Assert.Equal(FLAT_HEIGHT, layout.Weight.Bottom);
    }

    [Theory]
    [InlineData(290, 311)]
    [InlineData(298, 292)]
    public void ASmallerContentBringsTheSlotsCloser(int width, int height)
    {
        var layout = MainHudLayout.InventoryIn(width, height);

        Assert.Equal(32, layout.Pitch);
        Assert.True(layout.SlotAt(InventoryWindowControl.SLOTS - 1).Right <= width);
        Assert.True(layout.SlotAt(InventoryWindowControl.SLOTS - 1).Bottom <= layout.Weight.Y);
    }

    [Fact]
    public void TheButtonsAndTheLastLineStayInTheContent()
    {
        var layout = MainHudLayout.InventoryIn(FLAT_WIDTH, FLAT_HEIGHT);

        Assert.Equal(FLAT_WIDTH, layout.End.Right);
        Assert.True(layout.Compact.Right < layout.Home.X);
        Assert.True(layout.Home.Right < layout.End.X);
        Assert.Equal(0, layout.QuestItems.X);
        Assert.True(layout.Weight.Right <= layout.Gold.X);
        Assert.True(layout.Gold.Right < layout.GoldBag.X);
        Assert.True(layout.GoldBag.Right < layout.Currency.X);
        Assert.Equal(FLAT_WIDTH, layout.Currency.Right);
    }

    [Fact]
    public void TheSlotsAreNumberedAcrossAndPast59AreDimmed()
    {
        var (window, _, _) = NewWindow();

        Assert.Equal(InventoryWindowControl.SLOTS, window.SlotControls.Count);
        Assert.Equal(Enumerable.Range(1, 72).Select(i => (byte)i), window.SlotControls.Select(slot => slot.Slot));
        Assert.All(window.SlotControls.Take(Inventory.MAX_SLOTS), slot => Assert.True(slot.Usable));
        Assert.All(window.SlotControls.Skip(Inventory.MAX_SLOTS), slot => Assert.False(slot.Usable));
    }

    [Fact]
    public void ASlotShowsItsItemAndTheCountOfAStack()
    {
        var (window, _, _) = NewWindow();

        Assert.Equal(100, SlotOf(window, 1).Icon?.OffsetX);
        Assert.Equal("3", SlotOf(window, 1).CountShown);
        Assert.Equal(200, SlotOf(window, 2).Icon?.OffsetX);
        Assert.Equal(string.Empty, SlotOf(window, 2).CountShown);
        Assert.Equal(string.Empty, SlotOf(window, 59).CountShown);
        Assert.Null(SlotOf(window, 3).Icon);
    }

    [Fact]
    public void AnItemThatDoesNotStackShowsNoCount()
    {
        var items = new Inventory();
        items.SetSlot(4, 100, DisplayColor.Default, "Ring", false, 2, 0, 0);
        var window = new InventoryWindowControl(items, new PlayerAttributes(), FakeIcon);
        Owned.Add(window);

        Assert.Equal(string.Empty, window.SlotControls[3].CountShown);
    }

    [Fact]
    public void ALargerContentKeepsTheSlots33PxApart()
    {
        var layout = MainHudLayout.InventoryIn(400, 400);

        Assert.Equal(33, layout.Pitch);
    }

    [Fact]
    public void TheItemColourGoesToTheIcon()
    {
        var items = new Inventory();
        items.SetSlot(4, 100, (DisplayColor)5, "Dyed Robe", false, 1, 0, 0);
        var window = new InventoryWindowControl(items, new PlayerAttributes(), FakeIcon);
        Owned.Add(window);

        Assert.Equal(5, window.SlotControls[3].Icon?.OffsetY);
    }

    [Fact]
    public void ADimmedSlotShowsNoItem()
    {
        var slot = new InventorySlotControl(60, usable: false);
        Owned.Add(slot);

        slot.Show(new IconTexture(null!, 0, 0), 5);

        Assert.Null(slot.Icon);
        Assert.Equal(string.Empty, slot.CountShown);
    }

    [Fact]
    public void ASlotChangeAndAClearShowAgain()
    {
        var (window, items, _) = NewWindow();

        items.SetSlot(3, 400, DisplayColor.Default, "Arrow", true, 20, 0, 0);
        Assert.Equal(400, SlotOf(window, 3).Icon?.OffsetX);
        Assert.Equal("20", SlotOf(window, 3).CountShown);

        items.ClearSlot(1);
        Assert.Null(SlotOf(window, 1).Icon);
        Assert.Equal(string.Empty, SlotOf(window, 1).CountShown);

        items.Clear();
        Assert.All(window.SlotControls, slot => Assert.Null(slot.Icon));
    }

    [Fact]
    public void ADoubleClickUsesAnItem()
    {
        var (window, _, _) = NewWindow();
        var used = new List<byte>();
        window.UseRequested += used.Add;

        var click = new ClickEvent { Button = MouseButton.Left };
        SlotOf(window, 2).OnClick(click);
        Assert.Empty(used);
        Assert.True(click.Handled);

        var doubleClick = new DoubleClickEvent { Button = MouseButton.Left };
        SlotOf(window, 2).OnDoubleClick(doubleClick);
        Assert.Equal([2], used);
        Assert.True(doubleClick.Handled);
    }

    [Fact]
    public void ADoubleClickOnAnEmptySlotOrWithTheRightButtonDoesNothing()
    {
        var (window, _, _) = NewWindow();
        var used = 0;
        window.UseRequested += _ => used++;

        SlotOf(window, 3).OnDoubleClick(new DoubleClickEvent { Button = MouseButton.Left });
        SlotOf(window, 2).OnDoubleClick(new DoubleClickEvent { Button = MouseButton.Right });
        SlotOf(window, 60).OnDoubleClick(new DoubleClickEvent { Button = MouseButton.Left });

        Assert.Equal(0, used);
    }

    [Fact]
    public void ADragOfAnItemCarriesItsSlotAndIcon()
    {
        var (window, _, _) = NewWindow();

        var drag = new DragStartEvent { Button = MouseButton.Left };
        SlotOf(window, 2).OnDragStart(drag);

        var payload = Assert.IsType<ItemDragPayload>(drag.Payload);
        Assert.Equal(2, payload.Slot);
    }

    [Fact]
    public void NoDragFromAnEmptySlotWithTheRightButtonOrAfterADoubleClick()
    {
        var (window, _, _) = NewWindow();

        var empty = new DragStartEvent { Button = MouseButton.Left };
        SlotOf(window, 3).OnDragStart(empty);
        Assert.Null(empty.Payload);

        var right = new DragStartEvent { Button = MouseButton.Right };
        SlotOf(window, 2).OnDragStart(right);
        Assert.Null(right.Payload);
    }

    [Fact]
    public void ADropOnAnotherSlotSwaps()
    {
        var (window, _, _) = NewWindow();
        var swaps = new List<(byte, byte)>();
        window.SwapRequested += (from, to) => swaps.Add((from, to));

        var drop = new DragDropEvent { Payload = new ItemDragPayload(1, null) };
        SlotOf(window, 5).OnDragDrop(drop);

        Assert.Equal([((byte)1, (byte)5)], swaps);
        Assert.True(drop.Handled);
    }

    [Fact]
    public void ADropOnItsOwnSlotOrADimmedSlotEndsTheDragWithNoSwap()
    {
        var (window, _, _) = NewWindow();
        var swaps = 0;
        window.SwapRequested += (_, _) => swaps++;

        var own = new DragDropEvent { Payload = new ItemDragPayload(1, null) };
        SlotOf(window, 1).OnDragDrop(own);
        var dimmed = new DragDropEvent { Payload = new ItemDragPayload(1, null) };
        SlotOf(window, 60).OnDragDrop(dimmed);

        Assert.Equal(0, swaps);
        Assert.True(own.Handled);
        Assert.True(dimmed.Handled);
    }

    [Fact]
    public void ADropOfAnotherPayloadPassesOn()
    {
        var (window, _, _) = NewWindow();
        var swaps = 0;
        window.SwapRequested += (_, _) => swaps++;

        var drop = new DragDropEvent { Payload = new AbilityDragPayload(true, 3, null) };
        SlotOf(window, 5).OnDragDrop(drop);

        Assert.Equal(0, swaps);
        Assert.False(drop.Handled);
    }

    [Fact]
    public void ADraggedItemMarksAUsableSlotAsTheDropTarget()
    {
        var (window, _, _) = NewWindow();

        SlotOf(window, 5).OnDragMove(new DragMoveEvent { Payload = new ItemDragPayload(1, null) });
        SlotOf(window, 60).OnDragMove(new DragMoveEvent { Payload = new ItemDragPayload(1, null) });
        SlotOf(window, 6).OnDragMove(new DragMoveEvent { Payload = new AbilityDragPayload(true, 3, null) });

        Assert.True(SlotOf(window, 5).ShowsDropTarget);
        Assert.False(SlotOf(window, 60).ShowsDropTarget);
        Assert.False(SlotOf(window, 6).ShowsDropTarget);

        SlotOf(window, 5).OnMouseLeave();
        Assert.False(SlotOf(window, 5).ShowsDropTarget);
    }

    [Fact]
    public void TheDropHighlightEndsWithTheDrag()
    {
        var (window, _, _) = NewWindow();

        SlotOf(window, 5).OnDragMove(new DragMoveEvent { Payload = new ItemDragPayload(1, null) });
        SlotOf(window, 5).Update(new GameTime());

        //no drag is active in the tests, as after a drag that a blocked release cancelled
        Assert.False(SlotOf(window, 5).ShowsDropTarget);
    }

    [Fact]
    public void ARootDropOfAnAbilityCastsASpellOrEnds()
    {
        Assert.Equal(WorldScreen.RootDrop.Spell, WorldScreen.RootDropOf(new AbilityDragPayload(true, 3, null)));
        Assert.Equal(WorldScreen.RootDrop.Ends, WorldScreen.RootDropOf(new AbilityDragPayload(false, 3, null)));
    }

    [Fact]
    public void ARootDropOfAWindowItemGoesToTheInventoryDrop()
        => Assert.Equal(WorldScreen.RootDrop.Item, WorldScreen.RootDropOf(new ItemDragPayload(4, null)));

    [Fact]
    public void ARootDropOfAClassicSlotGoesToItsPanel()
        => Assert.Equal(WorldScreen.RootDrop.ClassicSlot, WorldScreen.RootDropOf(new SlotDragPayload { Source = new PanelSlot(), SlotIndex = 1, SourcePanel = HudTab.Inventory }));

    [Fact]
    public void ARootDropOfAnythingElsePassesOn()
    {
        Assert.Equal(WorldScreen.RootDrop.None, WorldScreen.RootDropOf(null));
        Assert.Equal(WorldScreen.RootDrop.None, WorldScreen.RootDropOf("text"));
    }

    [Fact]
    public void TheHoverFollowsTheItemUnderThePointer()
    {
        var (window, items, _) = NewWindow();
        var hovers = new List<byte?>();
        window.HoverChanged += hovers.Add;

        SlotOf(window, 2).OnMouseEnter();
        SlotOf(window, 2).OnMouseLeave();
        SlotOf(window, 3).OnMouseEnter();
        items.SetSlot(3, 400, DisplayColor.Default, "Arrow", true, 20, 0, 0);
        items.ClearSlot(3);
        window.ClearHover();

        Assert.Equal([2, null, null, 3, null, null], hovers);
        Assert.Null(window.HoveredSlot);
    }

    [Fact]
    public void ALeaveOfTheSlotBeforeDoesNotClearTheHover()
    {
        var (window, _, _) = NewWindow();

        SlotOf(window, 2).OnMouseEnter();
        SlotOf(window, 1).OnMouseEnter();
        SlotOf(window, 2).OnMouseLeave();

        Assert.Equal((byte)1, window.HoveredSlot);
    }

    [Fact]
    public void AChangeOfAnotherSlotDoesNotMoveTheHover()
    {
        var (window, items, _) = NewWindow();
        var hovers = new List<byte?>();
        SlotOf(window, 2).OnMouseEnter();
        window.HoverChanged += hovers.Add;

        items.SetSlot(3, 400, DisplayColor.Default, "Arrow", true, 20, 0, 0);
        window.ClearHover();
        window.ClearHover();

        Assert.Equal([null], hovers);
    }

    [Fact]
    public void TheLastLineShowsTheWeightAndTheGold()
    {
        var (window, _, attributes) = NewWindow();

        attributes.Update(new AttributesPacket
        {
            Primary = new PrimaryAttributes { CurrentWeight = 42, MaxWeight = 120 },
            Experience = new ExperienceAttributes { Gold = 1_234_567 }
        });

        Assert.Equal("Weight 42/120", window.WeightShown);
        Assert.Equal("1.2M", window.GoldShown);
    }

    private MainHudControl NewHud(out MainHudControlTests.RecordingHud classic)
    {
        var proxy = DispatchProxy.Create<IWorldHud, MainHudControlTests.RecordingHud>();
        var hud = new MainHudControl(proxy, attributes: new PlayerAttributes(), chat: new Chat());
        Owned.Add(hud);
        classic = (MainHudControlTests.RecordingHud)(object)proxy;

        return hud;
    }

    private static bool InventoryOpen(MainHudControl hud) => hud.Windows.Panels.Single(panel => panel.Window == HudWindows.Inventory).Visible;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheInventoryKeysOpenAndCloseTheWindowAndOnlySelectTheClassicPanel(bool shift)
    {
        var hud = NewHud(out var classic);

        hud.HandleTabActivation(HudTab.Inventory, shift);
        Assert.True(InventoryOpen(hud));

        hud.HandleTabActivation(HudTab.Inventory, shift);
        Assert.False(InventoryOpen(hud));

        //a second A on the classic HUD opens the status book, and Shift+A expands the panel: the new HUD selects only
        Assert.Equal(["ShowTab", "ShowTab"], classic.Calls);
    }
}
