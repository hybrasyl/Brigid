#region
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
    private const int MAX_MESSAGE_HISTORY = 20;

    //retail (Kru) Dark Ages caps the say/shout/whisper input field at 55 characters (empirically verified against the
    //retail client — a flat cap, independent of the "Name: " echo prefix / player-name length). On those servers we
    //hold that limit so we don't emit messages a retail client can't enter; other servers keep the 255-byte string8
    //wire max. Only the cap changes — the editing/history behavior is identical. Gated on GlobalSettings.IsCursed
    //(host ends in kru.com).
    private const int RETAIL_MESSAGE_LENGTH = 55;

    private readonly int FullWidth;
    private readonly List<string> MessageHistory = [];
    private readonly UILabel PrefixLabel;
    private readonly UITextBox TextBox;
    private readonly List<string> WhisperHistory = [];

    //in-progress text preserved when cycling into history, restored when cycling back past newest
    private string DraftMessage = string.Empty;

    //-1 = not cycling (live input / draft); 0 = most recent sent message
    private int MessageHistoryIndex = -1;
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
            MaxLength = GlobalSettings.IsCursed ? RETAIL_MESSAGE_LENGTH : 255,
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
        //claim the sticky-chat registration for this HUD's box: both HUD implementations construct
        //a ChatInputControl, and construction order must not decide which box popups treat as the
        //protected chat input — the one actually being focused is the real one
        if (InputDispatcher.Instance is { } dispatcher)
            dispatcher.ChatInputTextBox = TextBox;

        Mode = mode;
        MessageHistoryIndex = -1;
        DraftMessage = string.Empty;
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
        TextBox.IsReadOnly = false;
        TextBox.IsFocused = false;
        TextBox.Text = string.Empty;
        TextBox.ForegroundColor = Color.White;
        UpdateLayout(string.Empty, Color.White);

        //only release explicit focus we actually hold — when unfocusing because keyboard routing
        //moved elsewhere (self-heal path), clearing would steal focus from its rightful owner
        if (InputDispatcher.Instance is { } dispatcher && (dispatcher.ExplicitFocus == TextBox))
            dispatcher.ClearExplicitFocus();

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

    //--- message history ---

    //bounded MRU shared by message and whisper history: newest first, dedup by move-to-front, oldest trimmed
    private static void AddMru(List<string> list, string entry, int max)
    {
        if (entry.Length == 0)
            return;

        list.Remove(entry);
        list.Insert(0, entry);

        if (list.Count > max)
            list.RemoveAt(list.Count - 1);
    }

    private void AddMessageHistory(string message) => AddMru(MessageHistory, message, MAX_MESSAGE_HISTORY);

    /// <summary>
    ///     Cycles the input through previously sent messages. Positive direction = older; stepping past the
    ///     newest entry restores whatever was typed before cycling began.
    /// </summary>
    private void CycleMessageHistory(int direction)
    {
        if (MessageHistory.Count == 0)
            return;

        var newIndex = Math.Clamp(MessageHistoryIndex + direction, -1, MessageHistory.Count - 1);

        if (newIndex == MessageHistoryIndex)
            return;

        //entering history preserves the in-progress draft
        if (MessageHistoryIndex < 0)
            DraftMessage = TextBox.Text;

        MessageHistoryIndex = newIndex;
        var text = newIndex < 0 ? DraftMessage : MessageHistory[newIndex];
        SetText(text, text.Length);
    }

    //--- whisper history ---

    private void AddWhisperTarget(string name) => AddMru(WhisperHistory, name, MAX_WHISPER_HISTORY);

    private void CycleWhisperTarget(int direction)
    {
        if ((WhisperHistory.Count == 0) || (Mode != ChatMode.WhisperName))
            return;

        WhisperHistoryIndex = (WhisperHistoryIndex + direction + WhisperHistory.Count) % WhisperHistory.Count;
        UpdateLayout($"to [{WhisperHistory[WhisperHistoryIndex]}]? ", TextBox.ForegroundColor);
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

    public override void OnMouseDown(MouseDownEvent e)
    {
        //clicking anywhere on the chat bar while it is active reclaims keyboard focus — the escape
        //hatch for a focus desync (dispatcher routing lost while the box still shows a caret). the
        //redundant-set path in UITextBox.IsFocused re-fires the dispatcher bridge even when the
        //box already believes it is focused.
        if ((e.Button == MouseButton.Left) && (Mode != ChatMode.None))
            TextBox.IsFocused = true;
    }

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
                AddMessageHistory(message);
                MessageSent?.Invoke(message);
                Unfocus();

                break;

            case ChatMode.Shout:
                AddMessageHistory(message);
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
                    AddMessageHistory(message);
                    AddWhisperTarget(WhisperTarget);
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

        //self-heal a focus desync: the bar renders active (Mode set) but the box lost focus without
        //going through Unfocus. The bar is hit-invisible while unfocused (by design — idle clicks
        //fall through to the world), so no click can ever repair this state; the control must.
        if ((Mode != ChatMode.None) && !TextBox.IsFocused)
        {
            //keyboard genuinely owned elsewhere (another textbox, or a panel holding explicit
            //focus): stop pretending to be active. Otherwise nothing owns it — reclaim.
            if ((UITextBox.CurrentlyFocused is not null) || (InputDispatcher.Instance?.ExplicitFocus is not null))
                Unfocus();
            else
                TextBox.IsFocused = true;
        }

        if (!IsFocused)
            return;

        switch (Mode)
        {
            case ChatMode.WhisperName:
                if (InputBuffer.WasKeyPressed(Keys.Up))
                    CycleWhisperTarget(1);
                else if (InputBuffer.WasKeyPressed(Keys.Down))
                    CycleWhisperTarget(-1);

                break;

            case ChatMode.Normal or ChatMode.Shout or ChatMode.WhisperMessage:
                if (InputBuffer.WasKeyPressed(Keys.Up))
                    CycleMessageHistory(1);
                else if (InputBuffer.WasKeyPressed(Keys.Down))
                    CycleMessageHistory(-1);

                break;
        }
    }
}