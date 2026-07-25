#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

// A line's AUTHORED trailing spaces are text: the caret has to be able to sit after them. Dropping them made a space
// typed at the end of a line insert without the caret moving, and made End walk the caret backwards over spaces the
// user had just typed. Only a soft wrap's boundary spaces are dropped, because the wrapper consumed them into the
// previous line's range and they belong to the break.
//
// Coverage limit: ComputeLineLayout runs off the draw path, so a headless box keeps the degenerate single-line
// layout and these exercise the no-wrap case only. The soft-wrap branch (SoftLineEnds) is not reachable here.
// Shares the UITextBoxFocus collection so it doesn't run in parallel with other classes that steal static focus.
[Collection("UITextBoxFocus")]
public class TextBoxTrailingSpaceTests
{
    private static UITextBox FocusedBox()
        => new()
        {
            Width = 200,
            Height = 100,
            MaxLength = 1000,
            IsMultiLine = true,
            IsFocused = true
        };

    private static void Type(UITextBox box, string text)
    {
        foreach (var c in text)
            box.OnTextInput(new TextInputEvent { Character = c });
    }

    private static void Key(UITextBox box, Keys key, KeyModifiers mods = KeyModifiers.None)
    {
        if ((mods & KeyModifiers.Ctrl) != 0)
            mods |= KeyModifiers.Meta;

        box.OnKeyDown(new KeyDownEvent { Key = key, Modifiers = mods });
    }

    [Fact]
    public void TypingTrailingSpace_AdvancesTheCaret()
    {
        var box = FocusedBox();

        Type(box, "abc ");

        Assert.Equal("abc ", box.Text);
        Assert.Equal(4, box.CursorPosition);
    }

    [Fact]
    public void End_KeepsTheCaretAfterAuthoredTrailingSpaces()
    {
        var box = FocusedBox();
        Type(box, "abc  ");

        Key(box, Keys.Home);
        Key(box, Keys.End);

        //End must reach the real end of the line, not the last non-space character
        Assert.Equal(5, box.CursorPosition);
    }
}
