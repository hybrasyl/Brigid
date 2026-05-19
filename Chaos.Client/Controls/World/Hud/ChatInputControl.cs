#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data.Models;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Hud;

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

    private readonly int FullWidth;
    private readonly UILabel PrefixLabel;
    private readonly UITextBox TextBox;
    private readonly List<string> WhisperHistory = [];

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
            TruncateWithEllipsis = false,
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
            MaxLength = 255,
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
                MessageSent?.Invoke(message);
                Unfocus();

                break;

            case ChatMode.Shout:
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

        if ((Mode != ChatMode.WhisperName) || !IsFocused)
            return;

        if (InputBuffer.WasKeyPressed(Keys.Up))
            CycleWhisperTarget(1);
        else if (InputBuffer.WasKeyPressed(Keys.Down))
            CycleWhisperTarget(-1);
    }
}