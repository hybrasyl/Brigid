using Brigid.Controls.Components;
using Brigid.Definitions;
using Xunit;

namespace Brigid.Tests;

// Regression: a single-line textbox (e.g. the chat SAY input) used to reject any character that would render past
// its visible pixel width, so pasting/typing a message longer than the box could show silently dropped the tail
// (the "Go get it!" -> "Go get i" bug). Single-line boxes now scroll horizontally and accept up to MaxLength.
// Shares the UITextBoxFocus collection so it doesn't run in parallel with other classes that steal static focus.
[Collection("UITextBoxFocus")]
public class SingleLineTextBoxScrollTests
{
    private static UITextBox FocusedBox(int width, int maxLength)
    {
        var box = new UITextBox
        {
            Width = width,        // deliberately narrow: far fewer pixels than the text needs
            Height = 14,
            MaxLength = maxLength,
            IsFocused = true
        };

        return box;
    }

    private static void Type(UITextBox box, string text)
    {
        foreach (var c in text)
            box.OnTextInput(new TextInputEvent { Character = c });
    }

    [Fact]
    public void Typing_PastVisibleWidth_KeepsAllCharacters()
    {
        var box = FocusedBox(width: 40, maxLength: 255);

        Type(box, new string('x', 250));

        Assert.Equal(250, box.Text.Length);
    }

    [Fact]
    public void Typing_StopsAtMaxLength()
    {
        var box = FocusedBox(width: 40, maxLength: 255);

        Type(box, new string('x', 300));

        Assert.Equal(255, box.Text.Length);
    }

    [Fact]
    public void Typing_PreservesTrailingCharacters()
    {
        // the exact failure shape: the last chars must survive, not get clipped at the visible edge
        var box = FocusedBox(width: 40, maxLength: 255);

        Type(box, "your dream is action. Go get it!");

        Assert.EndsWith("Go get it!", box.Text);
    }
}
