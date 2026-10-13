#region
using System.Text.RegularExpressions;
using Brigid.Controls.Components;
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
        AssertNamed(menu);

        static void AssertNamed(UIPanel panel)
        {
            var names = panel.Children.Select(child => child.Name).ToList();

            //the id rule of the ui_panels layout plan; a dot is the path separator, so it is never part of a name
            Assert.All(names, name => Assert.Matches(new Regex("^[a-z0-9_]+$"), name));
            Assert.Equal(names.Count, names.Distinct().Count());

            //a TextButton is a panel too; its label is the button's own detail, not a separate target
            foreach (var child in panel.Children.OfType<UIPanel>().Where(child => child is not Controls.Generic.TextButton))
                AssertNamed(child);
        }
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
        Assert.True(menu.ConfigButton.Enabled);
        Assert.True(menu.ExitButton.Enabled);

        menu.EnableButtons();

        Assert.True(menu.ContinueButton.Enabled);
        Assert.True(menu.HomepageButton.Enabled);
        //Story waits for StoryView, not for the lobby
        Assert.False(menu.StoryButton.Enabled);

        menu.SetButtonsEnabled(false);

        Assert.False(menu.ContinueButton.Enabled);
        Assert.True(menu.AboutButton.Enabled);
        Assert.True(menu.ExitButton.Enabled);
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
}
