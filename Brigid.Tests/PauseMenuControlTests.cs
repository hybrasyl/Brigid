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
///     closes the menu, X closes it and exits, and every other key goes on to the world screen.
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
    public void XClosesTheMenuAndExits()
    {
        var menu = new PauseMenuControl();
        var exits = 0;
        var closes = 0;
        menu.OnExit += () => exits++;
        menu.OnClose += () => closes++;
        menu.Show();

        var x = new KeyDownEvent { Key = Keys.X };
        menu.OnKeyDown(x);

        Assert.True(x.Handled);
        Assert.False(menu.Visible);
        Assert.Equal(1, exits);
        Assert.Equal(1, closes);
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
