using Brigid.Controls.Components;
using Brigid.Definitions;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Brigid.Tests;

// UITextBox keeps a process-global static FocusedTextBox, and focusing one box unfocuses any other. Test classes that
// construct focused text boxes must therefore run serially, not in parallel, or they steal focus from each other's
// in-flight boxes. This collection (DisableParallelization) groups all such classes so they never overlap.
[CollectionDefinition("UITextBoxFocus", DisableParallelization = true)]
public sealed class UITextBoxFocusCollection;

// Single-level undo/redo (one step back, one forward) in UITextBox. Contiguous same-kind edit runs (typing, deleting)
// coalesce into one undo unit; caret movement, mouse selection, and select-all break the run so a following edit is
// its own unit. A second consecutive undo is inert, and any fresh edit invalidates a pending redo.
[Collection("UITextBoxFocus")]
public class TextBoxUndoTests
{
    private static UITextBox FocusedBox(bool multiLine = false)
    {
        return new UITextBox
        {
            Width = 200,
            Height = multiLine ? 100 : 14,
            MaxLength = 1000,
            IsMultiLine = multiLine,
            IsFocused = true
        };
    }

    private static void Type(UITextBox box, string text)
    {
        foreach (var c in text)
            box.OnTextInput(new TextInputEvent { Character = c });
    }

    private static void Key(UITextBox box, Keys key, KeyModifiers mods = KeyModifiers.None) =>
        box.OnKeyDown(new KeyDownEvent { Key = key, Modifiers = mods });

    [Fact]
    public void Undo_RevertsWholeTypingBurst()
    {
        var box = FocusedBox();
        Type(box, "hello");

        box.Undo();

        Assert.Equal(string.Empty, box.Text);
    }

    [Fact]
    public void Redo_RestoresUndoneTyping()
    {
        var box = FocusedBox();
        Type(box, "hello");

        box.Undo();
        box.Redo();

        Assert.Equal("hello", box.Text);
    }

    [Fact]
    public void SecondConsecutiveUndo_IsInert()
    {
        var box = FocusedBox();
        Type(box, "world");

        box.Undo();
        box.Undo();

        Assert.Equal(string.Empty, box.Text);
    }

    [Fact]
    public void CaretMovement_StartsANewUndoGroup()
    {
        var box = FocusedBox();
        Type(box, "abc");
        Key(box, Keys.Left);   //breaks the typing run
        Type(box, "X");        //X inserted before 'c' -> "abXc"

        box.Undo();

        Assert.Equal("abc", box.Text);
    }

    [Fact]
    public void SelectAllThenType_UndoRestoresOriginal()
    {
        //regression: select-all must break the typing run, otherwise the replace coalesces past the
        //intermediate state and undo reverts to empty instead of the pre-replace text
        var box = FocusedBox();
        Type(box, "abc");
        Key(box, Keys.A, KeyModifiers.Ctrl | KeyModifiers.Shift);   //select all
        Type(box, "X");                                             //replaces selection -> "X"

        Assert.Equal("X", box.Text);

        box.Undo();

        Assert.Equal("abc", box.Text);
    }

    [Fact]
    public void SelectAllThenBackspace_UndoRestoresOriginal()
    {
        var box = FocusedBox();
        Type(box, "abc");
        Key(box, Keys.A, KeyModifiers.Ctrl | KeyModifiers.Shift);
        Key(box, Keys.Back);   //deletes the whole selection -> ""

        Assert.Equal(string.Empty, box.Text);

        box.Undo();

        Assert.Equal("abc", box.Text);
    }

    [Fact]
    public void NewEdit_InvalidatesPendingRedo()
    {
        var box = FocusedBox();
        Type(box, "abc");
        box.Undo();            //-> ""

        Type(box, "z");        //fresh edit clears redo
        box.Redo();            //should be a no-op now

        Assert.Equal("z", box.Text);
    }

    [Fact]
    public void CtrlZ_KeyBinding_Undoes()
    {
        var box = FocusedBox();
        Type(box, "hello");

        Key(box, Keys.Z, KeyModifiers.Ctrl);

        Assert.Equal(string.Empty, box.Text);
    }

    [Theory]
    [InlineData(Keys.Y, KeyModifiers.Ctrl)]                          //Ctrl+Y
    [InlineData(Keys.Z, KeyModifiers.Ctrl | KeyModifiers.Shift)]     //Ctrl+Shift+Z
    public void RedoKeyBindings_Redo(Keys key, KeyModifiers mods)
    {
        var box = FocusedBox();
        Type(box, "hello");
        Key(box, Keys.Z, KeyModifiers.Ctrl);   //undo

        Key(box, key, mods);                   //redo

        Assert.Equal("hello", box.Text);
    }

    [Fact]
    public void BackspaceRun_Undo_RestoresDeletedText()
    {
        var box = FocusedBox();
        Type(box, "hello");
        Key(box, Keys.Back);   //"hell"
        Key(box, Keys.Back);   //"hel"

        box.Undo();

        Assert.Equal("hello", box.Text);
    }

    [Fact]
    public void Cut_Undo_RestoresText()
    {
        var box = FocusedBox();
        Type(box, "abc");
        Key(box, Keys.A, KeyModifiers.Ctrl | KeyModifiers.Shift);   //select all
        Key(box, Keys.X, KeyModifiers.Ctrl);                       //cut -> ""

        Assert.Equal(string.Empty, box.Text);

        box.Undo();

        Assert.Equal("abc", box.Text);
    }

    [Fact]
    public void ReadOnlyBox_DoesNotUndo()
    {
        var box = FocusedBox();
        Type(box, "abc");
        box.IsReadOnly = true;

        Key(box, Keys.Z, KeyModifiers.Ctrl);   //gated out by !IsReadOnly

        Assert.Equal("abc", box.Text);
    }

    [Fact]
    public void ResetUndoHistory_DropsTheUndoSlot()
    {
        var box = FocusedBox();
        Type(box, "abc");

        box.ResetUndoHistory();
        box.Undo();            //nothing to undo now

        Assert.Equal("abc", box.Text);
    }

    [Fact]
    public void MultiLine_NewlineAndTyping_Undo()
    {
        var box = FocusedBox(multiLine: true);
        Type(box, "line1");
        Key(box, Keys.Enter);   //newline is its own undo unit
        Type(box, "line2");

        box.Undo();             //reverts the "line2" typing burst
        Assert.Equal("line1\n", box.Text);

        box.Undo();             //single level: second undo inert
        Assert.Equal("line1\n", box.Text);
    }
}
