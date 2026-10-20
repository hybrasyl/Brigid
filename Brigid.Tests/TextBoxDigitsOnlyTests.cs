using Brigid.Controls.Components;
using Brigid.Definitions;
using Xunit;

namespace Brigid.Tests;

// UITextBox.DigitsOnly (BRIG-122): a typed character that is not a digit does nothing, and text that code sets is not
// filtered. A box without the option takes every character, as before.
[Collection("UITextBoxFocus")]
public class TextBoxDigitsOnlyTests
{
    private static UITextBox FocusedBox(bool digitsOnly)
        => new()
        {
            Width = 200,
            Height = 14,
            MaxLength = 20,
            DigitsOnly = digitsOnly,
            IsFocused = true
        };

    private static void Type(UITextBox box, string text)
    {
        foreach (var c in text)
            box.OnTextInput(new TextInputEvent { Character = c });
    }

    [Fact]
    public void ADigitsOnlyBoxTakesOnlyDigits()
    {
        var box = FocusedBox(true);

        Type(box, "1,000 x 2");

        Assert.Equal("10002", box.Text);
    }

    [Fact]
    public void ADigitsOnlyBoxDoesNotFilterCodeText()
    {
        var box = FocusedBox(true);
        box.Text = "abc";

        Assert.Equal("abc", box.Text);
    }

    [Fact]
    public void AnOrdinaryBoxTakesEveryCharacter()
    {
        var box = FocusedBox(false);

        Type(box, "1,000 x 2");

        Assert.Equal("1,000 x 2", box.Text);
    }
}
