#region
using Brigid.Controls.World.Hud;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-144 (BRIG-127 D42): while the new HUD is built, Shift+/ changes to it and back to the HUD that showed before,
///     and / swaps the classic and the large HUD, but does nothing while the new HUD shows.
/// </summary>
public class HudLayoutSwitchTests
{
    [Fact]
    public void TheClassicHudShowsFirst() => Assert.Equal(HudLayout.Classic, new HudLayoutSwitch().Active);

    [Fact]
    public void SwapChangesBetweenTheClassicAndTheLargeHud()
    {
        var huds = new HudLayoutSwitch();

        Assert.True(huds.Swap());
        Assert.Equal(HudLayout.Large, huds.Active);

        Assert.True(huds.Swap());
        Assert.Equal(HudLayout.Classic, huds.Active);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheMainHudGoesBackToTheHudThatShowedBefore(bool fromLarge)
    {
        var huds = new HudLayoutSwitch();

        if (fromLarge)
            huds.Swap();

        var before = huds.Active;

        huds.ToggleMain();
        Assert.Equal(HudLayout.Main, huds.Active);

        huds.ToggleMain();
        Assert.Equal(before, huds.Active);
    }

    [Fact]
    public void SwapDoesNothingWhileTheMainHudShows()
    {
        var huds = new HudLayoutSwitch();
        huds.ToggleMain();

        Assert.False(huds.Swap());
        Assert.Equal(HudLayout.Main, huds.Active);

        //the HUD before the main HUD is still the one it goes back to
        huds.ToggleMain();
        Assert.Equal(HudLayout.Classic, huds.Active);
    }
}
