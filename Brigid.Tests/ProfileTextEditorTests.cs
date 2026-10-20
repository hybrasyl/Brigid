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
