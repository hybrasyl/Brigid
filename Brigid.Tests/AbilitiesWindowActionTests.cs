#region
using System.Reflection;
using Brigid.Controls.Components;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Main;
using Brigid.Controls.World.Hud.Main.Windows;
using Brigid.Data.Models;
using Brigid.Definitions;
using Brigid.Models;
using Brigid.Rendering;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-149: the actions of the Abilities window. A left click uses a learned ability and does nothing on one that the
///     player can learn; a right click asks for the details of either; a drag of a learned ability carries its kind and
///     slot; the cooldown of the book drives the swipe; on the new HUD the ability keys open and close the window and
///     still select the hidden classic panel.
/// </summary>
[Collection("FontEngine")]
public class AbilitiesWindowActionTests : IDisposable
{
    public AbilitiesWindowActionTests() => FontEngine.Initialize(0);

    private readonly List<IDisposable> Owned = [];

    public void Dispose()
    {
        foreach (var owned in Owned)
            owned.Dispose();
    }

    private (AbilitiesWindowControl Window, SkillBook Skills, SpellBook Spells) NewWindow()
    {
        var skills = new SkillBook();
        skills.SetSlot(3, 1, "Assail");
        var spells = new SpellBook();
        spells.SetSlot(40, 1, "beag ioc");
        var metadata = new AbilityMetadata([new AbilityMetadataEntry { Name = "Kick", IsSpell = false }], []);

        var window = new AbilitiesWindowControl(skills, spells, new PlayerAttributes(), () => metadata, _ => null, _ => AbilityIconState.Locked);
        window.SetBounds(new Rectangle(6, 22, 298, 311));
        Owned.Add(window);
        window.UnlearnedFilter.OnClick(new ClickEvent { Button = MouseButton.Left });
        window.Refresh();

        return (window, skills, spells);
    }

    private static AbilityRowControl RowOf(AbilitiesWindowControl window, string name) => window.RowControls.Single(row => row.Row?.Name == name);

    [Fact]
    public void ALeftClickUsesALearnedAbility()
    {
        var (window, _, _) = NewWindow();
        var used = new List<AbilityRow>();
        window.UseRequested += used.Add;

        var click = new ClickEvent { Button = MouseButton.Left };
        RowOf(window, "beag ioc").OnClick(click);

        Assert.Equal([(true, (byte?)40)], used.Select(row => (row.IsSpell, row.Slot)));
        Assert.True(click.Handled);
    }

    [Fact]
    public void ALeftClickOnAnAbilityToLearnDoesNothing()
    {
        var (window, _, _) = NewWindow();
        var used = 0;
        window.UseRequested += _ => used++;

        var click = new ClickEvent { Button = MouseButton.Left };
        RowOf(window, "Kick").OnClick(click);

        Assert.Equal(0, used);
        Assert.False(click.Handled);
    }

    [Theory]
    [InlineData("Assail", true)]
    [InlineData("Kick", false)]
    public void ARightClickAsksForTheDetails(string name, bool learned)
    {
        var (window, _, _) = NewWindow();
        var asked = new List<AbilityRow>();
        window.DetailsRequested += asked.Add;

        RowOf(window, name).OnClick(new ClickEvent { Button = MouseButton.Right });

        Assert.Equal(name, Assert.Single(asked).Name);
        Assert.Equal(learned, asked[0].IsLearned);
    }

    [Fact]
    public void ADragOfALearnedAbilityCarriesItsKindAndSlot()
    {
        var (window, _, _) = NewWindow();
        var drag = new DragStartEvent { Button = MouseButton.Left };

        RowOf(window, "beag ioc").OnDragStart(drag);

        Assert.Equal(new AbilityDragPayload(true, 40, null), drag.Payload);
    }

    //a drop casts a spell at once, so an ability in its cooldown does not drag, as on the classic slots
    [Fact]
    public void AnAbilityInItsCooldownDoesNotDrag()
    {
        var (window, _, spells) = NewWindow();
        spells.SetCooldown(40, 5);
        var drag = new DragStartEvent { Button = MouseButton.Left };

        RowOf(window, "beag ioc").OnDragStart(drag);

        Assert.Null(drag.Payload);
    }

    [Fact]
    public void ARightButtonDoesNotDrag()
    {
        var (window, _, _) = NewWindow();
        var drag = new DragStartEvent { Button = MouseButton.Right };

        RowOf(window, "beag ioc").OnDragStart(drag);

        Assert.Null(drag.Payload);
    }

    [Fact]
    public void AnAbilityToLearnDoesNotDrag()
    {
        var (window, _, _) = NewWindow();
        var drag = new DragStartEvent { Button = MouseButton.Left };

        RowOf(window, "Kick").OnDragStart(drag);

        Assert.Null(drag.Payload);
    }

    //the swipe covers the part of the cooldown that is left: all of it just after a use
    [Fact]
    public void TheCooldownOfTheBookDrivesTheSwipe()
    {
        var (window, skills, spells) = NewWindow();
        var assail = RowOf(window, "Assail").Row!;
        var beag = RowOf(window, "beag ioc").Row!;

        Assert.Equal(0f, window.CooldownOf(assail));

        skills.SetCooldown(3, 10);
        spells.SetCooldown(40, 4);
        spells.Update(1000);

        Assert.Equal(1f, window.CooldownOf(assail));
        Assert.Equal(0.75f, window.CooldownOf(beag), 4);
        Assert.Equal(0f, window.CooldownOf(RowOf(window, "Kick").Row!));
    }

    private MainHudControl NewHud(out MainHudControlTests.RecordingHud classic)
    {
        var proxy = DispatchProxy.Create<IWorldHud, MainHudControlTests.RecordingHud>();
        var hud = new MainHudControl(proxy, attributes: new PlayerAttributes(), chat: new Chat());
        Owned.Add(hud);
        classic = (MainHudControlTests.RecordingHud)(object)proxy;

        return hud;
    }

    private static bool AbilitiesOpen(MainHudControl hud) => hud.Windows.Panels.Single(panel => panel.Window == HudWindows.Abilities).Visible;

    [Theory]
    [InlineData(HudTab.Skills, false)]
    [InlineData(HudTab.Skills, true)]
    [InlineData(HudTab.Spells, false)]
    [InlineData(HudTab.Spells, true)]
    [InlineData(HudTab.Tools, false)]
    public void TheAbilityKeysOpenAndCloseTheWindowAndSelectTheClassicPanel(HudTab tab, bool shift)
    {
        var hud = NewHud(out var classic);

        hud.HandleTabActivation(tab, shift);
        Assert.True(AbilitiesOpen(hud));

        hud.HandleTabActivation(tab, shift);
        Assert.False(AbilitiesOpen(hud));

        Assert.Equal(["HandleTabActivation", "HandleTabActivation"], classic.Calls);
    }

    [Fact]
    public void TheOtherTabKeysDoNotOpenTheWindow()
    {
        var hud = NewHud(out _);

        hud.HandleTabActivation(HudTab.Inventory, false);
        Assert.False(AbilitiesOpen(hud));

        hud.HandleTabActivation(HudTab.Stats, false);
        Assert.False(AbilitiesOpen(hud));
    }
}
