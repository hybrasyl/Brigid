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
///     <c>MailSendControl</c> (<c>_nmails</c>). A party row (the mail recipient, or the author on a board post),
///     a subject row, and a multi-line body with its own scrollbar. The legacy panels overlaid their bar on a
///     black strip baked into the art at a hardcoded (483, 65, 214); here the bar is laid out from the pane rect
///     like any other control, and the field boxes are painted by an <see cref="InputSlotPane" /> the fields live
///     in rather than by background art.
/// </summary>
internal sealed class PostComposePane : UIPanel
{
    private const int FIELD_H = 20;
    private const int FIELD_GAP = 6;
    private const int LABEL_W = 64;
    private const int RECIPIENT_MAX_LENGTH = 24;
    private const int SUBJECT_MAX_LENGTH = 60;
    private const int BODY_MAX_LENGTH = 10000;

    //hosts every text field, so the slot chrome is painted behind each of them.
    private readonly InputSlotPane Fields;

    private readonly UILabel PartyLabel;
    private readonly UITextBox PartyBox;
    private readonly UITextBox SubjectBox;
    private readonly UITextBox BodyBox;
    private readonly ScrollBarControl BodyScrollBar;

    /// <summary>True when composing mail (the party row is an editable recipient) rather than a board post.</summary>
    public bool IsMail { get; private set; }

    /// <summary>The mail recipient. Empty for a board post, whose party row is the read-only author display.</summary>
    public string Recipient => IsMail ? PartyBox.Text : string.Empty;
    public string Subject => SubjectBox.Text;
    public string Body => BodyBox.Text;

    public PostComposePane(Rectangle pane)
    {
        X = pane.X;
        Y = pane.Y;
        Width = pane.Width;
        Height = pane.Height;

        Fields = new InputSlotPane
        {
            X = 0,
            Y = 0,
            Width = pane.Width,
            Height = pane.Height
        };
        AddChild(Fields);

        PartyLabel = AddFieldLabel("To", 0);

        PartyBox = AddField(
            "Party",
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

        Fields.AddChild(BodyBox);

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

        Fields.AddChild(label);

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

        Fields.AddChild(box);

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
    ///     Resets the pane for a fresh compose. The first row is the mail recipient — editable for a new mail,
    ///     locked when <paramref name="party" /> pre-fills a reply — or, for a board post, the read-only author
    ///     display the legacy compose panel showed.
    /// </summary>
    public void Reset(bool isMail, string? party)
    {
        IsMail = isMail;

        //editable only for a new mail: a reply's recipient is fixed, and a board post's row is the author.
        var editable = isMail && string.IsNullOrEmpty(party);

        PartyLabel.Text = isMail ? "To" : "By";
        PartyBox.Text = party ?? string.Empty;
        PartyBox.IsReadOnly = !editable;
        PartyBox.ForegroundColor = editable ? LegendColors.White : TextColors.Default;
        PartyBox.IsTabStop = editable;

        SubjectBox.Text = string.Empty;

        BodyBox.Text = string.Empty;
        BodyBox.ScrollOffset = 0;
        BodyBox.CursorPosition = 0;

        //this pane is a reused singleton — drop undo so Ctrl+Z can't resurrect a prior message's contents
        PartyBox.ResetUndoHistory();
        SubjectBox.ResetUndoHistory();
        BodyBox.ResetUndoHistory();

        ClearFocus();

        if (editable)
            PartyBox.IsFocused = true;
        else
            SubjectBox.IsFocused = true;
    }

    /// <summary>Whether the post can be sent — mail needs a recipient, a board post needs nothing.</summary>
    public bool CanSend => !IsMail || !string.IsNullOrWhiteSpace(PartyBox.Text);

    /// <summary>Drops focus from every field (the modal calls this when the view is left).</summary>
    public void ClearFocus()
    {
        PartyBox.IsFocused = false;
        SubjectBox.IsFocused = false;
        BodyBox.IsFocused = false;
    }

    /// <summary>
    ///     Advances focus from the recipient/subject line to the next field on Enter, matching the legacy compose
    ///     panels. Returns true when the key was consumed. Tab traversal is left to the panel's own tab-stop walk.
    /// </summary>
    public bool HandleEnter()
    {
        if (PartyBox.IsFocused)
        {
            PartyBox.IsFocused = false;
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
