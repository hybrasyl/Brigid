#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Boards;

/// <summary>
///     The compose view — the single pane replacing both <c>ArticleSendControl</c> (<c>_nartin</c>) and
///     <c>MailSendControl</c> (<c>_nmails</c>). A recipient box (mail only), a subject box, and a multi-line body
///     with its own scrollbar. The legacy panels overlaid their bar on a black strip baked into the art at a
///     hardcoded (483, 65, 214); here the bar is laid out from the pane rect like any other control.
/// </summary>
internal sealed class PostComposePane : InputSlotPane
{
    private const int FIELD_H = 20;
    private const int FIELD_GAP = 6;
    private const int LABEL_W = 64;
    private const int RECIPIENT_MAX_LENGTH = 24;
    private const int SUBJECT_MAX_LENGTH = 60;
    private const int BODY_MAX_LENGTH = 10000;

    private readonly UILabel RecipientLabel;
    private readonly UITextBox RecipientBox;
    private readonly UITextBox SubjectBox;
    private readonly UITextBox BodyBox;
    private readonly ScrollBarControl BodyScrollBar;

    /// <summary>True when composing mail (recipient row shown) rather than a bulletin-board post.</summary>
    public bool IsMail { get; private set; }

    public string Recipient => RecipientBox.Text;
    public string Subject => SubjectBox.Text;
    public string Body => BodyBox.Text;

    public PostComposePane(Rectangle pane)
    {
        X = pane.X;
        Y = pane.Y;
        Width = pane.Width;
        Height = pane.Height;

        RecipientLabel = AddFieldLabel("To", 0);

        RecipientBox = AddField(
            "Recipient",
            LABEL_W,
            0,
            pane.Width - LABEL_W,
            RECIPIENT_MAX_LENGTH);

        var subjectY = FIELD_H + FIELD_GAP;
        AddFieldLabel("Subject", subjectY);

        SubjectBox = AddField(
            "Subject",
            LABEL_W,
            subjectY,
            pane.Width - LABEL_W,
            SUBJECT_MAX_LENGTH);

        var bodyTop = subjectY + FIELD_H + FIELD_GAP;
        var bodyWidth = pane.Width - ScrollBarControl.DEFAULT_WIDTH;

        BodyBox = new UITextBox
        {
            Name = "Body",
            X = 0,
            Y = bodyTop,
            Width = bodyWidth,
            Height = pane.Height - bodyTop,
            IsMultiLine = true,
            IsSelectable = true,
            MaxLength = BODY_MAX_LENGTH,
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
            X = bodyWidth,
            Y = bodyTop,
            Height = pane.Height - bodyTop
        };

        TextBoxScrollSync.Wire(BodyBox, BodyScrollBar);
        AddChild(BodyScrollBar);
    }

    private UILabel AddFieldLabel(string text, int y)
    {
        var label = new UILabel
        {
            X = 0,
            Y = y,
            Width = LABEL_W,
            Height = FIELD_H,
            PaddingLeft = 0,
            PaddingTop = 0,
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            ForegroundColor = DialogPalette.Title,
            IsHitTestVisible = false
        };

        AddChild(label);

        return label;
    }

    private UITextBox AddField(
        string name,
        int x,
        int y,
        int width,
        int maxLength)
    {
        var box = new UITextBox
        {
            Name = name,
            X = x,
            Y = y,
            Width = width,
            Height = FIELD_H,
            MaxLength = maxLength,
            ForegroundColor = LegendColors.White,
            FocusedBackgroundColor = DialogPalette.RowHoverFill,
            IsTabStop = true
        };

        AddChild(box);

        return box;
    }

    /// <summary>
    ///     Keeps the scrollbar range and thumb in sync with the editable body as it changes.
    /// </summary>
    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (Visible)
            TextBoxScrollSync.Sync(BodyBox, BodyScrollBar);
    }

    /// <summary>
    ///     Resets the pane for a fresh compose. <paramref name="recipient" /> pre-fills and locks the To field for a
    ///     mail reply; board posts hide the row entirely.
    /// </summary>
    public void Reset(bool isMail, string? recipient)
    {
        IsMail = isMail;

        var isReply = isMail && !string.IsNullOrEmpty(recipient);

        RecipientLabel.Visible = isMail;
        RecipientBox.Visible = isMail;
        RecipientBox.Text = recipient ?? string.Empty;
        RecipientBox.IsReadOnly = isReply;
        RecipientBox.ForegroundColor = isReply ? TextColors.Default : LegendColors.White;
        RecipientBox.IsTabStop = isMail && !isReply;

        SubjectBox.Text = string.Empty;

        BodyBox.Text = string.Empty;
        BodyBox.ScrollOffset = 0;
        BodyBox.CursorPosition = 0;

        //this pane is a reused singleton — drop undo so Ctrl+Z can't resurrect a prior message's contents
        RecipientBox.ResetUndoHistory();
        SubjectBox.ResetUndoHistory();
        BodyBox.ResetUndoHistory();

        ClearFocus();

        //a reply already knows its recipient, so start on the subject.
        if (isMail && !isReply)
            RecipientBox.IsFocused = true;
        else
            SubjectBox.IsFocused = true;
    }

    /// <summary>Whether the post can be sent — mail needs a recipient, a board post needs nothing.</summary>
    public bool CanSend => !IsMail || !string.IsNullOrWhiteSpace(RecipientBox.Text);

    /// <summary>Drops focus from every field (the modal calls this when the view is left).</summary>
    public void ClearFocus()
    {
        RecipientBox.IsFocused = false;
        SubjectBox.IsFocused = false;
        BodyBox.IsFocused = false;
    }

    /// <summary>
    ///     Advances focus from the recipient/subject line to the next field on Enter, matching the legacy compose
    ///     panels. Returns true when the key was consumed. Tab traversal is left to the panel's own tab-stop walk.
    /// </summary>
    public bool HandleEnter()
    {
        if (RecipientBox is { IsFocused: true, Visible: true })
        {
            RecipientBox.IsFocused = false;
            SubjectBox.IsFocused = true;

            return true;
        }

        if (SubjectBox.IsFocused)
        {
            SubjectBox.IsFocused = false;
            BodyBox.IsFocused = true;

            return true;
        }

        //the body is multi-line: Enter inserts a newline there, so it is not ours to consume.
        return false;
    }
}
