#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Popups.Profile;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-122 profile text editor: every child has a lowercase name, the text box keeps the legacy size and limits,
///     Save raises the text and hides the editor, and Cancel and Escape hide it with no save.
/// </summary>
[Collection("FontEngine")]
public class ProfileTextEditorTests
{
    public ProfileTextEditorTests() => FontEngine.Initialize(0);

    [Fact]
    public void EveryChildHasAUniqueName() => UiAssert.NamedTree(new SelfProfileTextEditorControl());

    [Fact]
    public void TheTextBoxKeepsTheLegacySizeAndLimits()
    {
        var editor = new SelfProfileTextEditorControl();
        var box = editor.FindChild<UITextBox>("text")!;

        Assert.Equal(174, box.Width);
        Assert.Equal(152, box.Height);
        Assert.Equal(256, box.MaxLength);
        Assert.True(box.IsMultiLine);
        Assert.True(box.ClampToVisibleArea);
        Assert.True(editor.IsModal);
    }

    [Fact]
    public void ShowFocusesTheBoxAndHideClearsTheFocus()
    {
        var editor = new SelfProfileTextEditorControl();
        var box = editor.FindChild<UITextBox>("text")!;

        editor.Show("Text.");

        Assert.True(box.IsFocused);

        editor.Hide();

        Assert.False(box.IsFocused);
    }

    [Fact]
    public void EnterStartsANewLineAndTheBoxRefusesAThirteenthLine()
    {
        var editor = new SelfProfileTextEditorControl();
        var saves = 0;
        editor.OnSave += _ => saves++;
        editor.Show(string.Empty);
        var box = editor.FindChild<UITextBox>("text")!;

        //the box shows 12 lines; a newline that would start a 13th is refused
        for (var i = 0; i < 13; i++)
        {
            box.OnTextInput(new TextInputEvent { Character = 'a' });
            box.OnTextInput(new TextInputEvent { Character = '\r' });
        }

        Assert.Equal(11, box.Text.Count(c => c == '\n'));
        Assert.True(editor.Visible);
        Assert.Equal(0, saves);
        editor.Hide();
    }

    //BRIG-100: a text saved before the wrap width changed can be one line past the 12 that the box shows. It can still be
    //edited: an edit that adds no line and a delete work; only an edit that adds a line is refused
    [Fact]
    public void AStoredTextPastTheVisibleAreaCanStillBeEdited()
    {
        var editor = new SelfProfileTextEditorControl();
        var stored = string.Join('\n', Enumerable.Repeat("a", 13));
        editor.Show(stored);
        var box = editor.FindChild<UITextBox>("text")!;
        box.CursorPosition = box.Text.Length;
        box.ClearSelection();

        box.OnTextInput(new TextInputEvent { Character = 'b' });

        //the stored text is past the 12 lines that the box shows, and the edit still went in
        Assert.True(box.LineCount > box.VisibleLineCount);
        Assert.Equal(stored + "b", box.Text);

        //a paste that adds no line works too; Ctrl is the primary modifier, as InputBuffer reports it on Windows
        var clipboard = TestClipboard.Instance.Text;
        TestClipboard.Instance.Text = "c";

        try
        {
            box.OnKeyDown(new KeyDownEvent { Key = Keys.V, Modifiers = KeyModifiers.Ctrl | KeyModifiers.Meta });
        } finally
        {
            TestClipboard.Instance.Text = clipboard;
        }

        Assert.Equal(stored + "bc", box.Text);

        box.OnKeyDown(new KeyDownEvent { Key = Keys.Back });

        box.OnTextInput(new TextInputEvent { Character = '\r' });

        Assert.Equal(stored + "b", box.Text);

        box.OnKeyDown(new KeyDownEvent { Key = Keys.Back });

        Assert.Equal(stored, box.Text);
        editor.Hide();
    }

    //the line count before an edit comes from the text before the edit, also when that text is empty: an empty text at an
    //unchanged width must not take the line layout that the box kept from its last text
    [Fact]
    public void APasteIntoAnEmptiedBoxIsCheckedAgainstTheEmptyText()
    {
        var editor = new SelfProfileTextEditorControl();
        var overflowing = string.Join('\n', Enumerable.Repeat("a", 13));
        editor.Show(overflowing);
        var box = editor.FindChild<UITextBox>("text")!;
        box.CursorPosition = box.Text.Length;
        box.ClearSelection();

        //a keystroke lays out the 13 lines, which the box keeps
        box.OnTextInput(new TextInputEvent { Character = 'b' });
        Assert.True(box.LineCount > box.VisibleLineCount);

        box.Text = string.Empty;
        box.CursorPosition = 0;
        box.ClearSelection();

        var clipboard = TestClipboard.Instance.Text;
        TestClipboard.Instance.Text = overflowing;

        try
        {
            box.OnKeyDown(new KeyDownEvent { Key = Keys.V, Modifiers = KeyModifiers.Ctrl | KeyModifiers.Meta });
        } finally
        {
            TestClipboard.Instance.Text = clipboard;
        }

        //the paste makes 13 lines from none, past the 12 that the box shows, so it is refused
        Assert.Equal(string.Empty, box.Text);
        editor.Hide();
    }

    [Fact]
    public void SaveRaisesTheTextAndHidesTheEditor()
    {
        var editor = new SelfProfileTextEditorControl();
        string? saved = null;
        editor.OnSave += text => saved = text;
        editor.Show("Hello.");

        editor.FindChild<TextButton>("save")!.OnClick(new ClickEvent { Button = MouseButton.Left });

        Assert.Equal("Hello.", saved);
        Assert.False(editor.Visible);
    }

    [Fact]
    public void CancelAndEscapeHideWithNoSave()
    {
        var editor = new SelfProfileTextEditorControl();
        var saves = 0;
        editor.OnSave += _ => saves++;

        editor.Show("Text.");
        editor.FindChild<TextButton>("cancel")!.OnClick(new ClickEvent { Button = MouseButton.Left });

        Assert.False(editor.Visible);

        editor.Show("Text.");
        var e = new KeyDownEvent { Key = Keys.Escape };
        editor.OnKeyDown(e);

        Assert.True(e.Handled);
        Assert.False(editor.Visible);
        Assert.Equal(0, saves);
    }
}
