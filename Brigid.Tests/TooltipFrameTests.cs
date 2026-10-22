#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Popups;
using Brigid.Data.AssetPacks;
using Brigid.Rendering;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-125 tooltip frame: the lines wrap at the largest text width, the frame fits the widest line, it sits at the
///     cursor in the viewport, it takes no mouse input, and it draws the <c>frame.popup</c> part (not <c>frame.panel</c>).
///     The item tooltip shows the durability only when the item has one.
/// </summary>
[Collection("FontEngine")]
public sealed class TooltipFrameTests : IDisposable
{
    private const int MAX_TEXT_W = 150;

    private UiThemePack? Theme;

    public TooltipFrameTests() => FontEngine.Initialize(0);

    public void Dispose()
    {
        AssetPackRegistry.SetUiThemeForTests(null);
        Theme?.Dispose();
    }

    [Fact]
    public void AShortLineGivesANarrowFrame_AndALongLineWraps()
    {
        var tooltip = new TooltipFrame(MAX_TEXT_W);

        tooltip.SetLines([TooltipLine.Themed("Dirk", ThemeRoles.Text)]);
        var narrow = tooltip.Width;

        tooltip.SetLines([TooltipLine.Themed("a very long item name that cannot fit on one line of the tooltip", ThemeRoles.Text)]);

        Assert.True(narrow < tooltip.Width);
        Assert.True(tooltip.WrappedText.Count() > 1);
        Assert.All(tooltip.WrappedText, line => Assert.True(TextRenderer.MeasureWidth(line) <= MAX_TEXT_W));
        Assert.Equal(tooltip.WrappedText.Count() * TextRenderer.CHAR_HEIGHT + 2 * tooltip.InsetFor(TooltipFrame.PADDING), tooltip.Height);
    }

    [Fact]
    public void TheTooltipSitsAtTheCursor_AndFlipsAtTheRightEdge()
    {
        var tooltip = new TooltipFrame(MAX_TEXT_W);
        tooltip.SetLines([TooltipLine.Themed("Dirk", ThemeRoles.Text)]);

        tooltip.PlaceAtCursor(100, 100);
        Assert.True(tooltip.X > 100 && tooltip.Y > 100);

        tooltip.PlaceAtCursor(635, 475);
        Assert.True(tooltip.X + tooltip.Width <= 635);
        Assert.True(tooltip.Y + tooltip.Height <= 480);
    }

    [Fact]
    public void AThemedLineFallsBackToTheFlatValueOfItsRole_AndALiteralLineKeepsItsColour()
    {
        var themed = TooltipLine.Themed("Dirk", ThemeRoles.TextTitle);
        var literal = TooltipLine.Literal("5/10", Microsoft.Xna.Framework.Color.CornflowerBlue);

        Assert.Equal(DialogPalette.ForRole(ThemeRoles.TextTitle), themed.Color);
        Assert.Equal(ThemeRoles.TextTitle, themed.Role);
        Assert.Null(literal.Role);
        Assert.Equal(Microsoft.Xna.Framework.Color.CornflowerBlue, literal.Color);
    }

    [Fact]
    public void TheTooltipTakesNoMouseInput() => Assert.False(new TooltipFrame(MAX_TEXT_W).IsHitTestVisible);

    [Fact]
    public void AThemePopupFrameWidensTheInset_AndAPanelFrameDoesNot()
    {
        var flat = Fitted();

        InstallTheme("frame.panel");
        var panelOnly = Fitted();

        InstallTheme("frame.popup");
        var popup = Fitted();

        Assert.Equal(flat.Width, panelOnly.Width);
        Assert.True(popup.Width > flat.Width);
    }

    [Fact]
    public void TheItemTooltipShowsTheDurability_OnlyWhenTheItemHasOne()
    {
        var tooltip = new ItemTooltipControl();

        tooltip.Show("Dirk", 5, 10, 100, 100);
        Assert.Equal(["Dirk", "5/10"], tooltip.WrappedText);
        Assert.True(tooltip.Visible);

        tooltip.Show("Apple", 0, 0, 100, 100);
        Assert.Equal(["Apple"], tooltip.WrappedText);

        tooltip.Hide();
        Assert.False(tooltip.Visible);
    }

    [Fact]
    public void TheItemTooltipWrapsAt25Characters()
    {
        var tooltip = new ItemTooltipControl();

        tooltip.Show("Wwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwww Wwwwwwwww", 0, 0, 100, 100);

        Assert.True(tooltip.Width <= 25 * TextRenderer.CHAR_WIDTH + 2 * tooltip.InsetFor(TooltipFrame.PADDING) + 1);
    }

    private static TooltipFrame Fitted()
    {
        var tooltip = new TooltipFrame(MAX_TEXT_W);
        tooltip.SetLines([TooltipLine.Themed("Dirk", ThemeRoles.Text)]);

        return tooltip;
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
