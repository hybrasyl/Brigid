#region
using Brigid.Controls.World.Hud.Main;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-144 phase 3: the panel model of the new HUD (BRIG-127 D30, plan Q4). One open compact panel is on the right; a
///     second opens on the left; a third takes the place of the left one. A large panel closes the compact panels, and a
///     compact panel closes the large panel.
/// </summary>
public class HudPanelHostTests
{
    private const HudPanelSize COMPACT = HudPanelSize.Compact;
    private const HudPanelSize LARGE = HudPanelSize.Large;

    [Fact]
    public void OneCompactPanelIsOnTheRight()
    {
        var host = new HudPanelHost();

        host.Toggle("inventory", COMPACT);

        Assert.Equal((null, "inventory", null), (host.Left, host.Right, host.Large));
    }

    [Fact]
    public void ASecondCompactPanelOpensOnTheLeft()
    {
        var host = new HudPanelHost();

        host.Toggle("inventory", COMPACT);
        host.Toggle("abilities", COMPACT);

        Assert.Equal(("abilities", "inventory"), (host.Left, host.Right));
    }

    [Fact]
    public void AThirdCompactPanelTakesThePlaceOfTheLeftOne()
    {
        var host = new HudPanelHost();

        host.Toggle("inventory", COMPACT);
        host.Toggle("abilities", COMPACT);
        host.Toggle("equipment", COMPACT);

        Assert.Equal(("equipment", "inventory"), (host.Left, host.Right));
    }

    [Fact]
    public void WhenTheRightPanelClosesTheLeftPanelMovesToTheRight()
    {
        var host = new HudPanelHost();
        host.Toggle("inventory", COMPACT);
        host.Toggle("abilities", COMPACT);

        host.Close("inventory");

        Assert.Equal((null, "abilities"), (host.Left, host.Right));
    }

    [Fact]
    public void ALargePanelClosesTheCompactPanels_AndACompactPanelClosesTheLargePanel()
    {
        var host = new HudPanelHost();
        host.Toggle("inventory", COMPACT);
        host.Toggle("abilities", COMPACT);

        host.Toggle("mastery", LARGE);
        Assert.Equal((null, null, "mastery"), (host.Left, host.Right, host.Large));

        host.Toggle("quests", COMPACT);
        Assert.Equal((null, "quests", null), (host.Left, host.Right, host.Large));
    }

    [Fact]
    public void AToggleOfAnOpenPanelClosesIt()
    {
        var host = new HudPanelHost();
        host.Toggle("inventory", COMPACT);
        host.Toggle("mastery", LARGE);

        host.Toggle("mastery", LARGE);

        Assert.False(host.IsOpen("mastery"));
        Assert.False(host.IsOpen("inventory"));
    }

    [Fact]
    public void EachOpenPanelHasItsPlace()
    {
        var host = new HudPanelHost();
        host.Toggle("inventory", COMPACT);
        host.Toggle("abilities", COMPACT);

        Assert.Equal(MainHudLayout.CompactRight, host.PlaceOf("inventory"));
        Assert.Equal(MainHudLayout.CompactLeft, host.PlaceOf("abilities"));
        Assert.Null(host.PlaceOf("mastery"));

        host.Toggle("mastery", LARGE);
        Assert.Equal(MainHudLayout.LargePanel, host.PlaceOf("mastery"));
    }

    //a window that opens on a server reply opens, and stays where it is when it is open
    [Fact]
    public void OpenKeepsAnOpenPanel()
    {
        var host = new HudPanelHost();
        host.Open("mail", COMPACT);
        host.Open("community", COMPACT);

        host.Open("mail", COMPACT);

        Assert.Equal(("community", "mail"), (host.Left, host.Right));
    }

    [Fact]
    public void CloseAllSaysWhetherAPanelWasOpen()
    {
        var host = new HudPanelHost();

        Assert.False(host.CloseAll());

        host.Toggle("inventory", COMPACT);

        Assert.True(host.CloseAll());
        Assert.False(host.IsOpen("inventory"));
    }

    //BRIG-150: the swap button of a compact panel
    [Fact]
    public void SwapSidesChangesTheSidesOfTheTwoCompactPanels()
    {
        var host = new HudPanelHost();
        host.Toggle("inventory", COMPACT);
        host.Toggle("abilities", COMPACT);

        Assert.True(host.SwapSides());

        Assert.Equal(("inventory", "abilities"), (host.Left, host.Right));
    }

    [Fact]
    public void SwapSidesMovesOneCompactPanelAndTheNextOpensOnTheOtherSide()
    {
        var host = new HudPanelHost();
        host.Toggle("inventory", COMPACT);

        Assert.True(host.SwapSides());
        Assert.Equal(("inventory", (string?)null), (host.Left, host.Right));

        host.Toggle("abilities", COMPACT);
        Assert.Equal(("inventory", "abilities"), (host.Left, host.Right));

        //when the right panel closes, the left panel moves to the right, as before a swap (D30)
        host.Close("abilities");
        Assert.Equal(((string?)null, "inventory"), (host.Left, host.Right));
    }

    [Fact]
    public void SwapSidesDoesNothingForALargePanelOrNoPanel()
    {
        var host = new HudPanelHost();
        Assert.False(host.SwapSides());

        host.Toggle("mastery", LARGE);
        Assert.False(host.SwapSides());
        Assert.Equal(((string?)null, (string?)null, "mastery"), (host.Left, host.Right, host.Large));
    }
}
