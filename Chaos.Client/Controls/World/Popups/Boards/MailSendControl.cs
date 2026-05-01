#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Boards;

/// <summary>
///     Mail compose/send panel using _nmails prefab. Provides recipient, subject, and body text entry fields. Receiver has
///     a display label (read-only) and an editable overlay. Content is a multi-line text area (480x204).
/// </summary>
public sealed class MailSendControl : PrefabPanel
{
    //content area — multi-line body
    private readonly UITextBox BodyBox;

    //receiver — editable overlay
    private readonly UITextBox? ReceiverEditBox;

    //subject
    private readonly UITextBox? TitleBox;
    private int TargetX;

    public ushort BoardId { get; set; }
    public UIButton? CancelButton { get; }
    public UIButton? SendButton { get; }

    public MailSendControl()
        : base("_nmails", false)
    {
        Name = "MailSend";
        Visible = false;
        UsesControlStack = true;

        SendButton = CreateButton("Send");
        CancelButton = CreateButton("Cancel");

        if (SendButton is not null)
            SendButton.Clicked += HandleSend;

        if (CancelButton is not null)
            CancelButton.Clicked += () =>
            {
                Hide();
                OnCancel?.Invoke();
            };

        CreateLabel("Receiver");
        ReceiverEditBox = CreateTextBox("ReceiverEdit", 24);
        ReceiverEditBox?.ForegroundColor = LegendColors.White;
        ReceiverEditBox?.IsTabStop = true;
        
        TitleBox = CreateTextBox("Title", 60);
        TitleBox?.ForegroundColor = LegendColors.White;
        TitleBox?.IsTabStop = true;

        //content rect for multi-line body text entry
        var contentRect = GetRect("Content");

        BodyBox = new UITextBox
        {
            X = contentRect.X,
            Y = contentRect.Y,
            Width = contentRect.Width - 2,
            Height = contentRect.Height,
            IsMultiLine = true,
            IsSelectable = true,
            MaxLength = 10000,
            PaddingLeft = 0,
            PaddingRight = 0,
            PaddingTop = 0,
            PaddingBottom = 0,
            ForegroundColor = TextColors.Default,
            IsTabStop = true
        };

        AddChild(BodyBox);
    }

    private void HandleSend()
    {
        var recipient = ReceiverEditBox?.Text ?? string.Empty;
        var subject = TitleBox?.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(recipient))
            return;

        OnSend?.Invoke(recipient, subject, BodyBox.Text);
    }

    public override void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
    }

    public event CancelHandler? OnCancel;

    public event MailSendHandler? OnSend; //recipient, subject, body

    public void SetViewportBounds(Rectangle viewport)
    {
        TargetX = viewport.X + viewport.Width - Width;
        Y = viewport.Y;
    }

    public override void Show()
    {
        X = TargetX;
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
    }

    /// <summary>
    ///     Shows the compose dialog, optionally pre-filling the recipient.
    /// </summary>
    public void ShowCompose(string? recipient = null)
    {
        var isReply = !string.IsNullOrEmpty(recipient);

        if (ReceiverEditBox is not null)
        {
            ReceiverEditBox.Text = recipient ?? string.Empty;
            ReceiverEditBox.IsReadOnly = isReply;
            ReceiverEditBox.ForegroundColor = isReply ? TextColors.Default : LegendColors.White;
            ReceiverEditBox.IsTabStop = !isReply;
            ReceiverEditBox.IsFocused = !isReply;
        }

        TitleBox?.Text = string.Empty;

        BodyBox.Text = string.Empty;
        BodyBox.ScrollOffset = 0;
        BodyBox.CursorPosition = 0;

        Show();

        if (isReply)
            TitleBox?.IsFocused = true;
        else
        {
            ReceiverEditBox?.IsFocused = true;
        }
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Keys.Escape:
                Hide();
                OnCancel?.Invoke();
                e.Handled = true;

                break;

            case Keys.Tab:
                if (ReceiverEditBox?.IsFocused == true)
                {
                    ReceiverEditBox.IsFocused = false;

                    if (TitleBox is not null)
                        TitleBox.IsFocused = true;
                    else
                        BodyBox.IsFocused = true;

                    e.Handled = true;
                } else if (TitleBox?.IsFocused == true)
                {
                    TitleBox.IsFocused = false;
                    BodyBox.IsFocused = true;
                    e.Handled = true;
                }

                break;

            case Keys.Enter when ReceiverEditBox?.IsFocused == true:
                ReceiverEditBox.IsFocused = false;

                if (TitleBox is not null)
                    TitleBox.IsFocused = true;
                else
                    BodyBox.IsFocused = true;

                e.Handled = true;

                break;
        }
    }
}