#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Popups.Boards;

/// <summary>
///     The read view for one post — the single pane replacing both <c>ArticleReadControl</c> (<c>_narti</c>) and
///     <c>MailReadControl</c> (<c>_nmailr</c>), which differed only by a Reply button the modal now owns. A header
///     line (author · date · subject) over a scrollable, selectable body.
/// </summary>
internal sealed class PostReadPane : UIPanel
{
    private const int HEADER_H = 16;
    private const int HEADER_GAP = 4;
    private const int AUTHOR_W = 140;
    private const int DATE_W = 60;
    private const int COLUMN_GAP = 6;

    private readonly UILabel AuthorLabel;
    private readonly UILabel DateLabel;
    private readonly UILabel SubjectLabel;
    private readonly SelectableTextView Body;

    /// <summary>The post being read — the cursor for the modal's Prev/Next/Delete/Reply actions.</summary>
    public short CurrentPostId { get; private set; }

    /// <summary>The post's author, for pre-filling a mail reply.</summary>
    public string CurrentAuthor { get; private set; } = string.Empty;

    /// <summary>Whether a previous (newer) post exists — gates the modal's Prev action.</summary>
    public bool EnablePrev { get; private set; }

    public PostReadPane(Rectangle pane)
    {
        X = pane.X;
        Y = pane.Y;
        Width = pane.Width;
        Height = pane.Height;

        var dateX = AUTHOR_W + COLUMN_GAP;
        var subjectX = dateX + DATE_W + COLUMN_GAP;

        AuthorLabel = AddHeader(0, AUTHOR_W, DialogPalette.Title);
        DateLabel = AddHeader(dateX, DATE_W, DialogPalette.Title);
        SubjectLabel = AddHeader(subjectX, Math.Max(0, pane.Width - subjectX), LegendColors.White);

        var bodyTop = HEADER_H + HEADER_GAP;

        Body = new SelectableTextView(new Rectangle(0, bodyTop, pane.Width, pane.Height - bodyTop));
        AddChild(Body);
    }

    private UILabel AddHeader(int x, int width, Color color)
    {
        var label = new UILabel
        {
            X = x,
            Y = 0,
            Width = width,
            Height = HEADER_H,
            PaddingLeft = 0,
            PaddingRight = 0,
            PaddingTop = 0,
            VerticalAlignment = VerticalAlignment.Center,
            ShrinkToFit = false,
            ForegroundColor = color,
            IsHitTestVisible = false
        };

        AddChild(label);

        return label;
    }

    /// <summary>Populates the pane with a post and scrolls the body back to the top.</summary>
    public void SetPost(
        short postId,
        string author,
        int month,
        int day,
        string subject,
        string message,
        bool enablePrev)
    {
        CurrentPostId = postId;
        CurrentAuthor = author;
        EnablePrev = enablePrev;

        AuthorLabel.Text = author;
        DateLabel.Text = $"{month}/{day}";
        SubjectLabel.Text = subject;

        Body.SetText(message);
    }

    /// <summary>
    ///     Routes a key to the selectable body so Ctrl+C / select-all / caret navigation work. The modal is the top
    ///     control, so keyboard events stop there instead of descending to the label.
    /// </summary>
    public void ForwardKey(KeyDownEvent e) => Body.ForwardKey(e);
}
