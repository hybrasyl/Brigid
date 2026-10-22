#region
using Brigid.Controls.Components;
using Brigid.Controls.World.Popups;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-125 world context menus on <see cref="Controls.Generic.PopupMenu" />: the aisling menu has the name as its
///     title and three actions in order, and the door menu has "Doors" as its title and up to three doors.
/// </summary>
[Collection("FontEngine")]
public sealed class ContextMenuTests
{
    public ContextMenuTests() => FontEngine.Initialize(0);

    [Fact]
    public void TheAislingMenuHasTheNameAsItsTitle_AndEveryChildNamed()
    {
        var menu = new AislingContextMenu();
        menu.Show(10, 10, "Angelique", static () => { }, static () => { }, static () => { });

        Assert.Equal("Angelique", menu.FindChild<UILabel>("title")!.Text);
        UiAssert.NamedTree(menu);
    }

    [Fact]
    public void TheDoorMenuHasDoorsAsItsTitle_AndEveryChildNamed()
    {
        var menu = new DoorContextMenu();
        menu.Show(10, 10, [("Open Door", static () => { })]);

        Assert.Equal("Doors", menu.FindChild<UILabel>("title")!.Text);
        UiAssert.NamedTree(menu);
    }

    [Theory]
    [InlineData(1, "info")]
    [InlineData(2, "group request")]
    [InlineData(3, "whisper")]
    public void TheAislingMenuRunsTheActionInItsRow(int downs, string expected)
    {
        var menu = new AislingContextMenu();
        string? ran = null;
        menu.Show(10, 10, "Angelique", () => ran = "info", () => ran = "group request", () => ran = "whisper");

        for (var i = 0; i < downs; i++)
            menu.OnKeyDown(new KeyDownEvent { Key = Keys.Down });

        menu.OnKeyDown(new KeyDownEvent { Key = Keys.Enter });

        Assert.Equal(expected, ran);
        Assert.False(menu.Visible);
    }

    [Fact]
    public void TheAislingMenuIsWideEnoughForALongName()
    {
        var menu = new AislingContextMenu();
        const string NAME = "Wwwwwwwwwwwwwwwwwwwwwwwwwwww";
        menu.Show(10, 10, NAME, static () => { }, static () => { }, static () => { });

        Assert.True(menu.Width > TextRenderer.MeasureWidth(NAME));
    }

    [Fact]
    public void TheDoorMenuShowsUpToThreeDoors()
    {
        var menu = new DoorContextMenu();
        var opened = new List<int>();
        var entries = Enumerable.Range(0, 5).Select(i => ($"Open Door {i}", (Action)(() => opened.Add(i)))).ToList();

        menu.Show(10, 10, entries);

        Assert.True(menu.Visible);
        Assert.Equal(DoorContextMenu.MAX_ENTRIES, menu.Count);

        menu.OnKeyDown(new KeyDownEvent { Key = Keys.Up });
        menu.OnKeyDown(new KeyDownEvent { Key = Keys.Enter });

        Assert.Equal([2], opened);
    }

    [Fact]
    public void TheDoorMenuWithNoDoorDoesNotOpen()
    {
        var menu = new DoorContextMenu();

        menu.Show(10, 10, []);

        Assert.False(menu.Visible);
    }
}
