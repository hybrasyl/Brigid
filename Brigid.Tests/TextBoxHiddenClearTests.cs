using Brigid.Controls.Components;
using Brigid.Definitions;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Brigid.Tests;

// UITextBox clamps its cursor in Update, and Update skips a hidden box. A panel that clears its text box while hidden
// (login, password change, gold and item amounts) left the cursor past the end, and the next key threw in Text.Insert
// or Text.Remove. The key and text handlers now clamp first.
[Collection("UITextBoxFocus")]
public class TextBoxHiddenClearTests
{
    private static UITextBox ClearedWhileHidden()
    {
        var box = new UITextBox
        {
            Width = 200,
            Height = 14,
            MaxLength = 100,
            IsFocused = true
        };

        foreach (var c in "password")
            box.OnTextInput(new TextInputEvent { Character = c });

        box.Visible = false;
        box.Text = string.Empty;
        box.Visible = true;

        return box;
    }

    [Fact]
    public void OnTextInput_InsertsAtTheEndAfterTextIsClearedWhileHidden()
    {
        var box = ClearedWhileHidden();

        box.OnTextInput(new TextInputEvent { Character = 'a' });

        Assert.Equal("a", box.Text);
        Assert.Equal(1, box.CursorPosition);
    }

    [Theory]
    [InlineData(Keys.Back)]
    [InlineData(Keys.Delete)]
    [InlineData(Keys.Left)]
    [InlineData(Keys.Right)]
    public void OnKeyDown_UsesACursorInsideTheTextAfterTextIsClearedWhileHidden(Keys key)
    {
        var box = ClearedWhileHidden();

        box.OnKeyDown(new KeyDownEvent { Key = key });

        Assert.Equal(string.Empty, box.Text);
        Assert.Equal(0, box.CursorPosition);
    }
}
