#region
using Brigid.Controls.World.ViewPort;
using Brigid.Data.AssetPacks;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-126: the group box over a recruiting aisling draws the <c>frame.popup</c> part, else the flat frame. The
///     title area keeps the legacy size, and the frame goes around it, so a wider themed border makes the box larger. The
///     box sizes itself for the current theme each time its text is set, so a theme change while the game runs takes effect
///     on the next frame.
/// </summary>
[Collection("FontEngine")]
public sealed class GroupBoxTests : IDisposable
{
    private UiThemePack? Theme;

    public GroupBoxTests()
    {
        FontEngine.Initialize(0);
        AssetPackRegistry.SetUiThemeForTests(null);
    }

    public void Dispose()
    {
        AssetPackRegistry.SetUiThemeForTests(null);
        Theme?.Dispose();
    }

    [Fact]
    public void TheFlatBoxGoesAroundTheLegacyTitleArea()
    {
        var box = new GroupBox(7);
        box.UpdateText("Mileth crypt");

        //the flat 1 px border and the 3 px gap
        Assert.Equal("Mileth crypt", box.Text);
        Assert.Equal(new Point(105, 21), new Point(box.Width, box.Height));
        Assert.Equal(new Rectangle(4, 4, 97, 13), box.TextBounds);
    }

    [Fact]
    public void AWidePopupBorderGrowsTheBox_AndTheTitleAreaKeepsItsSize()
    {
        InstallTheme("frame.popup");
        var box = new GroupBox(7);
        box.UpdateText("Mileth crypt");

        var inset = box.InsetFor(GroupBox.PADDING);

        Assert.True(inset > 4);
        Assert.Equal(
            new Point(GroupBox.TITLE_WIDTH + 2 * inset, GroupBox.TITLE_HEIGHT + 2 * inset),
            new Point(box.Width, box.Height));
        Assert.Equal(new Rectangle(inset, inset, GroupBox.TITLE_WIDTH, GroupBox.TITLE_HEIGHT), box.TextBounds);
    }

    [Fact]
    public void APanelFramePartDoesNotChangeTheBox()
    {
        InstallTheme("frame.panel");
        var box = new GroupBox(7);
        box.UpdateText("Mileth crypt");

        Assert.Equal(new Point(105, 21), new Point(box.Width, box.Height));
    }

    [Fact]
    public void AThemeChangeResizesTheBoxOnTheNextText()
    {
        var box = new GroupBox(7);
        box.UpdateText("Mileth crypt");

        InstallTheme("frame.popup");
        box.UpdateText("Mileth crypt");

        Assert.True(box.Width > 105);

        AssetPackRegistry.SetUiThemeForTests(null);
        box.UpdateText("Mileth crypt");

        Assert.Equal(105, box.Width);
    }

    //a theme with a sliced part for one role, with a 12 logical px border
    private void InstallTheme(string role)
    {
        Theme?.Dispose();
        Theme = UiThemeTests.BuildPack(
            "{ \"parts\": { \"" + role + "\": { \"file\": \"frame.png\", \"slice\": [12, 12, 12, 12] } } }",
            ("frame.png", UiThemeTests.Png(64, 64)));
        AssetPackRegistry.SetUiThemeForTests(Theme);
    }
}
