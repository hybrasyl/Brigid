#region
using Brigid.Controls.World.Hud.Main;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-144: the rectangles of the new HUD (BRIG-127 D28, D36, D37). The bars and the world cover the screen with no
///     gap, the panels are 4 px inside the world, and each part that a later card adds has its own place.
/// </summary>
public class MainHudLayoutTests
{
    private static readonly Rectangle Screen = new(0, 0, ChaosGame.VIRTUAL_WIDTH, ChaosGame.VIRTUAL_HEIGHT);

    //the parts of the bottom area, each in its own place
    private static readonly Rectangle[] BottomParts =
    [
        MainHudLayout.EffectBar,
        MainHudLayout.HealthBar,
        MainHudLayout.ManaBar,
        MainHudLayout.Hotbar,
        MainHudLayout.Divider,
        MainHudLayout.PlayerPlate,
        MainHudLayout.WindowButtons,
        MainHudLayout.ExperienceBar
    ];

    [Fact]
    public void TheWorldIs632By347BelowTheTopBar()
    {
        Assert.Equal(new Rectangle(4, 24, 632, 347), MainHudLayout.World);
        Assert.Equal(MainHudLayout.TopBar.Bottom, MainHudLayout.World.Top);
        Assert.Equal(MainHudLayout.World.Bottom, MainHudLayout.BottomArea.Top);
    }

    [Fact]
    public void TheBarsAndTheEdgesCoverTheScreenAroundTheWorld()
    {
        Rectangle[] chrome = [MainHudLayout.TopBar, MainHudLayout.LeftEdge, MainHudLayout.RightEdge, MainHudLayout.BottomArea];

        var covered = chrome.Sum(rect => rect.Width * rect.Height) + MainHudLayout.World.Width * MainHudLayout.World.Height;

        Assert.Equal(Screen.Width * Screen.Height, covered);

        foreach (var rect in chrome)
        {
            Assert.True(Screen.Contains(rect), rect.ToString());
            Assert.False(rect.Intersects(MainHudLayout.World), rect.ToString());
        }

        for (var i = 0; i < chrome.Length; i++)
            for (var j = i + 1; j < chrome.Length; j++)
                Assert.False(chrome[i].Intersects(chrome[j]), $"{chrome[i]} and {chrome[j]}");
    }

    [Fact]
    public void ThePanelsAre4PxInsideTheWorld()
    {
        const int inset = MainHudLayout.PANEL_INSET;
        var world = MainHudLayout.World;

        Assert.Equal(4, inset);
        Assert.Equal(new Rectangle(8, 28, 310, 339), MainHudLayout.CompactLeft);
        Assert.Equal(new Rectangle(322, 28, 310, 339), MainHudLayout.CompactRight);
        Assert.Equal(new Rectangle(8, 28, 624, 339), MainHudLayout.LargePanel);

        Assert.Equal(world.Left + inset, MainHudLayout.LargePanel.Left);
        Assert.Equal(world.Top + inset, MainHudLayout.LargePanel.Top);
        Assert.Equal(world.Right - inset, MainHudLayout.LargePanel.Right);
        Assert.Equal(world.Bottom - inset, MainHudLayout.LargePanel.Bottom);

        //the two compact panels fill the large panel with the inset between them
        Assert.Equal(MainHudLayout.LargePanel.Left, MainHudLayout.CompactLeft.Left);
        Assert.Equal(MainHudLayout.CompactLeft.Right + inset, MainHudLayout.CompactRight.Left);
        Assert.Equal(MainHudLayout.LargePanel.Right, MainHudLayout.CompactRight.Right);
        Assert.Equal(MainHudLayout.CompactLeft.Width, MainHudLayout.CompactRight.Width);
    }

    [Fact]
    public void ThePartsOfTheBottomAreaDoNotOverlap()
    {
        foreach (var part in BottomParts)
            Assert.True(MainHudLayout.BottomArea.Contains(part), part.ToString());

        for (var i = 0; i < BottomParts.Length; i++)
            for (var j = i + 1; j < BottomParts.Length; j++)
                Assert.False(BottomParts[i].Intersects(BottomParts[j]), $"{BottomParts[i]} and {BottomParts[j]}");
    }

    [Fact]
    public void TheHotbarHoldsTwelve32PxSlots()
    {
        Assert.Equal(32 + 4, MainHudLayout.Hotbar.Height);
        Assert.True(MainHudLayout.Hotbar.Width >= 12 * 32);
    }

    [Fact]
    public void TheChatBoxAndTheToastAreOverTheWorld()
    {
        Assert.True(MainHudLayout.World.Contains(MainHudLayout.ChatBox));
        Assert.True(MainHudLayout.World.Contains(MainHudLayout.Toast));
        Assert.False(MainHudLayout.ChatBox.Intersects(MainHudLayout.Toast));
    }
}
