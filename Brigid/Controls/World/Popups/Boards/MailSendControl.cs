#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Boards;

/// <summary>
///     Mail compose/send panel using _nmails prefab. Provides recipient, subject, and body text entry fields. Receiver has
///     a display label (read-only) and an editable overlay. Content is a multi-line text area (480x204).
/// </summary>
public sealed class MailSendControl : PrefabPanel
{
    //the _nmails.spf[0] anchor art has a bare black scrollbar strip at (483,65), 16px wide
    //by 214px tall — measured from the rendered art; it does NOT align with the prefab
    //Content rect (21,69)-(501,273), sitting 18px inside its right edge and 4px above its
    //top. A live scrollbar is overlaid on the strip and the body text stops at its left
    //edge (matching the retail client).
    private const int SCROLLBAR_X = 483;
    private const int SCROLLBAR_Y = 65;
    private const int SCROLLBAR_HEIGHT = 214;

    //content area — multi-line body
    private readonly UITextBox BodyBox;
    private readonly ScrollBarControl BodyScrollBar;

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
            Width = SCROLLBAR_X - contentRect.X,
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

        BodyScrollBar = new ScrollBarControl
        {
            Name = "BodyScrollBar",
            X = SCROLLBAR_X,
            Y = SCROLLBAR_Y,
            Height = SCROLLBAR_HEIGHT
        };

        TextBoxScrollSync.Wire(BodyBox, BodyScrollBar);
        AddChild(BodyScrollBar);
    }

    /// <summary>
    ///     Keeps the scrollbar range and thumb in sync with the editable body as it changes.
    /// </summary>
    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        TextBoxScrollSync.Sync(BodyBox, BodyScrollBar);
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