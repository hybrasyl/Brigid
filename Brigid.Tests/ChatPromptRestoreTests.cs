using Brigid.Controls.Components;
using Brigid.Controls.World.Hud;
using Brigid.Data.Models;
using Brigid.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Brigid.Tests;

// A chat prompt (the drop-count prompt) makes the chat box white and sets a short MaxLength. Only Enter and Escape put
// the box back, so F9 or the focus self-heal left a white chat box that took 12 characters. A second prompt also saved
// the first prompt's values as the ones to restore. Every exit now restores the fixed chat values.
//
// The prompts here have no prefix text: a prefix is measured, which needs the FontEngine collection, and the prefix has
// no part in the restore.
[Collection("UITextBoxFocus")]
public class ChatPromptRestoreTests
{
    private const int CHAT_MAX_LENGTH = 255;
    private const int PROMPT_MAX_LENGTH = 12;
    private static readonly Color ChatBackground = new(0, 0, 0, 160);

    private static (ChatInputControl Chat, UITextBox Box) PromptOpen(int prompts = 1)
    {
        var chat = new ChatInputControl(new ControlPrefabSet("_test"));

        for (var i = 0; i < prompts; i++)
            chat.ShowPrompt(string.Empty, PROMPT_MAX_LENGTH, _ => { });

        return (chat, chat.FindChild<UITextBox>("ChatTextBox")!);
    }

    private static void AssertChatValues(ChatInputControl chat, UITextBox box)
    {
        Assert.Equal(ChatMode.None, chat.Mode);
        Assert.Equal(CHAT_MAX_LENGTH, box.MaxLength);
        Assert.Equal(ChatBackground, box.FocusedBackgroundColor);
        Assert.Null(box.BackgroundColor);
    }

    [Fact]
    public void Unfocus_RestoresTheChatBoxWhenAPromptIsOpen()
    {
        var (chat, box) = PromptOpen();

        chat.Unfocus();

        AssertChatValues(chat, box);
    }

    [Fact]
    public void Unfocus_RestoresTheChatBoxAfterASecondPrompt()
    {
        var (chat, box) = PromptOpen(prompts: 2);

        chat.Unfocus();

        AssertChatValues(chat, box);
    }

    [Fact]
    public void Focus_RestoresTheChatBoxWhenAPromptIsOpen()
    {
        var (chat, box) = PromptOpen();
        box.OnTextInput(new TextInputEvent { Character = '7' });

        chat.Focus(string.Empty, Color.White);

        Assert.Equal(ChatMode.Normal, chat.Mode);
        Assert.Equal(string.Empty, box.Text);
        Assert.Equal(CHAT_MAX_LENGTH, box.MaxLength);
        Assert.Equal(ChatBackground, box.FocusedBackgroundColor);
    }

    [Fact]
    public void OnKeyDown_EscapeRestoresTheChatBox()
    {
        var (chat, box) = PromptOpen();

        chat.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });

        AssertChatValues(chat, box);
    }

    [Fact]
    public void OnKeyDown_EnterSendsThePromptTextAndRestoresTheChatBox()
    {
        var chat = new ChatInputControl(new ControlPrefabSet("_test"));
        string? sent = null;
        chat.ShowPrompt(string.Empty, PROMPT_MAX_LENGTH, text => sent = text);
        var box = chat.FindChild<UITextBox>("ChatTextBox")!;

        box.OnTextInput(new TextInputEvent { Character = '7' });
        chat.OnKeyDown(new KeyDownEvent { Key = Keys.Enter });

        Assert.Equal("7", sent);
        AssertChatValues(chat, box);
    }
}
