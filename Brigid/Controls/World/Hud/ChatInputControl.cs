#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Data.Models;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Hud;

public enum ChatMode
{
    None,
    Normal,
    Shout,
    WhisperName,
    WhisperMessage,
    IgnoreModeSelect,
    IgnoreAdd,
    IgnoreRemove,
    Prompt
}

public sealed class ChatInputControl : UIPanel
{
    private const int MAX_WHISPER_HISTORY = 5;
    private const int MAX_SENT_HISTORY = 50;

    //deliberate conservative cap that keeps headroom under the 255-byte string8 wire cap for the server's "Name: "
    //echo prefix (~4x the old visible-width limit). Long messages scroll horizontally rather than hard-block at the
    //visible box width (~77 chars) — single-line UITextBox scrolls to follow the caret.
    private const int MAX_MESSAGE_LENGTH = 225;

    private readonly int FullWidth;
    private readonly UILabel PrefixLabel;
    private readonly UITextBox TextBox;
    private readonly List<string> WhisperHistory = [];

    //shared, in-session recall of sent messages (say/shout/whisper) navigated with Up/Down while the prompt is open
    private readonly CircularBuffer<string> SentHistory = new(MAX_SENT_HISTORY);
    private int SentHistoryIndex = -1;           //-1 = not navigating (showing the in-progress draft)
    private string SentHistoryDraft = string.Empty;

    private Action<string>? PromptCallback;
    private Color? SavedFocusedBackgroundColor;
    private int SavedMaxLength;
    private int WhisperHistoryIndex;
    private string? WhisperTarget;

    public ChatMode Mode { get; private set; }
    public bool IsFocused => TextBox.IsFocused;

    public ChatInputControl(ControlPrefabSet prefabSet)
    {
        Name = "ChatInput";

        var rect = PrefabPanel.GetRect(prefabSet, "SAY");
        X = rect.X;
        Y = rect.Y;
        Width = rect.Width;
        Height = rect.Height;
        FullWidth = rect.Width;

        PrefixLabel = new UILabel
        {
            Name = "ChatPrefix",
            X = 0,
            Y = 0,
            Width = 0,
            Height = rect.Height,
            BackgroundColor = Color.Black,
            PaddingLeft = 1,
            PaddingTop = 1,
            ShrinkToFit = false,
            Visible = false
        };

        AddChild(PrefixLabel);

        TextBox = new UITextBox
        {
            Name = "ChatTextBox",
            X = 0,
            Y = 0,
            Width = rect.Width,
            Height = rect.Height,
            MaxLength = MAX_MESSAGE_LENGTH,
            PaddingLeft = 1,
            PaddingRight = 1,
            PaddingTop = 1,
            PaddingBottom = 1,
            FocusedBackgroundColor = new Color(0, 0, 0, 160)
        };

        AddChild(TextBox);

        //register the chat textbox so popups don't tear keyboard focus away while typing.
        if (InputDispatcher.Instance is { } dispatcher)
            dispatcher.ChatInputTextBox = TextBox;
    }

    //--- events ---

    public event MessageSentHandler? MessageSent;
    public event ShoutSentHandler? ShoutSent;
    public event WhisperSentHandler? WhisperSent;
    public event IgnoreAddedHandler? IgnoreAdded;
    public event IgnoreRemovedHandler? IgnoreRemoved;
    public event IgnoreListRequestedHandler? IgnoreListRequested;
    public event FocusChangedHandler? FocusChanged;

    //--- layout ---

    private void UpdateLayout(string prefix, Color color)
    {
        if (prefix.Length == 0)
        {
            PrefixLabel.Visible = false;
            TextBox.X = 0;
            TextBox.Width = FullWidth;

            return;
        }

        var prefixWidth = TextRenderer.MeasureWidth(prefix) + PrefixLabel.PaddingLeft;
        PrefixLabel.Text = prefix;
        PrefixLabel.ForegroundColor = color;
        PrefixLabel.Width = prefixWidth;
        PrefixLabel.Visible = true;

        TextBox.X = prefixWidth;
        TextBox.Width = FullWidth - prefixWidth;
    }

    //--- focus methods ---

    private void FocusInternal(ChatMode mode, string prefix, Color color)
    {
        Mode = mode;
        UpdateLayout(prefix, color);
        TextBox.ForegroundColor = color;
        TextBox.IsFocused = true;
        FocusChanged?.Invoke(true);
    }

    public void Focus(string prefix, Color color)
    {
        ChatMode mode;

        if (prefix.EndsWithI("! "))
            mode = ChatMode.Shout;
        else if (prefix.StartsWithI("-> ") && prefix.EndsWithI(": "))
        {
            mode = ChatMode.WhisperMessage;
            WhisperTarget = prefix[3..^2];
        } else
            mode = ChatMode.Normal;

        FocusInternal(mode, prefix, color);
    }

    public void FocusWhisper()
    {
        WhisperHistoryIndex = 0;
        var defaultName = WhisperHistory.Count > 0 ? WhisperHistory[0] : string.Empty;
        FocusInternal(ChatMode.WhisperName, $"to [{defaultName}]? ", TextColors.Whisper);
    }

    public void FocusIgnore()
    {
        FocusInternal(ChatMode.IgnoreModeSelect, "a: add, d: delete, ?: see list>", TextColors.Default);
        TextBox.IsReadOnly = true;
    }

    public void ShowPrompt(string prefix, int maxLength, Action<string> onConfirm)
    {
        PromptCallback = onConfirm;
        SavedMaxLength = TextBox.MaxLength;
        SavedFocusedBackgroundColor = TextBox.FocusedBackgroundColor;

        TextBox.MaxLength = maxLength;
        TextBox.FocusedBackgroundColor = Color.White;
        TextBox.BackgroundColor = Color.White;
        TextBox.ForegroundColor = Color.Black;

        Mode = ChatMode.Prompt;
        PrefixLabel.BackgroundColor = Color.White;
        UpdateLayout(prefix, Color.Black);
        TextBox.Text = string.Empty;
        TextBox.IsFocused = true;
        FocusChanged?.Invoke(true);
    }

    public void Unfocus()
    {
        Mode = ChatMode.None;
        WhisperTarget = null;
        SentHistoryIndex = -1;
        SentHistoryDraft = string.Empty;
        TextBox.IsReadOnly = false;
        TextBox.IsFocused = false;
        TextBox.Text = string.Empty;
        TextBox.ForegroundColor = Color.White;
        UpdateLayout(string.Empty, Color.White);
        InputDispatcher.Instance?.ClearExplicitFocus();
        FocusChanged?.Invoke(false);
    }

    public void SetText(string text, int cursorPosition)
    {
        TextBox.Text = text;
        TextBox.CursorPosition = cursorPosition;
        TextBox.ClearSelection();
    }

    private void RestoreFromPrompt()
    {
        PromptCallback = null;
        TextBox.MaxLength = SavedMaxLength;
        TextBox.FocusedBackgroundColor = SavedFocusedBackgroundColor;
        TextBox.BackgroundColor = null;
        PrefixLabel.BackgroundColor = Color.Black;
    }

    //--- whisper history ---

    private void AddWhisperTarget(string name)
    {
        WhisperHistory.Remove(name);
        WhisperHistory.Insert(0, name);

        if (WhisperHistory.Count > MAX_WHISPER_HISTORY)
            WhisperHistory.RemoveAt(WhisperHistory.Count - 1);
    }

    private void CycleWhisperTarget(int direction)
    {
        if ((WhisperHistory.Count == 0) || (Mode != ChatMode.WhisperName))
            return;

        WhisperHistoryIndex = (WhisperHistoryIndex + direction + WhisperHistory.Count) % WhisperHistory.Count;
        UpdateLayout($"to [{WhisperHistory[WhisperHistoryIndex]}]? ", TextBox.ForegroundColor);
    }

    //--- sent-message history ---

    private void AddSentHistory(string message)
    {
        if (message.Length == 0)
            return;

        //skip consecutive duplicates, like a shell history
        if ((SentHistory.Count > 0) && (SentHistory[SentHistory.Count - 1] == message))
            return;

        SentHistory.Add(message);
    }

    //direction: -1 = older (Up), +1 = newer (Down). Preserves the in-progress draft the first time recall begins and
    //restores it when the user pages back past the newest entry. Relies on the buffer staying stable while the prompt is
    //focused — every send path calls Unfocus() (which resets the index), so no Add can shift the ring mid-navigation.
    private void RecallSentHistory(int direction)
    {
        if (SentHistory.Count == 0)
        {
            //the TextBox already moved the caret to column 0 (Up) during dispatch; snap it back to the end so a
            //following keystroke appends rather than prepends
            SnapCaretToEnd();

            return;
        }

        if (SentHistoryIndex == -1)
        {
            //Down does nothing until recall has started
            if (direction > 0)
                return;

            SentHistoryDraft = TextBox.Text;
            SentHistoryIndex = SentHistory.Count - 1;
        } else
        {
            var next = SentHistoryIndex + direction;

            //already at the oldest entry — stay put, but re-assert the caret (Up moved it to column 0 during dispatch)
            if (next < 0)
            {
                SnapCaretToEnd();

                return;
            }

            //paged past the newest entry — restore the preserved draft
            if (next >= SentHistory.Count)
            {
                SentHistoryIndex = -1;
                SetText(SentHistoryDraft, SentHistoryDraft.Length);
                SentHistoryDraft = string.Empty;

                return;
            }

            SentHistoryIndex = next;
        }

        var recalled = SentHistory[SentHistoryIndex];
        SetText(recalled, recalled.Length);
    }

    //re-assert the caret at the end after the TextBox's own Up/Down dispatch moved it; ClearSelection re-syncs the
    //anchor so the recalled/draft text isn't left fully selected (which the next keystroke would replace).
    private void SnapCaretToEnd()
    {
        TextBox.CursorPosition = TextBox.Text.Length;
        TextBox.ClearSelection();
    }

    private string GetBracketedWhisperTarget()
    {
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        var prefix = PrefixLabel.Text ?? string.Empty;
        var start = prefix.IndexOf('[') + 1;
        var end = prefix.IndexOf(']');

        if ((start <= 0) || (end < start))
            return string.Empty;

        return prefix[start..end];
    }

    //--- input handling ---

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Enter)
        {
            HandleEnter();
            e.Handled = true;

            return;
        }

        if (e.Key == Keys.Escape)
        {
            HandleEscape();
            e.Handled = true;
        }
    }

    private void HandleEnter()
    {
        var message = TextBox.Text.Trim();

        switch (Mode)
        {
            case ChatMode.Normal:
                AddSentHistory(message);
                MessageSent?.Invoke(message);
                Unfocus();

                break;

            case ChatMode.Shout:
                AddSentHistory(message);
                ShoutSent?.Invoke(message);
                Unfocus();

                break;

            case ChatMode.IgnoreModeSelect:
                Unfocus();

                break;

            case ChatMode.IgnoreAdd:
                if (message.Length > 0)
                    IgnoreAdded?.Invoke(message);

                Unfocus();

                break;

            case ChatMode.IgnoreRemove:
                if (message.Length > 0)
                    IgnoreRemoved?.Invoke(message);

                Unfocus();

                break;

            case ChatMode.WhisperName:
                var targetName = message.Length > 0 ? message : GetBracketedWhisperTarget();

                if (targetName.Length > 0)
                {
                    WhisperTarget = targetName;
                    Mode = ChatMode.WhisperMessage;
                    UpdateLayout($"-> {targetName}: ", TextBox.ForegroundColor);
                    TextBox.Text = string.Empty;
                }

                break;

            case ChatMode.WhisperMessage:
                if (WhisperTarget is not null)
                {
                    AddWhisperTarget(WhisperTarget);
                    AddSentHistory(message);
                    WhisperSent?.Invoke(WhisperTarget, message);
                }

                Unfocus();

                break;

            case ChatMode.Prompt:
                var callback = PromptCallback;
                var text = TextBox.Text;
                RestoreFromPrompt();
                Unfocus();
                callback?.Invoke(text);

                break;
        }
    }

    private void HandleEscape()
    {
        if (Mode == ChatMode.Prompt)
            RestoreFromPrompt();

        Unfocus();
    }

    public override void OnTextInput(TextInputEvent e)
    {
        if (Mode != ChatMode.IgnoreModeSelect)
            return;

        switch (e.Character)
        {
            case 'a' or 'A':
                Mode = ChatMode.IgnoreAdd;
                TextBox.IsReadOnly = false;
                UpdateLayout("ID of people you wish to reject whisper >", TextBox.ForegroundColor);
                TextBox.Text = string.Empty;
                e.Handled = true;

                break;

            case 'd' or 'D':
                Mode = ChatMode.IgnoreRemove;
                TextBox.IsReadOnly = false;
                UpdateLayout("ID of people you wish to cancel rejection of whisper >", TextBox.ForegroundColor);
                TextBox.Text = string.Empty;
                e.Handled = true;

                break;

            case '?':
                IgnoreListRequested?.Invoke();
                Unfocus();
                e.Handled = true;

                break;

            default:
                e.Handled = true;

                break;
        }
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (!IsFocused)
            return;

        //Up/Down cycle whisper recipients while choosing a target; in text-entry modes they recall sent messages.
        //Polled here (not via OnKeyDown) because the single-line TextBox consumes Up/Down in Phase 1; Home/End still
        //provide caret-to-start/end so nothing is lost by repurposing them here.
        if (Mode == ChatMode.WhisperName)
        {
            if (InputBuffer.WasKeyPressed(Keys.Up))
                CycleWhisperTarget(1);
            else if (InputBuffer.WasKeyPressed(Keys.Down))
                CycleWhisperTarget(-1);

            return;
        }

        if (Mode is ChatMode.Normal or ChatMode.Shout or ChatMode.WhisperMessage)
        {
            //WasKeyPressed surfaces OS key-repeat, so holding Up/Down intentionally walks through history shell-style;
            //recall parks at the oldest entry / restores the draft, so there's no wrap or runaway.
            if (InputBuffer.WasKeyPressed(Keys.Up))
                RecallSentHistory(-1);
            else if (InputBuffer.WasKeyPressed(Keys.Down))
                RecallSentHistory(1);
        }
    }
}