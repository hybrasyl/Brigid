#region
using System.Text.RegularExpressions;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.LobbyLogin;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-107 main menu: every child is addressable by a lowercase name for a later <c>ui_panels</c> pack, the
///     buttons that need a lobby connection follow it and the others do not, and the status line shows the server.
/// </summary>
[Collection("FontEngine")]
public class MainMenuControlTests
{
    public MainMenuControlTests() => FontEngine.Initialize(0);

    [Fact]
    public void EveryChildHasAUniqueName()
    {
        var menu = new MainMenuControl();

        Assert.Equal("mainmenu", menu.Name);
        AssertNamedTree(menu);
    }

    [Fact]
    public void TheButtonsAreInTheirOrder()
    {
        var menu = new MainMenuControl();
        var column = menu.FindChild<UIPanel>("buttons")!;

        Assert.Equal(
            ["create", "continue", "password", "about", "homepage", "story", "config", "exit"],
            column.Children.OrderBy(button => button.Y).Select(button => button.Name));
    }

    [Fact]
    public void OnlyTheConnectionButtonsWaitForTheLobby()
    {
        var menu = new MainMenuControl();

        //the menu starts with the connection buttons off, until the server greeting
        Assert.False(menu.ContinueButton.Enabled);
        Assert.False(menu.CreateButton.Enabled);
        Assert.False(menu.PasswordButton.Enabled);
        Assert.False(menu.HomepageButton.Enabled);
        Assert.True(menu.AboutButton.Enabled);
        Assert.True(menu.StoryButton.Enabled);
        Assert.True(menu.ConfigButton.Enabled);
        Assert.True(menu.ExitButton.Enabled);

        menu.EnableButtons();

        Assert.True(menu.ContinueButton.Enabled);
        Assert.True(menu.HomepageButton.Enabled);

        menu.SetButtonsEnabled(false);

        Assert.False(menu.ContinueButton.Enabled);
        Assert.True(menu.AboutButton.Enabled);
        Assert.True(menu.StoryButton.Enabled);
        Assert.True(menu.ExitButton.Enabled);
    }

    [Fact]
    public void EveryChildOfTheOpenPanelsHasAUniqueName()
    {
        var menu = new MainMenuControl();

        //the alert tabs and actions, and the Story view, are made when they are needed
        menu.Alerts.Add(MenuAlerts.NoSound(true));
        menu.Alerts.Add(MenuAlerts.LegacyImport(() => { }, () => { }));
        Click(menu.StoryButton);

        AssertNamedTree(menu);
    }

    [Fact]
    public void TheAlertsShowOnlyWhileThereIsAnAlert()
    {
        var menu = new MainMenuControl();

        Assert.False(menu.Alerts.Visible);

        menu.Alerts.Add(MenuAlerts.NoSound(false));
        Assert.True(menu.Alerts.Visible);

        menu.Alerts.RunAction(0);
        Assert.False(menu.Alerts.Visible);
    }

    [Fact]
    public void APopupCoversTheButtonsAndTheAlerts()
    {
        var menu = new MainMenuControl();
        menu.Alerts.Add(MenuAlerts.NoSound(false));

        menu.SetCovered(true);

        Assert.False(menu.FindChild<UIPanel>("buttons")!.Visible);
        Assert.False(menu.Alerts.Visible);
        Assert.True(menu.FindChild<UIPanel>("status")!.Visible);

        //an alert that arrives while the menu is covered waits
        menu.Alerts.Add(MenuAlerts.LegacyImport(() => { }, () => { }));
        Assert.False(menu.Alerts.Visible);

        menu.SetCovered(false);

        Assert.True(menu.FindChild<UIPanel>("buttons")!.Visible);
        Assert.True(menu.Alerts.Visible);
    }

    [Fact]
    public void TheMenuWaitsForTheGreetingThenGivesUp()
    {
        var menu = new MainMenuControl();
        var buttons = menu.FindChild<UIPanel>("buttons")!;

        menu.CoverUntilGreeting(5000);
        menu.Update(Elapsed(4000));

        Assert.False(buttons.Visible);
        Assert.True(menu.IsWaitingForGreeting);

        menu.Update(Elapsed(1500));

        Assert.True(buttons.Visible);
        Assert.False(menu.IsWaitingForGreeting);
    }

    [Fact]
    public void AGreetingInTimeKeepsTheMenuCovered()
    {
        var menu = new MainMenuControl();
        var buttons = menu.FindChild<UIPanel>("buttons")!;

        menu.CoverUntilGreeting(5000);

        //the greeting opens: the screen covers the menu, and the timeout no longer applies
        menu.SetCovered(true);
        menu.Update(Elapsed(10000));

        Assert.False(buttons.Visible);

        menu.SetCovered(false);

        Assert.True(buttons.Visible);
    }

    [Fact]
    public void EnabledButtonsEndTheWait()
    {
        var menu = new MainMenuControl();

        menu.CoverUntilGreeting(5000);
        menu.EnableButtons();

        Assert.True(menu.FindChild<UIPanel>("buttons")!.Visible);
        Assert.False(menu.IsWaitingForGreeting);
    }

    [Fact]
    public void DisabledButtonsDoNotEndTheWait()
    {
        var menu = new MainMenuControl();

        menu.CoverUntilGreeting(5000);
        menu.SetButtonsEnabled(false);

        Assert.True(menu.IsWaitingForGreeting);
    }

    [Fact]
    public void StoryOpensTheShippedStory()
    {
        var menu = new MainMenuControl();

        Click(menu.StoryButton);

        var story = menu.FindChild<StoryView>("storyview")!;

        Assert.True(story.Visible);
        Assert.True(story.IsModal);
        Assert.Equal(0, story.PageIndex);
    }

    [Fact]
    public void TheMenuThemesEverythingInIt()
    {
        var menu = new MainMenuControl();

        Assert.True(menu.ContinueButton.IsThemed);
        Assert.True(menu.FindChild<UIElement>("title")!.IsThemed);
    }

    [Theory]
    [InlineData("qa.hybrasyl.com", false, null, "qa.hybrasyl.com")]
    [InlineData("qa.hybrasyl.com", false, "Hybrasyl QA", "Hybrasyl QA")]
    [InlineData("qa.hybrasyl.com", true, "Hybrasyl QA", Glyphs.PADLOCK + "Hybrasyl QA")]
    [InlineData("qa.hybrasyl.com", true, " ", Glyphs.PADLOCK + "qa.hybrasyl.com")]
    public void TheStatusLineShowsTheServer(string host, bool secure, string? serverName, string expected)
    {
        var menu = new MainMenuControl();

        menu.SetConnection(host, secure, serverName);

        Assert.Equal(expected, menu.FindChild<UILabel>("server")!.Text);
    }

    [Fact]
    public void TheStatusLineDoesNotOverlap()
    {
        var menu = new MainMenuControl();
        menu.SetConnection("a.long.server.name.example.com", true, null);
        menu.Update(new GameTime());

        var version = menu.FindChild<UILabel>("version")!;
        var server = menu.FindChild<UILabel>("server")!;

        Assert.True(server.X >= version.X + version.Width);
    }

    [Fact]
    public void TheStatusLineIsMeasuredAgainAfterAResize()
    {
        var engine = FontEngine.Instance;
        var menu = new MainMenuControl();
        var server = menu.FindChild<UILabel>("server")!;

        menu.SetConnection("qa.hybrasyl.com", false, "Hybrasyl Quality Assurance Server");
        menu.Update(new GameTime());

        try
        {
            //a window resize changes the layout scale, so measured widths change with it
            engine.SetLayoutScale(1.75f, 1.75f);
            menu.Update(new GameTime());

            Assert.Equal(TextRenderer.MeasureWidth(server.Text) + 2, server.Width);
        } finally
        {
            engine.SetLayoutScale(1f, 1f);
        }

        menu.Update(new GameTime());
        Assert.Equal(TextRenderer.MeasureWidth(server.Text) + 2, server.Width);
    }

    [Fact]
    public void TheAboutTextShips()
        => Assert.StartsWith("# About Hybrasyl", MainMenuControl.ReadText("about.md"));

    private static void AssertNamedTree(UIPanel panel)
    {
        var names = panel.Children.Select(child => child.Name).ToList();

        //the id rule of the ui_panels layout plan; a dot is the path separator, so it is never part of a name
        Assert.All(names, name => Assert.Matches(new Regex("^[a-z0-9_]+$"), name));
        Assert.Equal(names.Count, names.Distinct().Count());

        //a TextButton or a tab is a panel too; its label is its own detail, not a separate target
        foreach (var child in panel.Children.OfType<UIPanel>().Where(child => child is not (TextButton or SelectableTab)))
            AssertNamedTree(child);
    }

    private static void Click(TextButton button) => button.OnClick(new ClickEvent { Button = MouseButton.Left });

    private static GameTime Elapsed(double ms) => new(TimeSpan.Zero, TimeSpan.FromMilliseconds(ms));
}
