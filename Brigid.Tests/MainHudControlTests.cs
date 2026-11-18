#region
using System.Reflection;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Main;
using Brigid.Definitions;
using Brigid.Rendering;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-144: the shell of the new HUD. Every child has a name, the world area passes the mouse to the world, the
///     parts that later cards add have their places, and the legacy members of <see cref="IWorldHud" /> come from the
///     classic HUD (BRIG-127 D41), so the code that uses them keeps working while nothing of them shows.
/// </summary>
[Collection("FontEngine")]
public class MainHudControlTests : IDisposable
{
    public MainHudControlTests() => FontEngine.Initialize(0);

    //a stand-in for the classic HUD that records each member the main HUD asks for
    public class RecordingHud : DispatchProxy
    {
        public List<string> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);

            var type = targetMethod.ReturnType;

            return type.IsValueType && (type != typeof(void)) ? Activator.CreateInstance(type) : null;
        }
    }

    private readonly List<MainHudControl> Huds = [];

    public void Dispose()
    {
        foreach (var hud in Huds)
            hud.Dispose();
    }

    private (MainHudControl Hud, RecordingHud Classic) NewHud()
    {
        var classic = DispatchProxy.Create<IWorldHud, RecordingHud>();
        var hud = new MainHudControl(classic, attributes: new PlayerAttributes(), chat: new Chat());
        Huds.Add(hud);

        return (hud, (RecordingHud)(object)classic);
    }

    [Fact]
    public void EveryChildHasAUniqueName()
    {
        var (hud, _) = NewHud();

        Assert.Equal("main_hud", hud.Name);
        UiAssert.NamedTree(hud);
    }

    [Fact]
    public void TheWorldAreaIsTheViewportAndPassesTheMouseOn()
    {
        var (hud, _) = NewHud();

        Assert.Equal(MainHudLayout.World, hud.ViewportBounds);
        Assert.True(hud.IsPassThrough);
        Assert.True(hud.IsThemed);
    }

    [Theory]
    [InlineData("toast")]
    [InlineData("effect_bar")]
    [InlineData("health_bar")]
    [InlineData("mana_bar")]
    [InlineData("hotbar")]
    [InlineData("player_plate")]
    [InlineData("experience_bar")]
    public void EachLaterPartHasAPlaceThatDoesNotTakeTheMouse(string name)
    {
        var (hud, _) = NewHud();
        var place = hud.Children.Single(child => child.Name == name);

        Assert.False(place.IsHitTestVisible);
    }

    [Fact]
    public void TheBarsTakeTheMouse()
    {
        var (hud, _) = NewHud();

        foreach (var name in new[] { "top_bar", "bottom_bar", "left_edge", "right_edge" })
            Assert.True(hud.Children.Single(child => child.Name == name).IsHitTestVisible, name);
    }

    //the four surfaces each draw a theme part, so a theme can draw one frame around the world
    [Fact]
    public void TheSurfacesAroundTheWorldAreThemeParts()
    {
        var (hud, _) = NewHud();

        string RoleOf(string name) => Assert.IsAssignableFrom<HudBarPanel>(hud.Children.Single(child => child.Name == name)).Role;

        Assert.Equal(ThemeRoles.HudTopBar, RoleOf("top_bar"));
        Assert.Equal(ThemeRoles.HudBottomBar, RoleOf("bottom_bar"));
        Assert.Equal(ThemeRoles.HudSideEdge, RoleOf("left_edge"));
        Assert.Equal(ThemeRoles.HudSideEdge, RoleOf("right_edge"));
        Assert.Equal(
            [
                ThemeRoles.HudTopBar, ThemeRoles.HudBottomBar, ThemeRoles.HudSideEdge, ThemeRoles.HudBox, ThemeRoles.HudChatBox,
                ThemeRoles.HudChatScrollTrack, ThemeRoles.HudChatScrollThumb
            ],
            ThemeRoles.HudSurfaces);
    }

    [Fact]
    public void TheLegacyMembersComeFromTheClassicHud()
    {
        var (hud, classic) = NewHud();

        _ = hud.Inventory;
        _ = hud.ChatInput;
        _ = hud.ActiveTab;
        hud.HandleTabActivation(HudTab.Chat, false);
        hud.ShowTab(HudTab.Chat);
        hud.HandleTabActivation(HudTab.Spells, true);
        _ = hud.CollapseExpanded();

        Assert.Equal(
            ["get_Inventory", "get_ActiveTab", "ShowTab", "HandleTabActivation", "CollapseExpanded"],
            classic.Calls);
    }

    //BRIG-145: a drop or a click on the chat box does not reach the world; while hidden, only its tab row takes the mouse
    [Fact]
    public void TheChatBoxCoversTheWorldWhereItTakesTheMouse()
    {
        var (hud, _) = NewHud();
        var box = MainHudLayout.ChatBox;
        var row = MainHudLayout.ChatCollapsedRow;

        Assert.True(hud.IsOverPanel(box.X + 40, box.Y + 30));
        Assert.False(hud.IsOverPanel(box.Right + 5, box.Y + 30));

        hud.ChatBox.SetCollapsed(true);

        Assert.False(hud.IsOverPanel(box.X + 40, box.Y + 30));
        Assert.True(hud.IsOverPanel(box.X + 40, box.Y + row.Y + 2));
    }

    //BRIG-145: the chat input is the new HUD's own, in its chat box, and F and Shift+F act on the box
    [Fact]
    public void TheChatInputAndTheChatKeysAreThisHudsOwn()
    {
        var (hud, classic) = NewHud();

        Assert.Same(hud.ChatBox.Input, hud.ChatInput);

        hud.HandleTabActivation(HudTab.Chat, false);
        Assert.True(hud.ChatBox.IsCollapsed);

        hud.HandleTabActivation(HudTab.Chat, true);
        Assert.False(hud.ChatBox.IsCollapsed);
        Assert.Equal("System", hud.ChatBox.SelectedTabName);
        Assert.Empty(classic.Calls);
    }

    [Fact]
    public void TheShellsOwnMembersDoNotReachTheClassicHud()
    {
        var (hud, classic) = NewHud();

        hud.SetPlayerName("Angelique");
        hud.SetZoneName("Mileth Village");
        hud.SetCoords(54, 37);
        hud.SetServerName("Hybrasyl");
        hud.SetTransportSecure(true);
        hud.SetWeight(42, 96);
        hud.SetGroupOpen(true);
        hud.SetDescription("an apple");
        hud.ShowPersistentMessage("Poisoned");
        hud.SetEffect(5, EffectColor.Blue);

        Assert.Empty(classic.Calls);
        Assert.Equal("Angelique", hud.PlayerName);
    }

    [Fact]
    public void TheLegacyButtonsAreAbsent()
    {
        var (hud, _) = NewHud();

        Assert.Null(hud.OptionButton);
        Assert.Null(hud.ChangeLayoutButton);
        Assert.Null(hud.EmoteButton);
        Assert.Null(hud.MailButton);
        Assert.Empty(hud.InventoryTabButtons);
    }
}
