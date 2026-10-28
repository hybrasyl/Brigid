#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Popups.Options;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-55 pause menu: every child has a lowercase name, the sliders carry the volumes in both directions, Escape
///     closes the menu, X closes it and exits to the menu, and every other key goes on to the world screen. BRIG-139: Exit
///     to Menu and Exit Game each raise their own event.
/// </summary>
[Collection("FontEngine")]
public class PauseMenuControlTests
{
    public PauseMenuControlTests() => FontEngine.Initialize(0);

    [Fact]
    public void EveryChildHasAUniqueName()
    {
        var menu = new PauseMenuControl();

        Assert.Equal("pausemenu", menu.Name);
        UiAssert.NamedTree(menu);
    }

    [Fact]
    public void TheSlidersCarryTheVolumes()
    {
        var menu = new PauseMenuControl();
        var music = new List<int>();
        menu.OnMusicVolumeChanged += music.Add;

        menu.SetSoundVolume(3);
        menu.SetMusicVolume(7);
        menu.SetAmbienceVolume(12);

        Assert.Equal(3, menu.GetSoundVolume());
        Assert.Equal(7, menu.GetMusicVolume());
        Assert.Equal(SliderControl.MAX_VALUE, menu.GetAmbienceVolume());
        Assert.Empty(music);

        var slider = menu.FindChild<SliderControl>("music")!;
        slider.OnMouseDown(new MouseDownEvent { Button = MouseButton.Left, ScreenX = slider.ScreenX + SliderControl.THUMB_W / 2 });

        Assert.Equal([0], music);
    }

    [Fact]
    public void XClosesTheMenuAndExitsToTheMenu()
    {
        var menu = new PauseMenuControl();
        var toMenu = 0;
        var game = 0;
        var closes = 0;
        menu.OnExitToMenu += () => toMenu++;
        menu.OnExitGame += () => game++;
        menu.OnClose += () => closes++;
        menu.Show();

        var x = new KeyDownEvent { Key = Keys.X };
        menu.OnKeyDown(x);

        Assert.True(x.Handled);
        Assert.False(menu.Visible);
        Assert.Equal(1, toMenu);
        Assert.Equal(0, game);
        Assert.Equal(1, closes);
    }

    [Theory]
    [InlineData("exit_menu", 1, 0)]
    [InlineData("exit_game", 0, 1)]
    public void EachExitButtonClosesTheMenuAndRaisesItsOwnEvent(string name, int expectedToMenu, int expectedGame)
    {
        var menu = new PauseMenuControl();
        var toMenu = 0;
        var game = 0;
        menu.OnExitToMenu += () => toMenu++;
        menu.OnExitGame += () => game++;
        menu.Show();

        menu.FindChild<TextButton>(name)!.OnClick(new ClickEvent { Button = MouseButton.Left });

        Assert.False(menu.Visible);
        Assert.Equal(expectedToMenu, toMenu);
        Assert.Equal(expectedGame, game);
    }

    [Fact]
    public void TheButtonsFitInThePanel()
    {
        var menu = new PauseMenuControl();
        var content = menu.ContentBounds;

        foreach (var name in (string[]) ["settings", "exit_menu", "exit_game"])
        {
            var button = menu.FindChild<TextButton>(name)!;

            Assert.True(button.X >= content.X && button.X + button.Width <= content.Right, name);
            Assert.True(button.Y >= content.Y && button.Y + button.Height <= content.Bottom, name);
        }

        Assert.True(menu.FindChild<TextButton>("exit_menu")!.Y > menu.FindChild<TextButton>("settings")!.Y);
    }

    [Fact]
    public void EscapeClosesTheMenuAndOtherKeysGoOn()
    {
        var menu = new PauseMenuControl();
        menu.Show();

        //Q is the default menu toggle: the world screen must get it to close the menu again
        var q = new KeyDownEvent { Key = Keys.Q };
        menu.OnKeyDown(q);

        Assert.False(q.Handled);
        Assert.True(menu.Visible);

        var escape = new KeyDownEvent { Key = Keys.Escape };
        menu.OnKeyDown(escape);

        Assert.True(escape.Handled);
        Assert.False(menu.Visible);
    }

    [Fact]
    public void SettingsClosesTheMenuAndOpensTheSettings()
    {
        var menu = new PauseMenuControl();
        var settings = 0;
        menu.OnSettings += () => settings++;
        menu.Show();

        menu.FindChild<TextButton>("settings")!.OnClick(new ClickEvent { Button = MouseButton.Left });

        Assert.False(menu.Visible);
        Assert.Equal(1, settings);
    }
}
