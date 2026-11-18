#region
using System.Reflection;
using Brigid.Controls.Components;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Main;
using Brigid.Controls.World.Hud.Main.Windows;
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
///     BRIG-151: the Equipment window of the new HUD. The layout puts every slot around the model, inside the content and
///     clear of Personal View; each slot of the wire has one control, and Cape, Mount and Trinket are disabled; a slot
///     shows its item, a double click or a drag onto the Inventory window takes it off, and a drop of an inventory item
///     equips it; the model is scaled up and clipped; Shift+G opens and closes the window.
/// </summary>
[Collection("FontEngine")]
public class EquipmentWindowTests : IDisposable
{
    private const int FLAT_WIDTH = 298;
    private const int FLAT_HEIGHT = 311;

    private readonly List<IDisposable> Owned = [];

    public EquipmentWindowTests() => FontEngine.Initialize(0);

    public void Dispose()
    {
        foreach (var owned in Owned)
            owned.Dispose();
    }

    //the icon of a sprite; the tests read the icon, not its texture
    private static IconTexture? FakeIcon(ushort sprite, DisplayColor color) => new IconTexture(null!, sprite, (int)color);

    private (EquipmentWindowControl Window, Equipment Items) NewWindow()
    {
        var items = new Equipment();
        items.SetSlot(EquipmentSlot.Weapon, 10, DisplayColor.Default, "Stick", 1000, 900);
        items.SetSlot(EquipmentSlot.Overcoat, 20, DisplayColor.Default, "Robe", 0, 0);

        var window = new EquipmentWindowControl(items, new PlayerAttributes(), FakeIcon);
        window.SetBounds(new Rectangle(6, 22, FLAT_WIDTH, FLAT_HEIGHT));
        Owned.Add(window);

        return (window, items);
    }

    private static EquipmentSlotControl SlotOf(EquipmentWindowControl window, string id)
        => window.SlotControls.Single(slot => slot.Spec.Id == id);

    [Fact]
    public void EverySlotIsInsideTheContentAndClearOfTheModelPersonalViewAndEachOther()
    {
        var layout = MainHudLayout.EquipmentIn(FLAT_WIDTH, FLAT_HEIGHT);
        var content = new Rectangle(0, 0, FLAT_WIDTH, FLAT_HEIGHT);

        Assert.Equal(EquipmentSlots.All.Length, layout.Slots.Count);
        Assert.All(layout.Slots, slot => Assert.Equal(new Point(32, 32), slot.Size));
        Assert.All(layout.Slots, slot => Assert.True(content.Contains(slot), slot.ToString()));
        Assert.All(layout.Slots, slot => Assert.False(slot.Intersects(layout.Model), slot.ToString()));
        Assert.All(layout.Slots, slot => Assert.False(slot.Intersects(layout.Stats), slot.ToString()));
        Assert.True(content.Contains(layout.Model));
        Assert.True(content.Contains(layout.Stats));

        for (var i = 0; i < layout.Slots.Count; i++)
            for (var j = i + 1; j < layout.Slots.Count; j++)
                Assert.False(layout.Slots[i].Intersects(layout.Slots[j]), $"{EquipmentSlots.All[i].Id} and {EquipmentSlots.All[j].Id}");
    }

    //the wireframe: the model is about 99 by 167 between the columns, and Personal View takes the rest of the width
    [Fact]
    public void TheModelIsBetweenTheColumnsAndPersonalViewTakesTheRestOfTheWidth()
    {
        var layout = MainHudLayout.EquipmentIn(FLAT_WIDTH, FLAT_HEIGHT);

        Assert.Equal(new Rectangle(34, 34, 100, 168), layout.Model);
        Assert.Equal(FLAT_WIDTH, layout.Stats.Right);
        Assert.Equal(FLAT_HEIGHT, layout.Stats.Height);
        Assert.True(layout.Stats.Left > MainHudLayout.EQUIPMENT_SLOTS_WIDTH);

        //the left column, the model and the right column, in that order on each row of the model
        var armor = layout.Slots[Array.FindIndex(EquipmentSlots.All, spec => spec.Id == "armor")];
        var cape = layout.Slots[Array.FindIndex(EquipmentSlots.All, spec => spec.Id == "cape")];
        Assert.True(armor.Right <= layout.Model.Left);
        Assert.True(cape.Left >= layout.Model.Right);
        Assert.Equal(armor.Y, cape.Y);
    }

    //each slot in its cell of the wireframe: (column, row) at a 34 px pitch; the bottom row is 12 px lower
    [Theory]
    [InlineData("earrings", 1, 0)]
    [InlineData("helm", 2, 0)]
    [InlineData("necklace", 3, 0)]
    [InlineData("armor", 0, 1)]
    [InlineData("left_ring", 0, 2)]
    [InlineData("left_glove", 0, 3)]
    [InlineData("belt", 0, 4)]
    [InlineData("boots", 0, 5)]
    [InlineData("cape", 4, 1)]
    [InlineData("right_ring", 4, 2)]
    [InlineData("right_glove", 4, 3)]
    [InlineData("greaves", 4, 4)]
    [InlineData("mount", 4, 5)]
    [InlineData("weapon", 1, 6)]
    [InlineData("offhand", 2, 6)]
    [InlineData("trinket", 3, 6)]
    [InlineData("vanity_helm", 0, 7)]
    [InlineData("vanity_armor", 1, 7)]
    [InlineData("accessory_1", 2, 7)]
    [InlineData("accessory_2", 3, 7)]
    [InlineData("accessory_3", 4, 7)]
    public void EachSlotIsInItsCell(string id, int column, int row)
    {
        var slot = MainHudLayout.EquipmentIn(FLAT_WIDTH, FLAT_HEIGHT).Slots[Array.FindIndex(EquipmentSlots.All, spec => spec.Id == id)];

        Assert.Equal(column * 34, slot.X);
        Assert.Equal(row * 34 + (row == 7 ? 12 : 0), slot.Y);
    }

    [Fact]
    public void EveryWireSlotHasOneControlAndCapeMountAndTrinketHaveNone()
    {
        var wire = Enum.GetValues<EquipmentSlot>().Where(slot => slot != EquipmentSlot.None).ToArray();

        Assert.Equal(18, wire.Length);
        Assert.All(wire, slot => Assert.Single(EquipmentSlots.All, spec => spec.Wire == slot));
        Assert.Equal(["cape", "mount", "trinket"], EquipmentSlots.All.Where(spec => spec.Wire is null).Select(spec => spec.Id));
        Assert.Equal(EquipmentSlots.All.Length, EquipmentSlots.All.Select(spec => spec.Id).Distinct().Count());
    }

    [Fact]
    public void EverySlotHasANamedRoleAndBuiltInEmptySlotArt()
    {
        Assert.Equal(EquipmentSlots.All.Select(spec => spec.IconRole), ThemeRoles.EquipmentIcons);
        Assert.All(EquipmentSlots.All, spec => Assert.True(BuiltInArt.Has(spec.IconRole), spec.IconRole));
        UiAssert.NamedTree(NewWindow().Window);
    }

    //a struct value: an empty slot is null, not an empty slot data, so the unequip key sends only for an item
    [Fact]
    public void AnEmptyEquipmentSlotIsNull()
    {
        var items = new Equipment();
        items.SetSlot(EquipmentSlot.Weapon, 10, DisplayColor.Default, "Stick", 0, 0);

        Assert.NotNull(items.GetSlot(EquipmentSlot.Weapon));
        Assert.Null(items.GetSlot(EquipmentSlot.Shield));
    }

    [Fact]
    public void ASlotShowsItsItemAndShowsAgainAtAChangeAClearAndAClearOfAll()
    {
        var (window, items) = NewWindow();

        Assert.Equal(10, SlotOf(window, "weapon").Icon!.Value.OffsetX);
        Assert.Equal(20, SlotOf(window, "vanity_armor").Icon!.Value.OffsetX);
        Assert.Null(SlotOf(window, "helm").Icon);

        items.SetSlot(EquipmentSlot.Helmet, 30, DisplayColor.Default, "Cap", 0, 0);
        Assert.Equal(30, SlotOf(window, "helm").Icon!.Value.OffsetX);

        items.ClearSlot(EquipmentSlot.Weapon);
        Assert.Null(SlotOf(window, "weapon").Icon);

        items.Clear();
        Assert.All(window.SlotControls, slot => Assert.Null(slot.Icon));
    }

    [Fact]
    public void ADisabledSlotShowsNoItem()
    {
        var slot = new EquipmentSlotControl(EquipmentSlots.All.Single(spec => spec.Id == "cape"));
        Owned.Add(slot);

        slot.Show(new IconTexture(null!, 1, 1));

        Assert.False(slot.Usable);
        Assert.Null(slot.Icon);
    }

    [Fact]
    public void ADoubleClickOnAnItemTakesItOffAndASingleClickDoesNothing()
    {
        var (window, _) = NewWindow();
        var taken = new List<EquipmentSlot>();
        window.UnequipRequested += taken.Add;

        var click = new ClickEvent { Button = MouseButton.Left };
        SlotOf(window, "weapon").OnClick(click);
        Assert.True(click.Handled);
        Assert.Empty(taken);

        var doubleClick = new DoubleClickEvent { Button = MouseButton.Left };
        SlotOf(window, "weapon").OnDoubleClick(doubleClick);
        Assert.True(doubleClick.Handled);

        SlotOf(window, "helm").OnDoubleClick(new DoubleClickEvent { Button = MouseButton.Left });
        SlotOf(window, "cape").OnDoubleClick(new DoubleClickEvent { Button = MouseButton.Left });
        SlotOf(window, "vanity_armor").OnDoubleClick(new DoubleClickEvent { Button = MouseButton.Right });

        Assert.Equal([EquipmentSlot.Weapon], taken);
    }

    [Fact]
    public void ADragOfAnItemCarriesItsWireSlotAndAnEmptySlotStartsNoDrag()
    {
        var (window, _) = NewWindow();

        var drag = new DragStartEvent { Button = MouseButton.Left };
        SlotOf(window, "vanity_armor").OnDragStart(drag);
        Assert.Equal(EquipmentSlot.Overcoat, Assert.IsType<EquipmentDragPayload>(drag.Payload).Slot);

        var empty = new DragStartEvent { Button = MouseButton.Left };
        SlotOf(window, "helm").OnDragStart(empty);
        Assert.Null(empty.Payload);

        var right = new DragStartEvent { Button = MouseButton.Right };
        SlotOf(window, "weapon").OnDragStart(right);
        Assert.Null(right.Payload);
    }

    [Fact]
    public void ADropOfAnInventoryItemOnASlotEquipsItAndADisabledSlotTakesNothing()
    {
        var (window, _) = NewWindow();
        var equipped = new List<byte>();
        window.EquipRequested += equipped.Add;

        var drop = new DragDropEvent { Payload = new ItemDragPayload(7, null) };
        SlotOf(window, "helm").OnDragDrop(drop);
        Assert.True(drop.Handled);

        var disabled = new DragDropEvent { Payload = new ItemDragPayload(8, null) };
        SlotOf(window, "mount").OnDragDrop(disabled);
        Assert.True(disabled.Handled);

        //an equipped item dropped on its own window ends there, and an ability passes on
        var own = new DragDropEvent { Payload = new EquipmentDragPayload(EquipmentSlot.Weapon, null) };
        SlotOf(window, "helm").OnDragDrop(own);
        Assert.True(own.Handled);

        var ability = new DragDropEvent { Payload = new AbilityDragPayload(true, 3, null) };
        SlotOf(window, "helm").OnDragDrop(ability);
        Assert.False(ability.Handled);

        Assert.Equal([7], equipped);
    }

    [Fact]
    public void ADropOfAnEquippedItemOnTheInventoryWindowTakesItOff()
    {
        var inventory = new InventoryWindowControl(new Inventory(), new PlayerAttributes(), FakeIcon);
        inventory.SetBounds(new Rectangle(0, 0, FLAT_WIDTH, FLAT_HEIGHT));
        Owned.Add(inventory);
        var taken = new List<EquipmentSlot>();
        inventory.UnequipRequested += taken.Add;

        var drop = new DragDropEvent { Payload = new EquipmentDragPayload(EquipmentSlot.Helmet, null) };
        inventory.OnDragDrop(drop);

        var other = new DragDropEvent { Payload = new ItemDragPayload(3, null) };
        inventory.OnDragDrop(other);

        Assert.True(drop.Handled);
        Assert.False(other.Handled);
        Assert.Equal([EquipmentSlot.Helmet], taken);
    }

    //a slot of the Inventory window does not take an equipped item, so the drop reaches the window
    [Fact]
    public void AnInventorySlotLetsAnEquippedItemThroughToTheWindow()
    {
        var slot = new InventorySlotControl(1, true);
        Owned.Add(slot);
        var drop = new DragDropEvent { Payload = new EquipmentDragPayload(EquipmentSlot.Helmet, null) };

        slot.OnDragDrop(drop);

        Assert.False(drop.Handled);
    }

    //a drop on the frame or the title of the Inventory panel takes the item off, as a drop on its content does
    [Fact]
    public void ADropOnThePanelFrameGoesToItsContent()
    {
        var hud = NewHud();
        var panel = hud.Windows.PanelFor(HudWindows.Inventory);
        var inventory = new InventoryWindowControl(new Inventory(), new PlayerAttributes(), FakeIcon);
        panel.SetContent(inventory);
        var taken = new List<EquipmentSlot>();
        inventory.UnequipRequested += taken.Add;
        hud.Windows.Open(HudWindows.Inventory);

        var frame = new DragDropEvent { Payload = new EquipmentDragPayload(EquipmentSlot.Boots, null), ScreenX = panel.ScreenBounds.X + 2, ScreenY = panel.ScreenBounds.Y + 2 };
        panel.OnDragDrop(frame);

        //a drop that the content had first does not reach it a second time from the panel
        var inside = panel.ScreenBounds.Center;
        var onContent = new DragDropEvent { Payload = new EquipmentDragPayload(EquipmentSlot.Belt, null), ScreenX = inside.X, ScreenY = inside.Y };
        panel.OnDragDrop(onContent);

        Assert.True(frame.Handled);
        Assert.Equal([EquipmentSlot.Boots], taken);
    }

    [Fact]
    public void TheWindowRaisesThePinsOfPersonalViewAndShowsSavedPins()
    {
        var (window, _) = NewWindow();
        var saved = new List<IReadOnlyList<PersonalStat>>();
        window.PinsChanged += saved.Add;

        window.SetPins([PersonalStat.Dex]);
        window.PersonalView.RowControls.Single(row => row.Stat == PersonalStat.Hit).OnClick(new ClickEvent { Button = MouseButton.Right });

        Assert.Equal([[PersonalStat.Dex, PersonalStat.Hit]], saved);
    }

    [Fact]
    public void ARootDropOfAnEquippedItemEnds()
        => Assert.Equal(WorldScreen.RootDrop.Ends, WorldScreen.RootDropOf(new EquipmentDragPayload(EquipmentSlot.Weapon, null)));

    [Fact]
    public void TheHoverFollowsTheSlotUnderThePointerAndItsItem()
    {
        var (window, items) = NewWindow();
        var hovers = new List<string?>();
        window.HoverChanged += spec => hovers.Add(spec?.Id);

        SlotOf(window, "weapon").OnMouseEnter();
        items.SetSlot(EquipmentSlot.Weapon, 11, DisplayColor.Default, "Club", 0, 0);
        items.SetSlot(EquipmentSlot.Helmet, 30, DisplayColor.Default, "Cap", 0, 0);
        SlotOf(window, "helm").OnMouseEnter();
        SlotOf(window, "weapon").OnMouseLeave();
        SlotOf(window, "helm").OnMouseLeave();
        window.ClearHover();

        Assert.Equal(["weapon", "weapon", "helm", null], hovers);
    }

    [Fact]
    public void TheModelIsScaledTwiceAtTheCentreOfItsArea()
    {
        var area = new Rectangle(100, 50, 100, 168);
        var placement = CharacterModelControl.Placement(new Point(40, 60), area, area)!.Value;

        Assert.Equal(new Rectangle(0, 0, 40, 60), placement.Source);
        Assert.Equal(new Rectangle(110, 74, 80, 120), placement.Destination);
    }

    //a figure wider than the area is cut at the sides in whole texture px, so each px stays square
    [Fact]
    public void AWideModelIsClippedInWholeTexturePx()
    {
        var area = new Rectangle(0, 0, 100, 168);
        var placement = CharacterModelControl.Placement(new Point(61, 80), area, area)!.Value;

        Assert.True(area.Contains(placement.Destination));
        Assert.Equal(placement.Source.Width * CharacterModelControl.SCALE, placement.Destination.Width);
        Assert.Equal(placement.Source.Height * CharacterModelControl.SCALE, placement.Destination.Height);
        Assert.Equal(new Rectangle(6, 0, 49, 80), placement.Source);
    }

    [Fact]
    public void AModelOutsideTheClipShowsNothing()
        => Assert.Null(CharacterModelControl.Placement(new Point(40, 60), new Rectangle(0, 0, 100, 168), new Rectangle(500, 500, 10, 10)));

    private MainHudControl NewHud()
    {
        var proxy = DispatchProxy.Create<IWorldHud, MainHudControlTests.RecordingHud>();
        var hud = new MainHudControl(proxy, attributes: new PlayerAttributes(), chat: new Chat());
        Owned.Add(hud);

        return hud;
    }

    [Fact]
    public void ShiftGOpensAndClosesTheEquipmentWindow()
    {
        var hud = NewHud();
        bool Open() => hud.Windows.Panels.Single(panel => panel.Window == HudWindows.Equipment).Visible;

        hud.HandleTabActivation(HudTab.Stats, true);
        Assert.True(Open());

        hud.HandleTabActivation(HudTab.Stats, true);
        Assert.False(Open());
    }
}

/// <summary>BRIG-151: Personal View. The rows, their values, the pins and their save.</summary>
[Collection("FontEngine")]
public class PersonalViewTests : IDisposable
{
    private readonly List<IDisposable> Owned = [];

    public PersonalViewTests() => FontEngine.Initialize(0);

    public void Dispose()
    {
        foreach (var owned in Owned)
            owned.Dispose();
    }

    private static PlayerAttributes Attributes()
    {
        var attrs = new PlayerAttributes();

        attrs.Update(new AttributesPacket
        {
            Primary = new PrimaryAttributes
            {
                MaxHp = 12_345,
                MaxMp = 678,
                Str = 3,
                Int = 4,
                Wis = 5,
                Con = 6,
                Dex = 7,
                CurrentWeight = 42,
                MaxWeight = 120
            },
            Secondary = new SecondaryAttributes
            {
                Ac = -10,
                HitRating = 8,
                DmgRating = 9,
                MrRating = 3,
                OffensiveElement = (byte)Element.Fire,
                DefensiveElement = (byte)Element.Water
            }
        });

        return attrs;
    }

    private PersonalViewControl NewView(PlayerAttributes attrs)
    {
        var view = new PersonalViewControl(attrs);
        view.SetBounds(new Rectangle(0, 0, 122, 311));
        Owned.Add(view);

        return view;
    }

    [Fact]
    public void EachStatHasItsValue()
    {
        var attrs = Attributes();

        Assert.Equal("12,345", PersonalStats.ValueOf(PersonalStat.Health, attrs));
        Assert.Equal("678", PersonalStats.ValueOf(PersonalStat.Mana, attrs));
        Assert.Equal("3", PersonalStats.ValueOf(PersonalStat.Str, attrs));
        Assert.Equal("7", PersonalStats.ValueOf(PersonalStat.Dex, attrs));
        Assert.Equal("110", PersonalStats.ValueOf(PersonalStat.Armor, attrs));
        Assert.Equal("8", PersonalStats.ValueOf(PersonalStat.Hit, attrs));
        Assert.Equal("9", PersonalStats.ValueOf(PersonalStat.AttackPower, attrs));
        Assert.Equal("30", PersonalStats.ValueOf(PersonalStat.MagicResistance, attrs));
        Assert.Equal("Fire", PersonalStats.ValueOf(PersonalStat.OffenseElement, attrs));
        Assert.Equal("Water", PersonalStats.ValueOf(PersonalStat.DefenseElement, attrs));
    }

    //the weight is not a stat of Personal View (Sabrael, 2026-11-18); a saved pin of it is skipped
    //the player names of the stat list of the design, and Armor on the higher-is-better scale
    [Fact]
    public void TheLabelsAreThePlayerNames()
    {
        Assert.Equal("Strength", PersonalStats.LabelOf(PersonalStat.Str));
        Assert.Equal("Armor", PersonalStats.LabelOf(PersonalStat.Armor));
        Assert.Equal("Attack Power", PersonalStats.LabelOf(PersonalStat.AttackPower));
        Assert.Equal("Magic Resist", PersonalStats.LabelOf(PersonalStat.MagicResistance));
        Assert.Equal(PersonalStats.All.Length, PersonalStats.All.Select(PersonalStats.LabelOf).Distinct().Count());
    }

    [Fact]
    public void TheWeightIsNotAStat()
    {
        Assert.DoesNotContain(PersonalStats.All, stat => stat.ToString() == "Weight");
        Assert.Empty(PersonalStats.Parse(["Weight"]));
    }

    [Fact]
    public void ThePinnedStatsGoToTheTopInTheOrderOfPinning()
    {
        var order = PersonalStats.Order([PersonalStat.Armor, PersonalStat.Str]);

        Assert.Equal(PersonalStats.All.Length, order.Count);
        Assert.Equal([PersonalStat.Armor, PersonalStat.Str, PersonalStat.Health, PersonalStat.Mana, PersonalStat.Int], order.Take(5));
        Assert.Equal(PersonalStats.All.Length, order.Distinct().Count());
    }

    [Fact]
    public void AToggleUnpinsAPinnedStatAndPinsAnotherLast()
    {
        IReadOnlyList<PersonalStat> pins = [PersonalStat.Dex, PersonalStat.Hit];

        Assert.Equal([PersonalStat.Hit], PersonalStats.Toggle(pins, PersonalStat.Dex));
        Assert.Equal([PersonalStat.Dex, PersonalStat.Hit, PersonalStat.Mana], PersonalStats.Toggle(pins, PersonalStat.Mana));
    }

    [Fact]
    public void TheSavedPinsReadBackAndBadLinesAreSkipped()
    {
        IReadOnlyList<PersonalStat> pins = [PersonalStat.DefenseElement, PersonalStat.Armor];

        Assert.Equal(pins, PersonalStats.Parse(PersonalStats.Format(pins)));
        Assert.Equal([PersonalStat.Str, PersonalStat.Con], PersonalStats.Parse(["Str", "Nonsense", "3", " Con ", "Str", ""]));
    }

    [Fact]
    public void TheRowsShowTheStatsInOrderWithTheirValuesAndFollowTheAttributes()
    {
        var attrs = new PlayerAttributes();
        var view = NewView(attrs);

        Assert.Equal(PersonalStats.All, view.RowControls.Select(row => row.Stat));
        Assert.Equal("Health", view.RowControls[0].LabelShown);

        attrs.Update(new AttributesPacket { Primary = new PrimaryAttributes { MaxHp = 50, Str = 9 } });

        Assert.Equal("50", view.RowControls[0].ValueShown);
        Assert.Equal("9", view.RowControls.Single(row => row.Stat == PersonalStat.Str).ValueShown);
    }

    [Fact]
    public void ARightClickOrADoubleClickTogglesThePinAndRaisesTheChange()
    {
        var view = NewView(Attributes());
        var saved = new List<IReadOnlyList<PersonalStat>>();
        view.PinsChanged += saved.Add;

        var armor = view.RowControls.Single(row => row.Stat == PersonalStat.Armor);
        armor.OnClick(new ClickEvent { Button = MouseButton.Right });

        Assert.Equal(PersonalStat.Armor, view.RowControls[0].Stat);
        Assert.Equal("* Armor", view.RowControls[0].LabelShown);
        Assert.Equal("110", view.RowControls[0].ValueShown);

        //a left click alone does nothing; a double click unpins
        view.RowControls[0].OnClick(new ClickEvent { Button = MouseButton.Left });
        Assert.True(view.RowControls[0].Pinned);

        view.RowControls[0].OnDoubleClick(new DoubleClickEvent { Button = MouseButton.Left });
        Assert.Equal(PersonalStat.Health, view.RowControls[0].Stat);
        Assert.False(view.RowControls[0].Pinned);

        Assert.Equal([[PersonalStat.Armor], []], saved);
    }

    [Fact]
    public void SavedPinsShowWithNoChangeRaised()
    {
        var view = NewView(Attributes());
        var raised = false;
        view.PinsChanged += _ => raised = true;

        view.SetPins([PersonalStat.Dex, PersonalStat.Dex, PersonalStat.Mana]);

        Assert.Equal([PersonalStat.Dex, PersonalStat.Mana], view.Pins);
        Assert.Equal([PersonalStat.Dex, PersonalStat.Mana], view.RowControls.Take(2).Select(row => row.Stat));
        Assert.False(raised);
    }

    [Fact]
    public void EveryRowFitsTheHeightOfPersonalView()
        => Assert.True(PersonalStats.All.Length * PersonalViewControl.ROW_HEIGHT <= MainHudLayout.EquipmentIn(298, 311).Stats.Height);
}
