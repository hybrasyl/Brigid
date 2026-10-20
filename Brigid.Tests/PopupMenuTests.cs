#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Data.AssetPacks;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-125 popup menu: it opens with up to its largest number of entries, fits its widest text, stays in the
///     viewport, and draws the <c>frame.popup</c> part (not <c>frame.panel</c>). The keys and the mouse select and pick an
///     entry; Escape, a right-click and a click that is not on an entry close it.
/// </summary>
[Collection("FontEngine")]
public sealed class PopupMenuTests : IDisposable
{
    private UiThemePack? Theme;

    public PopupMenuTests() => FontEngine.Initialize(0);

    public void Dispose()
    {
        AssetPackRegistry.SetUiThemeForTests(null);
        Theme?.Dispose();
    }

    private static PopupMenu Open(out List<int> picked, int x = 100, int y = 100, int count = 3)
    {
        var log = new List<int>();
        var menu = new PopupMenu(3);
        var entries = Enumerable.Range(0, count).Select(i => ($"entry {i}", (Action)(() => log.Add(i)))).ToList();

        menu.Show(x, y, "Title", entries);
        picked = log;

        return menu;
    }

    [Fact]
    public void EveryChildHasAUniqueName() => UiAssert.NamedTree(Open(out _));

    [Fact]
    public void WithNoEntryTheMenuDoesNotOpen()
    {
        var menu = new PopupMenu(3);

        menu.Show(0, 0, "Title", []);

        Assert.False(menu.Visible);
    }

    [Fact]
    public void EntriesAfterTheLargestNumberAreDropped()
    {
        var menu = Open(out _, count: 5);

        Assert.Equal(3, menu.Count);
    }

    [Fact]
    public void TheMenuIsAtLeastTheLegacyWidthAndFitsALongEntry()
    {
        var shortMenu = Open(out _);
        var longMenu = new PopupMenu(1);
        longMenu.Show(0, 0, "Title", [("a much longer entry than the legacy row holds", static () => { })]);

        Assert.True(shortMenu.Width >= 74 + 2 * 4);
        Assert.True(longMenu.Width >= TextRenderer.MeasureWidth("a much longer entry than the legacy row holds") + 2 * 4);
    }

    [Fact]
    public void TheMenuStaysInTheViewport()
    {
        var menu = Open(out _, 630, 470);

        Assert.True(menu.X + menu.Width <= 640);
        Assert.True(menu.Y + menu.Height <= 480);
        Assert.True(menu.X >= 0 && menu.Y >= 0);
    }

    [Fact]
    public void DownAndUpMoveTheSelectionAndWrap()
    {
        var menu = Open(out _);

        Press(menu, Keys.Up);
        Assert.Equal(2, menu.SelectedIndex);

        Press(menu, Keys.Down);
        Assert.Equal(0, menu.SelectedIndex);

        Press(menu, Keys.Down);
        Assert.Equal(1, menu.SelectedIndex);
    }

    [Fact]
    public void EnterPicksTheSelectedEntryAndClosesTheMenu()
    {
        var menu = Open(out var picked);

        Press(menu, Keys.Down);
        Press(menu, Keys.Down);
        Press(menu, Keys.Enter);

        Assert.Equal([1], picked);
        Assert.False(menu.Visible);
    }

    [Fact]
    public void EnterWithNoSelectionOrWithAltPicksNothing()
    {
        var menu = Open(out var picked);

        Press(menu, Keys.Enter);
        Press(menu, Keys.Down);
        menu.OnKeyDown(new KeyDownEvent { Key = Keys.Enter, Modifiers = KeyModifiers.Alt });

        Assert.Empty(picked);
        Assert.True(menu.Visible);
    }

    [Fact]
    public void EscapeClosesTheMenu()
    {
        var menu = Open(out var picked);

        Press(menu, Keys.Escape);

        Assert.False(menu.Visible);
        Assert.Empty(picked);
    }

    [Fact]
    public void AClickOnAnEntryPicksIt()
    {
        var menu = Open(out var picked);

        menu.OnClick(new ClickEvent { Button = MouseButton.Left, ScreenX = menu.X + 10, ScreenY = EntryY(menu, 2) });

        Assert.Equal([2], picked);
        Assert.False(menu.Visible);
    }

    [Theory]
    [InlineData(MouseButton.Right, true)]
    [InlineData(MouseButton.Left, false)]
    public void ARightClickOrAClickOnTheTitleClosesTheMenu(MouseButton button, bool onEntry)
    {
        var menu = Open(out var picked);
        var y = onEntry ? EntryY(menu, 0) : menu.Y + 6;

        menu.OnClick(new ClickEvent { Button = button, ScreenX = menu.X + 10, ScreenY = y });

        Assert.Empty(picked);
        Assert.False(menu.Visible);
    }

    [Fact]
    public void TheMouseSelectsTheEntryUnderIt()
    {
        var menu = Open(out _);

        menu.OnMouseMove(new MouseMoveEvent { ScreenX = menu.X + 10, ScreenY = EntryY(menu, 1) });
        Assert.Equal(1, menu.SelectedIndex);

        menu.OnMouseLeave();
        Assert.Equal(-1, menu.SelectedIndex);
    }

    [Fact]
    public void TheMenuStaysInTheViewport_AtANegativePoint()
    {
        var menu = Open(out _, -50, -50);

        Assert.Equal(0, menu.X);
        Assert.Equal(0, menu.Y);
    }

    [Fact]
    public void TheMenuStaysInsideItsParent()
    {
        var parent = new UIPanel { Width = 200, Height = 120 };
        var menu = new PopupMenu(3);
        parent.AddChild(menu);

        menu.Show(190, 110, "Title", [("entry", static () => { })]);

        Assert.True(menu.X + menu.Width <= parent.Width);
        Assert.True(menu.Y + menu.Height <= parent.Height);
    }

    [Fact]
    public void ASecondShowReplacesTheEntriesAndClearsTheSelection()
    {
        var menu = Open(out var picked);
        Press(menu, Keys.Down);

        menu.Show(100, 100, "Other", [("only", () => picked.Add(9))]);

        Assert.Equal(1, menu.Count);
        Assert.Equal(-1, menu.SelectedIndex);

        Press(menu, Keys.Down);
        Press(menu, Keys.Enter);
        Assert.Equal([9], picked);
    }

    [Fact]
    public void AClickBesideTheRowsOrBelowTheLastRowClosesTheMenu()
    {
        var menu = Open(out var picked, count: 2);

        Assert.Equal(-1, menu.RowAt(1, menu.RowBounds(0).Center.Y));
        Assert.Equal(-1, menu.RowAt(menu.RowBounds(0).Center.X, menu.RowBounds(1).Bottom + 1));

        menu.OnClick(new ClickEvent { Button = MouseButton.Left, ScreenX = menu.X + 1, ScreenY = EntryY(menu, 0) });

        Assert.Empty(picked);
        Assert.False(menu.Visible);
    }

    [Fact]
    public void AnEntryThatOpensTheMenuAgainLeavesItOpen()
    {
        var menu = new PopupMenu(1);
        menu.Show(0, 0, "Title", [("again", () => menu.Show(0, 0, "Title", [("x", static () => { })]))]);

        Press(menu, Keys.Down);
        Press(menu, Keys.Enter);

        Assert.True(menu.Visible);
    }

    [Fact]
    public void AThemePopupFrameWidensTheInset_AndAPanelFrameDoesNot()
    {
        var flat = Open(out _);

        InstallTheme("frame.panel");
        var panelOnly = Open(out _);

        InstallTheme("frame.popup");
        var popup = Open(out _);

        Assert.Equal(flat.Width, panelOnly.Width);
        Assert.True(popup.Width > flat.Width);
    }

    private static void Press(PopupMenu menu, Keys key) => menu.OnKeyDown(new KeyDownEvent { Key = key });

    //the screen y in the middle of an entry row
    private static int EntryY(PopupMenu menu, int index) => menu.Y + menu.RowBounds(index).Center.Y;

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
