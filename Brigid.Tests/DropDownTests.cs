#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The drop-down primitive (BRIG-116): a click on the box opens the list, a click on an entry selects it and closes the
///     list, the open list draws above its siblings, and the list closes when the mouse leaves.
/// </summary>
public class DropDownTests
{
    private const int ROW_H = 18;

    [Fact]
    public void TheFirstEntryIsSelected()
    {
        var dropDown = new DropDown(["Human", "Elf"], 100, ROW_H);

        Assert.Equal(0, dropDown.SelectedIndex);
        Assert.Equal("Human", dropDown.SelectedText);
        Assert.False(dropDown.IsOpen);
    }

    [Fact]
    public void AClickOnTheBoxOpensTheListBelowIt()
    {
        var parent = new UIPanel { Width = 200, Height = 200 };
        var dropDown = new DropDown(["Human", "Elf", "Dwarf"], 100, ROW_H) { ZIndex = 2 };
        parent.AddChild(dropDown);

        ClickAt(dropDown, 0);

        Assert.True(dropDown.IsOpen);
        Assert.Equal(4 * ROW_H, dropDown.Height);

        dropDown.Close();

        //the host's ZIndex stays as it was, so the order of the siblings does not change
        Assert.Equal(ROW_H, dropDown.Height);
        Assert.Equal(2, dropDown.ZIndex);
    }

    [Fact]
    public void AnInteractionResetClosesTheList()
    {
        var dropDown = new DropDown(["Human"], 100, ROW_H);
        dropDown.Open();

        dropDown.ResetInteractionState();

        Assert.False(dropDown.IsOpen);
    }

    [Fact]
    public void ADisabledControlClosesItsOpenList()
    {
        var dropDown = new DropDown(["Human"], 100, ROW_H);
        dropDown.Open();

        dropDown.Enabled = false;
        dropDown.Update(new GameTime());

        Assert.False(dropDown.IsOpen);
    }

    [Fact]
    public void ARowHeightOfZeroIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new DropDown(["Human"], 100, 0));

    [Fact]
    public void AClickOnAnEntrySelectsItAndClosesTheList()
    {
        var dropDown = new DropDown(["Human", "Elf", "Dwarf"], 100, ROW_H);
        var selected = new List<int>();
        dropDown.SelectionChanged += selected.Add;

        ClickAt(dropDown, 0);
        ClickAt(dropDown, 2);

        Assert.False(dropDown.IsOpen);
        Assert.Equal(1, dropDown.SelectedIndex);
        Assert.Equal([1], selected);
    }

    [Fact]
    public void AClickOnTheSelectedEntryOrTheBoxClosesWithNoChange()
    {
        var dropDown = new DropDown(["Human", "Elf"], 100, ROW_H);
        var changes = 0;
        dropDown.SelectionChanged += _ => changes++;

        ClickAt(dropDown, 0);
        ClickAt(dropDown, 1);
        Assert.False(dropDown.IsOpen);

        ClickAt(dropDown, 0);
        ClickAt(dropDown, 0);
        Assert.False(dropDown.IsOpen);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void TheListClosesWhenTheMouseLeaves()
    {
        var dropDown = new DropDown(["Human"], 100, ROW_H);
        dropDown.Open();

        dropDown.OnMouseLeave();

        Assert.False(dropDown.IsOpen);
    }

    [Fact]
    public void SetSelectedRaisesNoEventAndIgnoresAnIndexOutOfRange()
    {
        var dropDown = new DropDown(["Human", "Elf"], 100, ROW_H);
        var changes = 0;
        dropDown.SelectionChanged += _ => changes++;

        dropDown.SetSelected(1);
        dropDown.SetSelected(5);
        dropDown.SetSelected(-1);

        Assert.Equal(1, dropDown.SelectedIndex);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void AnEmptyOrDisabledListDoesNotOpen()
    {
        var empty = new DropDown([], 100, ROW_H);
        empty.Open();

        Assert.False(empty.IsOpen);
        Assert.Equal(-1, empty.SelectedIndex);
        Assert.Equal(string.Empty, empty.SelectedText);

        var disabled = new DropDown(["Human"], 100, ROW_H) { Enabled = false };
        ClickAt(disabled, 0);

        Assert.False(disabled.IsOpen);
    }

    //a left click on row 0 (the box) or on list row n (row n + 1 of the open control)
    private static void ClickAt(DropDown dropDown, int row)
        => dropDown.OnClick(
            new ClickEvent
            {
                Button = MouseButton.Left,
                ScreenX = dropDown.ScreenX + 1,
                ScreenY = dropDown.ScreenY + row * ROW_H + ROW_H / 2
            });
}
