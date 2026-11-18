#region
using System.Reflection;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Main;
using Brigid.Definitions;
using Brigid.Rendering;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-144 phase 3: the window buttons of the new HUD (BRIG-127 D3, D18, D29, D30, D40). The row is built from one
///     list; a window that a later card makes opens as an empty panel; the other windows run the action that the world
///     screen binds; Group toggles group open on a click and opens the group window on a right click; Escape closes the
///     panels first.
/// </summary>
[Collection("FontEngine")]
public class HudWindowButtonsTests : IDisposable
{
    private readonly List<MainHudControl> Huds = [];

    public HudWindowButtonsTests() => FontEngine.Initialize(0);

    public void Dispose()
    {
        foreach (var hud in Huds)
            hud.Dispose();
    }

    private MainHudControl NewHud()
    {
        var hud = new MainHudControl(DispatchProxy.Create<IWorldHud, MainHudControlTests.RecordingHud>(), attributes: new PlayerAttributes(), chat: new Chat());
        Huds.Add(hud);

        return hud;
    }

    private static UIPanel Row(MainHudControl hud) => hud.FindChild<UIPanel>("window_buttons")!;

    private static IconButton Button(MainHudControl hud, string id) => Row(hud).FindChild<IconButton>(id)!;

    private static void Click(UIElement element, MouseButton button = MouseButton.Left) => element.OnClick(new ClickEvent { Button = button });

    [Fact]
    public void TheRowHasTheWindowsOfTheListInOrder()
    {
        var hud = NewHud();

        Assert.Equal(
            ["abilities", "inventory", "equipment", "mastery", "quests", "menu", "mail", "community", "map", "group", "screenshot"],
            Row(hud).Children.OfType<IconButton>().Select(button => button.Name));
        Assert.Equal(HudWindows.All.Select(window => window.Id), Row(hud).Children.OfType<IconButton>().Select(button => button.Name));
        UiAssert.NamedTree(Row(hud));
    }

    [Fact]
    public void EachButtonHasItsIconAndBuiltInArt()
    {
        var hud = NewHud();

        Assert.All(HudWindows.All, window => Assert.Equal($"icon.window.{window.Id}", Button(hud, window.Id).Role));
        Assert.All(HudWindows.All, window => Assert.True(BuiltInArt.Has(window.IconRole), window.IconRole));
        Assert.Equal(HudWindows.All.Select(window => window.IconRole), ThemeRoles.WindowIcons);
    }

    [Fact]
    public void AButtonOpensAndClosesItsPanel()
    {
        var hud = NewHud();
        var panel = hud.FindChild<HudPanel>("inventory_panel")!;

        Click(Button(hud, "inventory"));

        Assert.True(panel.Visible);
        Assert.Equal(MainHudLayout.CompactRight, panel.Bounds);
        Assert.True(Button(hud, "inventory").IsSelected);

        Click(Button(hud, "inventory"));

        Assert.False(panel.Visible);
        Assert.False(Button(hud, "inventory").IsSelected);
    }

    [Fact]
    public void TheHostPlacesThePanelsThatAreOpen()
    {
        var hud = NewHud();

        Click(Button(hud, "inventory"));
        Click(Button(hud, "abilities"));

        Assert.Equal(MainHudLayout.CompactLeft, hud.FindChild<HudPanel>("abilities_panel")!.Bounds);

        Click(Button(hud, "mastery"));

        Assert.False(hud.FindChild<HudPanel>("inventory_panel")!.Visible);
        Assert.False(hud.FindChild<HudPanel>("abilities_panel")!.Visible);
        Assert.Equal(MainHudLayout.LargePanel, hud.FindChild<HudPanel>("mastery_panel")!.Bounds);
    }

    [Fact]
    public void TheXOfAPanelClosesIt()
    {
        var hud = NewHud();
        Click(Button(hud, "quests"));
        var panel = hud.FindChild<HudPanel>("quests_panel")!;

        panel.FindChild<TextButton>("close")!.PerformClick();

        Assert.False(panel.Visible);
        Assert.False(Button(hud, "quests").IsSelected);
    }

    [Fact]
    public void EscapeClosesThePanelsFirst()
    {
        var hud = NewHud();

        Assert.False(hud.ClosePanels());

        Click(Button(hud, "inventory"));

        Assert.True(hud.ClosePanels());
        Assert.False(hud.FindChild<HudPanel>("inventory_panel")!.Visible);
    }

    [Fact]
    public void AnOpenPanelCoversTheWorldUnderIt()
    {
        var hud = NewHud();
        var middleOfRight = MainHudLayout.CompactRight.Center;

        Assert.False(hud.IsOverPanel(middleOfRight.X, middleOfRight.Y));

        Click(Button(hud, "inventory"));

        Assert.True(hud.IsOverPanel(middleOfRight.X, middleOfRight.Y));
        Assert.False(hud.IsOverPanel(MainHudLayout.CompactLeft.Center.X, MainHudLayout.CompactLeft.Center.Y));
    }

    [Fact]
    public void ABoundWindowRunsItsActionAndShowsItsState()
    {
        var hud = NewHud();
        var open = false;
        hud.Windows.BindWindow(HudWindows.Menu, () => open = !open, () => open);

        Click(Button(hud, "menu"));
        hud.Update(new GameTime());

        Assert.True(open);
        Assert.True(Button(hud, "menu").IsSelected);

        Click(Button(hud, "menu"));
        hud.Update(new GameTime());

        Assert.False(Button(hud, "menu").IsSelected);
    }

    [Fact]
    public void AnUnboundWindowDoesNothing()
    {
        var hud = NewHud();

        Click(Button(hud, "map"));

        Assert.False(Button(hud, "map").IsSelected);
    }

    [Fact]
    public void GroupTogglesOnAClickAndOpensTheWindowOnARightClick()
    {
        var hud = NewHud();
        var groupOpen = false;
        var windows = 0;
        hud.Windows.BindWindow(
            HudWindows.Group,
            () => groupOpen = !groupOpen,
            () => groupOpen,
            () => windows++,
            () => [TooltipLine.Themed(groupOpen ? "Group: open" : "Group: closed", ThemeRoles.Text)]);

        Click(Button(hud, "group"));
        Click(Button(hud, "group"), MouseButton.Right);
        hud.Update(new GameTime());

        Assert.Equal((true, 1), (groupOpen, windows));
        Assert.True(Button(hud, "group").IsSelected);

        //the lines of the binding come under the name
        Button(hud, "group").OnMouseEnter();
        Assert.Contains("Group: open", hud.FindChild<HudTooltip>("hud_tooltip")!.WrappedText);
    }

    [Fact]
    public void ABindingMustNameAWindowOfItsKind()
    {
        var hud = NewHud();

        Assert.Throws<ArgumentException>(() => hud.Windows.BindWindow(new HudWindow("nope", "Nope"), () => { }));
        Assert.Throws<ArgumentException>(() => hud.Windows.BindWindow(HudWindows.Inventory, () => { }));
        Assert.Throws<ArgumentException>(() => hud.Windows.BindPanel(HudWindows.Menu, new UIPanel()));
        Assert.Throws<ArgumentException>(() => hud.Windows.Open(HudWindows.Map));
    }

    //a window card puts its content in the panel, and runs its requests when the panel opens and closes
    [Fact]
    public void AWindowCardPutsItsContentInThePanel()
    {
        var hud = NewHud();
        var content = new UIPanel { Name = "inventory_grid" };
        var events = new List<string>();
        hud.Windows.BindPanel(HudWindows.Inventory, content, () => events.Add("opened"), () => events.Add("closed"));
        var panel = hud.FindChild<HudPanel>("inventory_panel")!;

        Assert.Null(panel.FindChild<UILabel>("not_built"));
        Assert.Same(content, panel.FindChild<UIPanel>("inventory_grid"));
        Assert.Equal(panel.ContentBounds, content.Bounds);

        Click(Button(hud, "inventory"));
        Click(Button(hud, "inventory"));

        Assert.Equal(["opened", "closed"], events);
    }

    //a key reaches a window through the same call as its button; a server reply opens it and keeps it open
    [Fact]
    public void AKeyAndAReplyReachThePanelThroughTheHost()
    {
        var hud = NewHud();
        var panel = hud.FindChild<HudPanel>("quests_panel")!;

        hud.Windows.Toggle(HudWindows.Quests);
        Assert.True(panel.Visible);

        hud.Windows.Open(HudWindows.Quests);
        Assert.True(panel.Visible);

        hud.Windows.Close(HudWindows.Quests);
        Assert.False(panel.Visible);
        Assert.False(Button(hud, "quests").IsSelected);
    }

    [Fact]
    public void ADisabledButtonDoesNothing()
    {
        var hud = NewHud();
        var button = Button(hud, "inventory");
        button.Enabled = false;

        Click(button);

        Assert.False(hud.FindChild<HudPanel>("inventory_panel")!.Visible);
    }

    //a popup in the world area (the pause menu and the Aisling list are -2) floats above the panels
    [Fact]
    public void TheHudIsUnderThePopups() => Assert.True(MainHudControl.LAYER < -2);

    [Fact]
    public void ARightClickOnAnotherButtonDoesNothing()
    {
        var hud = NewHud();

        Click(Button(hud, "inventory"), MouseButton.Right);

        Assert.False(hud.FindChild<HudPanel>("inventory_panel")!.Visible);
    }

    [Fact]
    public void AHoverShowsTheNameAndTheKeyOverTheButton()
    {
        var hud = NewHud();
        var tooltip = hud.FindChild<HudTooltip>("hud_tooltip")!;
        var menu = Button(hud, "menu");

        menu.OnMouseEnter();

        Assert.True(tooltip.Visible);
        Assert.Contains("Menu", tooltip.WrappedText);
        Assert.Contains("Key: Q", tooltip.WrappedText);
        Assert.True(tooltip.Y + tooltip.Height <= menu.ScreenY);

        menu.OnMouseLeave();

        Assert.False(tooltip.Visible);
    }

    [Fact]
    public void AnEmptyPanelSaysThatItsWindowIsNotBuilt()
    {
        var hud = NewHud();
        var panel = hud.FindChild<HudPanel>("abilities_panel")!;

        Assert.Equal("Abilities", panel.FindChild<UILabel>("title")!.Text);
        Assert.NotNull(panel.FindChild<UILabel>("not_built"));
        UiAssert.NamedTree(panel);
    }
}
