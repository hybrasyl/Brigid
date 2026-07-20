#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Models;
using Brigid.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Popups.Boards;

/// <summary>
///     The post list for one board or the player's mailbox — the single pane replacing both
///     <c>ArticleListControl</c> (<c>_narlist</c>) and <c>MailListControl</c> (<c>_nmaill</c>), which were
///     line-for-line the same panel over different art. Mode is per-bind (<see cref="IsMail" />), not per-instance.
///     <para>
///         Rows are <see cref="PostRowView" />s with real per-column labels, so the columns are laid out in pixels
///         rather than padded to a character count against a specific bitmap — this is what retires the legacy
///         panels' <c>POSTID_CHARS</c>/<c>AUTHOR_CHARS</c>/<c>DATE_CHARS</c>, <c>TEXT_INDENT</c>, and the
///         <c>ROW_OFFSET_X/Y</c> art nudge. The subject runs to the pane edge and is clipped there.
///     </para>
///     <para>
///         Retail-style scroll-back paging is preserved verbatim: reaching the oldest loaded row raises
///         <see cref="LoadMoreRequested" /> with the oldest post id, and <see cref="AppendEntries" /> keeps paging
///         alive only while a batch brings at least one genuinely new post.
///     </para>
/// </summary>
internal sealed class PostListPane : UIPanel
{
    private const int ROW_HEIGHT = Constants.BOARD_ROW_HEIGHT;
    private const int HEADER_H = 16;
    private const int ROW_INSET_X = 4;

    private readonly UILabel HeaderLabel;
    private readonly VirtualizedListView<MailEntry, PostRowView> ListView;

    private List<MailEntry> Entries = [];
    private bool LoadingMore;
    private bool MoreMayExist;

    /// <summary>The board this list is showing. Mail is just the board whose id the server hands back.</summary>
    public ushort BoardId { get; private set; }

    /// <summary>True when this is the player's mailbox rather than a public bulletin board.</summary>
    public bool IsMail { get; private set; }

    /// <summary>Whether a scroll-paging request is in flight, so the host can route a reply to append vs replace.</summary>
    public bool IsPaging => LoadingMore;

    /// <summary>Whether a row is selected — gates the modal's View/Reply/Delete/Hilight actions.</summary>
    public bool HasSelection => TrySelected(out _);

    /// <summary>The selected post's author, for pre-filling a mail reply.</summary>
    public string SelectedAuthor => TrySelected(out var i) ? Entries[i].Author : string.Empty;

    /// <summary>Rows visible at once — the paging step for PageUp/PageDown.</summary>
    public int VisibleRows => ListView.VisibleRows;

    public event ViewPostHandler? PostActivated;
    public event Action? SelectionChanged;

    /// <summary>
    ///     Raised when the view scrolls to the oldest loaded row and more posts may exist. The parameter is the
    ///     oldest post id held, used as the next page's cursor.
    /// </summary>
    public event LoadMorePostsHandler? LoadMoreRequested;

    public PostListPane(Rectangle pane)
    {
        X = pane.X;
        Y = pane.Y;
        Width = pane.Width;
        Height = pane.Height;

        HeaderLabel = new UILabel
        {
            X = ROW_INSET_X,
            Y = 0,
            Width = pane.Width - ROW_INSET_X,
            Height = HEADER_H,
            PaddingLeft = 0,
            PaddingTop = 0,
            VerticalAlignment = VerticalAlignment.Center,
            ForegroundColor = DialogPalette.Title,
            IsHitTestVisible = false
        };
        AddChild(HeaderLabel);

        ListView = new VirtualizedListView<MailEntry, PostRowView>(
            new Rectangle(0, HEADER_H, pane.Width, pane.Height - HEADER_H),
            ROW_HEIGHT,
            (w, i) => new PostRowView(w) { Name = $"Post{i}" },
            BindRow,
            ROW_INSET_X)
        {
            Selectable = true
        };

        ListView.SelectionChanged += _ => SelectionChanged?.Invoke();

        ListView.ItemActivated += i =>
        {
            if ((i >= 0) && (i < Entries.Count))
                PostActivated?.Invoke(Entries[i].PostId);
        };

        ListView.ReachedEnd += MaybeRequestOlder;

        AddChild(ListView);
    }

    private bool TrySelected(out int index)
    {
        index = ListView.SelectedIndex;

        return (index >= 0) && (index < Entries.Count);
    }

    private static void BindRow(PostRowView row, VirtualRow<MailEntry> slot)
    {
        if (slot.Kind != VirtualRowKind.Item)
        {
            row.BindEmpty();

            return;
        }

        row.Bind(slot.Item, slot.Selected);
    }

    /// <summary>Replaces the list with a board's first page.</summary>
    public void SetPosts(ushort boardId, List<MailEntry> entries, bool isMail, string header)
    {
        BoardId = boardId;
        IsMail = isMail;
        Entries = entries;
        HeaderLabel.Text = header;

        //a full first page means the server may have older posts behind it.
        MoreMayExist = entries.Count >= BoardProtocol.PageSize;
        LoadingMore = false;

        ListView.SetItems(Entries);
        ListView.SetSelectedIndex(-1);
        SelectionChanged?.Invoke();
    }

    /// <summary>Appends a scroll-back page, de-duplicated against the posts already held.</summary>
    public void AppendEntries(List<MailEntry> entries)
    {
        LoadingMore = false;

        //dedupe against posts we already hold: a server that ignores the paging cursor (Hybrasyl drops navOffset
        //and re-sends the same set) or a final overlapping batch must not append duplicate rows or re-arm paging
        //forever.
        var existingIds = new HashSet<short>(Entries.Select(e => e.PostId));
        var added = 0;

        foreach (var entry in entries)
            if (existingIds.Add(entry.PostId))
            {
                Entries.Add(entry);
                added++;
            }

        //keep paging while a batch brings at least one genuinely new post. retail's paged response is inclusive of
        //the cursor post, so a full page always overlaps by one — gating on "a full page of new" would stop after a
        //single fetch. a batch that adds nothing new means we reached the oldest post (or the server does not page).
        MoreMayExist = added > 0;

        ListView.Refresh();
    }

    /// <summary>Clears the in-flight paging flag without appending (a reply that arrived after the user left).</summary>
    public void CancelPaging() => LoadingMore = false;

    private void MaybeRequestOlder()
    {
        if (!MoreMayExist || LoadingMore || (Entries.Count == 0))
            return;

        LoadingMore = true;
        LoadMoreRequested?.Invoke(Entries[^1].PostId);
    }

    /// <summary>Activates the selected post (the modal's View action).</summary>
    public void ActivateSelected()
    {
        if (TrySelected(out var index))
            PostActivated?.Invoke(Entries[index].PostId);
    }

    /// <summary>The selected post's id, or null when nothing is selected.</summary>
    public short? SelectedPostId => TrySelected(out var index) ? Entries[index].PostId : null;

    /// <summary>Removes a post by id and re-clamps the selection.</summary>
    public void RemoveEntry(short postId)
    {
        var index = Entries.FindIndex(e => e.PostId == postId);

        if (index < 0)
            return;

        Entries.RemoveAt(index);

        if (ListView.SelectedIndex >= Entries.Count)
            ListView.SetSelectedIndex(Entries.Count - 1);

        ListView.Refresh();
        SelectionChanged?.Invoke();
    }

    /// <summary>Flips a post's highlighted flag (the GM Hilight action, after the server confirms).</summary>
    public void ToggleHighlight(short postId)
    {
        var index = Entries.FindIndex(e => e.PostId == postId);

        if (index < 0)
            return;

        var entry = Entries[index];
        Entries[index] = entry with { IsHighlighted = !entry.IsHighlighted };
        ListView.Refresh();
    }

    /// <summary>Moves the selection by <paramref name="delta" /> rows, keeping it on screen.</summary>
    public void MoveSelection(int delta)
    {
        if (Entries.Count == 0)
            return;

        var current = ListView.SelectedIndex;

        var next = current < 0
            ? delta > 0 ? 0 : Entries.Count - 1
            : Math.Clamp(current + delta, 0, Entries.Count - 1);

        ListView.SetSelectedIndex(next);

        //driving the selection to the last row scrolls to the bottom, which re-arms paging through ReachedEnd —
        //so keyboard navigation and the wheel share one trigger.
        ListView.EnsureVisible(next);
        SelectionChanged?.Invoke();
    }

    /// <summary>
    ///     One post row: id, author, date and subject as separate labels at fixed pixel columns. Highlighted posts
    ///     render yellow, the selected row in the palette's selection colour.
    /// </summary>
    private sealed class PostRowView : UIPanel
    {
        private const int ID_W = 34;
        private const int AUTHOR_W = 104;
        private const int DATE_W = 44;
        private const int COLUMN_GAP = 6;

        private readonly UILabel IdLabel;
        private readonly UILabel AuthorLabel;
        private readonly UILabel DateLabel;
        private readonly UILabel SubjectLabel;

        public PostRowView(int width)
        {
            Width = width;
            Height = ROW_HEIGHT;

            var authorX = ID_W + COLUMN_GAP;
            var dateX = authorX + AUTHOR_W + COLUMN_GAP;
            var subjectX = dateX + DATE_W + COLUMN_GAP;

            IdLabel = AddColumn(0, ID_W, HorizontalAlignment.Right);
            AuthorLabel = AddColumn(authorX, AUTHOR_W, HorizontalAlignment.Left);
            DateLabel = AddColumn(dateX, DATE_W, HorizontalAlignment.Right);

            //the subject is the final column and runs to the pane edge, where the label's clip trims it — so the
            //cutoff tracks the control rectangle exactly, independent of font size.
            SubjectLabel = AddColumn(subjectX, Math.Max(0, width - subjectX), HorizontalAlignment.Left);
        }

        private UILabel AddColumn(int x, int width, HorizontalAlignment alignment)
        {
            var label = new UILabel
            {
                X = x,
                Y = 0,
                Width = width,
                Height = ROW_HEIGHT,
                PaddingLeft = 0,
                PaddingRight = 0,
                PaddingTop = 0,
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Center,
                //fixed pixel columns: clip at the column edge instead of squishing the text to fit
                ShrinkToFit = false,
                IsHitTestVisible = false
            };

            AddChild(label);

            return label;
        }

        public void BindEmpty()
        {
            IdLabel.Text = string.Empty;
            AuthorLabel.Text = string.Empty;
            DateLabel.Text = string.Empty;
            SubjectLabel.Text = string.Empty;
            BackgroundColor = null;
        }

        public void Bind(MailEntry entry, bool selected)
        {
            IdLabel.Text = entry.PostId.ToString();
            AuthorLabel.Text = entry.Author;
            DateLabel.Text = $"{entry.Month}/{entry.Day}";
            SubjectLabel.Text = entry.Subject;

            var color = selected
                ? DialogPalette.SelectedText
                : entry.IsHighlighted
                    ? Color.Yellow
                    : TextColors.Default;

            IdLabel.ForegroundColor = color;
            AuthorLabel.ForegroundColor = color;
            DateLabel.ForegroundColor = color;
            SubjectLabel.ForegroundColor = color;

            BackgroundColor = selected ? DialogPalette.RowSelectedFill : null;
        }
    }
}
