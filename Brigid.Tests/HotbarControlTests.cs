#region
using System.Reflection;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Main;
using Brigid.Definitions;
using Brigid.Models;
using Brigid.Rendering;
using Brigid.Rendering.Definitions;
using Brigid.Screens;
using Brigid.Systems.Keybinds;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-146 phase 2: the hotbar control. It shows the page of the model that shows, dims a gone entry and counts an
///     item; a drop from a window sets a slot, a drag between slots swaps them, and a drag out clears the slot; a click
///     uses an entry, a right click asks for the castable popup; the arrows and the wheel change the page.
/// </summary>
[Collection("FontEngine")]
public class HotbarControlTests : IDisposable
{
    private readonly List<IDisposable> Owned = [];
    private readonly SkillBook Skills = new();
    private readonly SpellBook Spells = new();
    private readonly Inventory Items = new();
    private readonly Hotbar Model;
    private readonly HudTooltip Tooltip = new();
    private object? ActiveDrag;

    //the tests have no graphics device: an icon with no texture stands for the icon of each entry
    private static readonly IconTexture FakeIcon = new(null!, 0, 0);

    private static readonly HotbarEntry Assail = new(HotbarKind.Skill, "Assail", 1);
    private static readonly HotbarEntry BeagIoc = new(HotbarKind.Spell, "beag ioc", 2);
    private static readonly HotbarEntry Potion = new(HotbarKind.Item, "Hemloch Potion", 10);

    public HotbarControlTests()
    {
        FontEngine.Initialize(0);
        Model = new Hotbar(Skills, Spells, Items, new Equipment(), () => 0);
        Owned.Add(Tooltip);
    }

    public void Dispose()
    {
        foreach (var owned in Owned)
            owned.Dispose();
    }

    private HotbarControl NewHotbar()
    {
        var hotbar = new HotbarControl(Model, Tooltip, (_, _) => FakeIcon, () => ActiveDrag);
        Owned.Add(hotbar);

        return hotbar;
    }

    private static void Update(UIElement element) => element.Update(new GameTime());

    private void AddPotions(byte slot, uint count) => Items.SetSlot(slot, 10, DisplayColor.Default, "Hemloch Potion", true, count, 0, 0);

    //a drag as the dispatcher runs it: the start gives the payload, which is the active drag until the drop
    private HotbarDragPayload StartDrag(HotbarSlotControl slot)
    {
        var start = new DragStartEvent { Button = MouseButton.Left };
        slot.OnDragStart(start);
        ActiveDrag = start.Payload;

        return Assert.IsType<HotbarDragPayload>(start.Payload);
    }

    private void Drop(HotbarSlotControl slot, object payload)
    {
        slot.OnDragDrop(new DragDropEvent { Payload = payload, Button = MouseButton.Left });
        ActiveDrag = null;
    }

    [Fact]
    public void TheSlotsAndThePageColumnFitTheHotbar()
    {
        var hotbar = NewHotbar();
        var area = new Rectangle(0, 0, MainHudLayout.Hotbar.Width, MainHudLayout.Hotbar.Height);
        List<Rectangle> parts = [MainHudLayout.HotbarPageUp, MainHudLayout.HotbarPageNumber, MainHudLayout.HotbarPageDown];
        parts.AddRange(Enumerable.Range(0, Hotbar.SLOTS).Select(MainHudLayout.HotbarSlot));

        Assert.Equal(MainHudLayout.Hotbar, hotbar.Bounds);
        Assert.All(parts, part => Assert.True(area.Contains(part), part.ToString()));

        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
                Assert.False(parts[i].Intersects(parts[j]), $"{parts[i]} {parts[j]}");

        Assert.All(Enumerable.Range(0, Hotbar.SLOTS), i => Assert.Equal(new Point(MainHudLayout.HOTBAR_SLOT, MainHudLayout.HOTBAR_SLOT), MainHudLayout.HotbarSlot(i).Size));
        Assert.Equal(HotbarControl.KeyLabels, Enumerable.Range(0, Hotbar.SLOTS).Select(i => hotbar.SlotAt(i).KeyShown));
        UiAssert.NamedTree(hotbar);
    }

    [Fact]
    public void TheHotbarShowsThePageThatShows()
    {
        var hotbar = NewHotbar();
        Model.Set(0, 0, Assail);
        Model.Set(2, 0, BeagIoc);
        Update(hotbar);

        Assert.Equal(Assail, hotbar.SlotAt(0).Entry);
        Assert.Null(hotbar.SlotAt(1).Entry);
        Assert.Equal("1", hotbar.PageShown);

        Model.SetPage(2);
        Update(hotbar);

        Assert.Equal(BeagIoc, hotbar.SlotAt(0).Entry);
        Assert.Equal("3", hotbar.PageShown);
    }

    [Fact]
    public void AGoneEntryShowsDimmedAndAnItemShowsItsCount()
    {
        var hotbar = NewHotbar();
        Skills.SetSlot(3, 1, "Assail");
        AddPotions(4, 3);
        AddPotions(5, 2);
        Model.Set(0, 0, Assail);
        Model.Set(0, 1, Potion);
        Model.Set(0, 2, BeagIoc);
        Update(hotbar);

        Assert.False(hotbar.SlotAt(0).ShowsDimmed);
        Assert.False(hotbar.SlotAt(1).ShowsDimmed);
        Assert.Equal("5", hotbar.SlotAt(1).CountShown);
        Assert.True(hotbar.SlotAt(2).ShowsDimmed);
        Assert.Equal(string.Empty, hotbar.SlotAt(0).CountShown);

        Items.ClearSlot(4);
        Items.ClearSlot(5);
        Update(hotbar);

        Assert.True(hotbar.SlotAt(1).ShowsDimmed);
        Assert.Equal(string.Empty, hotbar.SlotAt(1).CountShown);
        Assert.False(hotbar.SlotAt(5).ShowsDimmed);

        //one item shows no count, as an inventory slot of one
        AddPotions(4, 1);
        Update(hotbar);
        Assert.False(hotbar.SlotAt(1).ShowsDimmed);
        Assert.Equal(string.Empty, hotbar.SlotAt(1).CountShown);
    }

    [Fact]
    public void ADropFromAWindowSetsTheSlot()
    {
        var hotbar = NewHotbar();
        Skills.SetSlot(3, 1, "Assail");
        Spells.SetSlot(5, 2, "beag ioc (Lev:1/50)");
        AddPotions(7, 4);
        Model.SetPage(4);

        Drop(hotbar.SlotAt(0), new AbilityDragPayload(false, 3, null));
        Drop(hotbar.SlotAt(1), new AbilityDragPayload(true, 5, null));
        Drop(hotbar.SlotAt(2), new ItemDragPayload(7, null));

        Assert.Equal(Assail, Model.Get(4, 0));
        Assert.Equal(BeagIoc, Model.Get(4, 1));
        Assert.Equal(Potion, Model.Get(4, 2));

        //a drop on a filled slot replaces its entry; an empty book slot sets nothing
        Drop(hotbar.SlotAt(0), new AbilityDragPayload(true, 5, null));
        Drop(hotbar.SlotAt(1), new AbilityDragPayload(false, 9, null));

        Assert.Equal(BeagIoc, Model.Get(4, 0));
        Assert.Equal(BeagIoc, Model.Get(4, 1));
    }

    [Fact]
    public void ADragToAnotherSlotSwapsTheTwo()
    {
        var hotbar = NewHotbar();
        Model.Set(0, 0, Assail);
        Model.Set(0, 5, Potion);
        Update(hotbar);

        var payload = StartDrag(hotbar.SlotAt(0));
        Drop(hotbar.SlotAt(5), payload);
        Update(hotbar);

        Assert.Equal(Potion, Model.Get(0, 0));
        Assert.Equal(Assail, Model.Get(0, 5));
    }

    //the wheel can turn the page during a drag: the drag keeps the page of its source
    [Fact]
    public void ADragCanEndOnAnotherPage()
    {
        var hotbar = NewHotbar();
        Model.Set(2, 0, Assail);
        Model.SetPage(2);
        Update(hotbar);

        var payload = StartDrag(hotbar.SlotAt(0));
        Assert.Equal((2, 0), (payload.Page, payload.Index));
        hotbar.OnMouseScroll(new MouseScrollEvent { Delta = 1 });
        Drop(hotbar.SlotAt(3), payload);
        Update(hotbar);

        Assert.Null(Model.Get(2, 0));
        Assert.Equal(Assail, Model.Get(3, 3));
    }

    //Q4: a drag out of the hotbar clears the slot and does nothing more
    [Fact]
    public void ADragThatEndsOutsideTheHotbarClearsTheSlot()
    {
        var hotbar = NewHotbar();
        Model.Set(0, 0, Assail);
        Update(hotbar);

        StartDrag(hotbar.SlotAt(0));
        Update(hotbar);
        Assert.Equal(Assail, Model.Get(0, 0));

        ActiveDrag = null;
        Update(hotbar);

        Assert.Null(Model.Get(0, 0));
    }

    [Fact]
    public void ARootDropOfAHotbarSlotEnds()
        => Assert.Equal(WorldScreen.RootDrop.Ends, WorldScreen.RootDropOf(new HotbarDragPayload(0, 0, null)));

    //BRIG-149: a skill from the Abilities window that reaches the root ends; only a spell is cast at the entity under it
    [Fact]
    public void ARootDropOfASkillEndsAndOfASpellCasts()
    {
        Assert.Equal(WorldScreen.RootDrop.Ends, WorldScreen.RootDropOf(new AbilityDragPayload(false, 3, null)));
        Assert.Equal(WorldScreen.RootDrop.Spell, WorldScreen.RootDropOf(new AbilityDragPayload(true, 3, null)));
    }

    [Theory]
    [InlineData(ClientEntityType.Aisling, true)]
    [InlineData(ClientEntityType.Creature, true)]
    [InlineData(ClientEntityType.GroundItem, false)]
    [InlineData(null, false)]
    public void ADroppedSpellIsCastOnlyAtAnAislingOrACreature(ClientEntityType? type, bool expected)
        => Assert.Equal(expected, WorldScreen.IsSpellDropTarget(type));

    //a spell dropped in a gap of the hotbar reached the root, which cast it at an entity whose hit box reached under the
    //bar; the root takes a spell or an item only in the world viewport
    [Fact]
    public void AGapOfTheHotbarIsNotInTheViewport()
    {
        var world = MainHudLayout.World;
        var hotbar = MainHudLayout.Hotbar;

        Assert.False(WorldScreen.IsInViewport(world, hotbar.Center.X, hotbar.Center.Y));
        Assert.False(WorldScreen.IsInViewport(world, world.X, world.Bottom));
        Assert.True(WorldScreen.IsInViewport(world, world.X, world.Bottom - 1));
        Assert.True(WorldScreen.IsInViewport(world, world.Center.X, world.Center.Y));
    }

    //a near miss: a drop in a gap between the slots or on the page column keeps the slot
    [Fact]
    public void ADropOnTheHotbarButNotOnASlotKeepsTheSlot()
    {
        var hotbar = NewHotbar();
        Model.Set(0, 0, Assail);
        Update(hotbar);

        var payload = StartDrag(hotbar.SlotAt(0));
        hotbar.OnDragDrop(new DragDropEvent { Payload = payload, Button = MouseButton.Left });
        ActiveDrag = null;
        Update(hotbar);

        Assert.Equal(Assail, Model.Get(0, 0));
    }

    [Fact]
    public void ADropOnItsOwnSlotKeepsTheEntry()
    {
        var hotbar = NewHotbar();
        Model.Set(0, 4, Assail);
        Update(hotbar);

        var payload = StartDrag(hotbar.SlotAt(4));
        Drop(hotbar.SlotAt(4), payload);
        Update(hotbar);

        Assert.Equal(Assail, Model.Get(0, 4));
    }

    //a drop of another payload (the gold bag, an equipped item) on a slot changes nothing, and does not reach the world
    [Fact]
    public void ADropOfAnotherPayloadChangesNothing()
    {
        var hotbar = NewHotbar();
        Model.Set(0, 0, Assail);
        Update(hotbar);
        var drop = new DragDropEvent { Payload = new GoldDragPayload(), Button = MouseButton.Left };

        hotbar.SlotAt(0).OnDragDrop(drop);

        Assert.True(drop.Handled);
        Assert.Equal(Assail, Model.Get(0, 0));
    }

    //the wheel changes the page under the pointer: the tooltip follows the entry of the slot, and an empty slot shows none
    [Fact]
    public void TheTooltipFollowsTheEntryUnderThePointer()
    {
        var hotbar = NewHotbar();
        Model.Set(0, 3, Assail);
        Model.Set(2, 3, BeagIoc);
        Update(hotbar);

        hotbar.SlotAt(3).OnMouseEnter();
        Assert.True(Tooltip.Visible);

        hotbar.OnMouseScroll(new MouseScrollEvent { Delta = 1 });
        Update(hotbar);
        Assert.False(Tooltip.Visible);

        hotbar.OnMouseScroll(new MouseScrollEvent { Delta = 1 });
        Update(hotbar);
        Update(Tooltip);
        Assert.True(Tooltip.Visible);
        Assert.Contains("beag ioc", Tooltip.WrappedText);

        //a click on an empty slot does not reach the world under the hotbar
        var click = new ClickEvent { Button = MouseButton.Left };
        hotbar.SlotAt(0).OnClick(click);
        Assert.True(click.Handled);
    }

    //a drop that a modal blocks ends the drag with a cancel: the slot keeps its entry
    [Fact]
    public void ACancelledDragKeepsTheSlot()
    {
        var hotbar = NewHotbar();
        Model.Set(0, 0, Assail);
        Update(hotbar);

        StartDrag(hotbar.SlotAt(0));
        hotbar.SlotAt(0).OnDragCancel();
        ActiveDrag = null;
        Update(hotbar);

        Assert.Equal(Assail, Model.Get(0, 0));
    }

    [Fact]
    public void AClickUsesAnEntryAndARightClickOnAnAbilityAsksForItsPopup()
    {
        var hotbar = NewHotbar();
        Model.Set(0, 0, Assail);
        Model.Set(0, 1, Potion);
        Update(hotbar);
        List<HotbarEntry> used = [];
        List<HotbarEntry> details = [];
        hotbar.UseRequested += used.Add;
        hotbar.DetailsRequested += details.Add;

        hotbar.SlotAt(0).OnClick(new ClickEvent { Button = MouseButton.Left });
        hotbar.SlotAt(1).OnClick(new ClickEvent { Button = MouseButton.Left });
        hotbar.SlotAt(2).OnClick(new ClickEvent { Button = MouseButton.Left });
        hotbar.SlotAt(0).OnClick(new ClickEvent { Button = MouseButton.Right });
        hotbar.SlotAt(1).OnClick(new ClickEvent { Button = MouseButton.Right });

        Assert.Equal([Assail, Potion], used);
        Assert.Equal([Assail], details);

        //the number keys use the same path
        hotbar.Use(1);
        hotbar.Use(2);
        Assert.Equal([Assail, Potion, Potion], used);
    }

    //BRIG-146 phase 3: on the new HUD a number key uses the entry of its slot on the page that shows
    [Fact]
    public void ANumberKeyUsesItsSlotOnThePageThatShows()
    {
        var hotbar = NewHotbar();
        Model.Set(0, 0, Assail);
        Model.Set(0, 11, Potion);
        Model.Set(3, 9, BeagIoc);
        List<HotbarEntry> used = [];
        hotbar.UseRequested += used.Add;

        hotbar.Use(WorldScreen.SlotHotkeyOf(new KeyDownEvent { Key = Keys.D1 }, true)!.Value);
        hotbar.Use(WorldScreen.SlotHotkeyOf(new KeyDownEvent { Key = Keys.OemPlus }, true)!.Value);

        //an empty slot takes the key and does nothing
        hotbar.Use(WorldScreen.SlotHotkeyOf(new KeyDownEvent { Key = Keys.D5 }, true)!.Value);

        Model.SetPage(3);
        hotbar.Use(WorldScreen.SlotHotkeyOf(new KeyDownEvent { Key = Keys.D0 }, true)!.Value);

        Assert.Equal([Assail, Potion, BeagIoc], used);
    }

    //Ctrl and Alt are emotes, Shift+1 is Shout, Cmd is the macOS primary modifier, and Shift is free for the page keys
    [Theory]
    [InlineData(KeyModifiers.Shift)]
    [InlineData(KeyModifiers.Ctrl)]
    [InlineData(KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Meta)]
    public void AKeyWithAModifierIsNotAHotbarKey(KeyModifiers modifiers)
    {
        Assert.Null(WorldScreen.SlotHotkeyOf(new KeyDownEvent { Key = Keys.D2, Modifiers = modifiers }, true));

        //the classic HUDs keep their behaviour: a number key with any modifier uses the slot of the panel that shows
        Assert.Equal(1, WorldScreen.SlotHotkeyOf(new KeyDownEvent { Key = Keys.D2, Modifiers = modifiers }, false));
    }

    [Fact]
    public void AKeyThatIsNotInTheNumberRowIsNotAHotbarKey()
    {
        Assert.Null(WorldScreen.SlotHotkeyOf(new KeyDownEvent { Key = Keys.Q }, true));
        Assert.Null(WorldScreen.SlotHotkeyOf(new KeyDownEvent { Key = Keys.NumPad1 }, true));
        Assert.Null(WorldScreen.SlotHotkeyOf(new KeyDownEvent { Key = Keys.Q }, false));
        Assert.Equal(-1, Keybinds.NumberRowIndexOf(Keys.F1));
    }

    //one table of the number keys: the hotbar, its labels, the classic panels (slot = index + 1) and the emotes
    [Fact]
    public void TheNumberRowIsTheOneKeyTable()
    {
        Assert.Equal(Hotbar.SLOTS, Keybinds.NumberRowKeys.Length);
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "="], HotbarControl.KeyLabels);
        Assert.Equal(Enumerable.Range(0, Hotbar.SLOTS), Keybinds.NumberRowKeys.Select(key => WorldScreen.SlotHotkeyOf(new KeyDownEvent { Key = key }, true)!.Value));
        Assert.Equal(Enumerable.Range(0, Hotbar.SLOTS), Keybinds.NumberRowKeys.Select(key => WorldScreen.SlotHotkeyOf(new KeyDownEvent { Key = key }, false)!.Value));
        Assert.Equal(9, Keybinds.NumberRowIndexOf(Keys.D0));
        Assert.Equal(10, Keybinds.NumberRowIndexOf(Keys.OemMinus));
    }

    [Fact]
    public void TheArrowsAndTheWheelChangeThePage()
    {
        var hotbar = NewHotbar();

        hotbar.FindChild<IconButton>("page_down")!.OnClick(new ClickEvent { Button = MouseButton.Left });
        Assert.Equal(Hotbar.PAGES - 1, Model.Page);

        hotbar.FindChild<IconButton>("page_up")!.OnClick(new ClickEvent { Button = MouseButton.Left });
        Assert.Equal(0, Model.Page);

        hotbar.OnMouseScroll(new MouseScrollEvent { Delta = 1 });
        Assert.Equal(1, Model.Page);

        hotbar.OnMouseScroll(new MouseScrollEvent { Delta = -1 });
        hotbar.OnMouseScroll(new MouseScrollEvent { Delta = -1 });
        Assert.Equal(Hotbar.PAGES - 1, Model.Page);
    }

    [Fact]
    public void AHoverShowsTheNameAndTheKey()
    {
        var hotbar = NewHotbar();
        Model.Set(0, 10, Potion);
        Update(hotbar);
        var slot = hotbar.SlotAt(10);

        slot.OnMouseEnter();
        Update(Tooltip);

        Assert.True(Tooltip.Visible);
        Assert.Contains("Hemloch Potion", Tooltip.WrappedText);
        Assert.Contains("None in the inventory", Tooltip.WrappedText);
        Assert.Contains("Key: -", Tooltip.WrappedText);

        slot.OnMouseLeave();
        Assert.False(Tooltip.Visible);

        //an empty slot shows no tooltip
        hotbar.SlotAt(0).OnMouseEnter();
        Assert.False(Tooltip.Visible);
    }

    //the icon is read once for each entry and icon, not at each update: a lookup that finds no icon is not cached
    [Fact]
    public void TheIconIsReadAgainOnlyWhenItChanges()
    {
        List<HotbarEntry> reads = [];
        var hotbar = new HotbarControl(Model, Tooltip, (entry, _) => { reads.Add(entry); return FakeIcon; }, () => ActiveDrag);
        Owned.Add(hotbar);
        Model.Set(0, 0, Assail);
        Update(hotbar);
        Update(hotbar);

        Assert.Equal([Assail], reads);

        Model.Set(0, 0, BeagIoc);
        Update(hotbar);
        Model.SetPage(1);
        Update(hotbar);
        Model.SetPage(0);
        Update(hotbar);

        Assert.Equal([Assail, BeagIoc, BeagIoc], reads);
        Assert.Null(hotbar.SlotAt(1).Icon);
    }

    [Fact]
    public void ThePageArrowsHaveBuiltInArt()
    {
        Assert.All(ThemeRoles.HotbarIcons, role => Assert.True(BuiltInArt.Has(role), role));
        Assert.Equal(new[] { ThemeRoles.IconHotbarPageUp, ThemeRoles.IconHotbarPageDown }, ThemeRoles.HotbarIcons);
    }

    [Fact]
    public void TheHudHasTheHotbar()
    {
        var hud = new MainHudControl(DispatchProxy.Create<IWorldHud, MainHudControlTests.RecordingHud>(), attributes: new PlayerAttributes(), chat: new Chat(), hotbar: Model);
        Owned.Add(hud);

        Assert.Same(hud.HotbarStrip, hud.FindChild<HotbarControl>("hotbar"));
        Assert.Equal(MainHudLayout.Hotbar, hud.HotbarStrip.Bounds);
    }
}
