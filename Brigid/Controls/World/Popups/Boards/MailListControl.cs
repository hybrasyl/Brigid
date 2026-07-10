#region
using Brigid.Controls.Components;
using Brigid.Controls.Scrolling;
using Brigid.Models;
using Brigid.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Boards;

/// <summary>
///     Mail list panel using _nmaill prefab. Displays a scrollable list of mail posts with author, date, and subject.
///     Buttons: View, New, Reply, Delete, Up (back to boards), Quit (close).
/// </summary>
public sealed class MailListControl : PrefabPanel
{
    private const int ROW_HEIGHT = Constants.BOARD_ROW_HEIGHT;
    private const int TEXT_INDENT = 24;
    private const int POSTID_CHARS = 6;
    private const int AUTHOR_CHARS = 17;
    private const int DATE_CHARS = 7;

    private static readonly Color SelectedColor = new(100, 149, 237);

    private readonly VirtualizedListView<MailEntry, UILabel> ListView;

    private List<MailEntry> Entries = [];
    private bool LoadingMore;
    private bool MoreMayExist;
    private int TargetX;

    public ushort BoardId { get; private set; }
    public string CurrentAuthor
        => TrySelected(out var i) ? Entries[i].Author : string.Empty;
    public UIButton? DeleteButton { get; }
    public UIButton? NewButton { get; }

    public UIButton? QuitButton { get; }
    public UIButton? ReplyButton { get; }
    public UIButton? UpButton { get; }

    public UIButton? ViewButton { get; }

    public MailListControl()
        : base("_nmaill", false)
    {
        Name = "MailList";
        Visible = false;
        UsesControlStack = true;

        ViewButton = CreateButton("View");
        NewButton = CreateButton("New");
        ReplyButton = CreateButton("Reply");
        DeleteButton = CreateButton("Delete");
        UpButton = CreateButton("Up");
        QuitButton = CreateButton("Quit");

        if (QuitButton is not null)
            QuitButton.Clicked += () => OnClose?.Invoke();

        if (ViewButton is not null)
            ViewButton.Clicked += () =>
            {
                if (TrySelected(out var i))
                    OnViewPost?.Invoke(Entries[i].PostId);
            };

        if (NewButton is not null)
            NewButton.Clicked += () => OnNewMail?.Invoke();

        if (ReplyButton is not null)
            ReplyButton.Clicked += () =>
            {
                if (TrySelected(out var i))
                    OnReplyPost?.Invoke(Entries[i].PostId);
            };

        if (DeleteButton is not null)
            DeleteButton.Clicked += () =>
            {
                if (TrySelected(out var i))
                    OnDeletePost?.Invoke(Entries[i].PostId);
            };

        if (UpButton is not null)
            UpButton.Clicked += () => OnUp?.Invoke();

        var mailListRect = GetRect("MailList");

        //rows are single labels, columns via fixed-width string formatting; text indented past the mail icon column
        ListView = new VirtualizedListView<MailEntry, UILabel>(
            mailListRect,
            ROW_HEIGHT,
            (w, i) => new UILabel
            {
                Name = $"Mail{i}",
                Width = w,
                Height = ROW_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0,
                //fixed-width columns: clip the subject at the panel edge instead of squishing the line to fit
                ShrinkToFit = false
            },
            BindRow,
            rowInsetX: TEXT_INDENT)
        {
            Selectable = true
        };

        ListView.SelectionChanged += _ => UpdateButtonStates();

        ListView.ItemActivated += i =>
        {
            if ((i >= 0) && (i < Entries.Count))
                OnViewPost?.Invoke(Entries[i].PostId);
        };

        //retail-style scroll-back paging: reaching the oldest loaded row requests the next older page. this relies on a
        //full page overflowing the viewport so the bottom is scroll-reachable — the board panels show well under
        //BoardProtocol.PageSize (16) rows, so a full page always scrolls.
        ListView.ReachedEnd += MaybeRequestOlder;

        AddChild(ListView);
    }

    private bool TrySelected(out int index)
    {
        index = ListView.SelectedIndex;

        return (index >= 0) && (index < Entries.Count);
    }

    private void BindRow(UILabel label, VirtualRow<MailEntry> row)
    {
        if (row.Kind != VirtualRowKind.Item)
        {
            label.Text = string.Empty;

            return;
        }

        label.ForegroundColor = row.Selected ? SelectedColor : TextColors.Default;
        label.Text = FormatRow(row.Item);
    }

    /// <summary>
    ///     Whether a scroll-paging request is currently in flight (fired but not yet answered). The server-handler uses
    ///     this to route a post-list reply to <see cref="AppendEntries" /> vs a fresh replace.
    /// </summary>
    public bool IsPaging => LoadingMore;

    /// <summary>
    ///     Clears the in-flight paging flag without appending — used when a paging reply arrives after the user has
    ///     already left this list, so the next time it is shown it can page again.
    /// </summary>
    public void CancelPaging() => LoadingMore = false;

    public void AppendEntries(List<MailEntry> entries)
    {
        LoadingMore = false;

        //dedupe against posts we already hold: a server that ignores the paging cursor (Hybrasyl drops navOffset and
        //re-sends the same set) or a final overlapping batch must not append duplicate rows or re-arm paging forever.
        var existingIds = new HashSet<short>(Entries.Select(e => e.PostId));
        var added = 0;

        foreach (var entry in entries)
            if (existingIds.Add(entry.PostId))
            {
                Entries.Add(entry);
                added++;
            }

        //keep paging while a batch brings at least one genuinely new post. retail's paged response is inclusive of the
        //cursor post, so a full page always overlaps by one — gating on "a full page of new" would stop after a single
        //fetch. a batch that adds nothing new means we reached the oldest post (or the server does not page).
        MoreMayExist = added > 0;

        ListView.Refresh();
    }

    private string FormatRow(MailEntry entry)
    {
        //truncate to the fixed column widths so a long author can't push the later columns over
        var author = entry.Author.Length > AUTHOR_CHARS ? entry.Author[..AUTHOR_CHARS] : entry.Author;

        //the subject is the final column and runs to the panel edge, where the label's clip trims it — so the cutoff
        //tracks the control rectangle exactly, independent of font size.
        return $"{entry.PostId,-POSTID_CHARS}{author,-AUTHOR_CHARS}{entry.Month + "/" + entry.Day,-DATE_CHARS}{entry.Subject}";
    }

    public override void Hide()
    {
        //never let an in-flight paging flag outlive the visible list — otherwise a dropped/error reply would wedge it
        //and divert the next fresh open into the paging branch.
        LoadingMore = false;
        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
    }

    public event CloseHandler? OnClose;
    public event DeletePostHandler? OnDeletePost;

    /// <summary>
    ///     Fired when the user scrolls to the oldest loaded row and more posts may exist, mirroring retail's scroll-back
    ///     paging. The short is the last (oldest) PostId held, used as the startPostId for the next older page request.
    /// </summary>
    public event LoadMorePostsHandler? OnLoadMorePosts;

    public event NewMailHandler? OnNewMail;
    public event ReplyPostHandler? OnReplyPost;
    public event UpHandler? OnUp;
    public event ViewPostHandler? OnViewPost;

    /// <summary>
    ///     Requests the next older page when the view has scrolled to the oldest loaded row (raised as
    ///     <see cref="VirtualizedListView{TItem,TRow}.ReachedEnd" />), paging is not exhausted, and no request is already
    ///     in flight. Mirrors retail, which re-sends 0x02 continuously as the user scrolls back.
    /// </summary>
    private void MaybeRequestOlder()
    {
        if (!MoreMayExist || LoadingMore || (Entries.Count == 0))
            return;

        LoadingMore = true;
        OnLoadMorePosts?.Invoke(Entries[^1].PostId);
    }

    /// <summary>
    ///     Removes an entry by post id and re-clamps the selection.
    /// </summary>
    public void RemoveEntry(short postId)
    {
        var index = Entries.FindIndex(e => e.PostId == postId);

        if (index < 0)
            return;

        Entries.RemoveAt(index);

        if (ListView.SelectedIndex >= Entries.Count)
            ListView.SetSelectedIndex(Entries.Count - 1);

        ListView.Refresh();
        UpdateButtonStates();
    }

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
    ///     Populates the mail list from server data (first page).
    /// </summary>
    public void ShowMailList(ushort boardId, List<MailEntry> entries)
    {
        BoardId = boardId;
        Entries = entries;
        MoreMayExist = entries.Count >= BoardProtocol.PageSize;
        LoadingMore = false;

        ListView.SetItems(Entries);
        ListView.SetSelectedIndex(-1);
        UpdateButtonStates();
        Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Keys.Escape:
                OnUp?.Invoke();
                e.Handled = true;

                break;
            case Keys.Up:
                MoveSelection(-1);
                e.Handled = true;

                break;
            case Keys.Down:
                MoveSelection(1);
                e.Handled = true;

                break;
            case Keys.PageUp:
                MoveSelection(-ListView.VisibleRows);
                e.Handled = true;

                break;
            case Keys.PageDown:
                MoveSelection(ListView.VisibleRows);
                e.Handled = true;

                break;
            case Keys.Enter:
                if (TrySelected(out var viewIndex))
                    OnViewPost?.Invoke(Entries[viewIndex].PostId);

                e.Handled = true;

                break;
            case Keys.Delete:
                if (TrySelected(out var deleteIndex))
                    OnDeletePost?.Invoke(Entries[deleteIndex].PostId);

                e.Handled = true;

                break;
        }
    }

    //keyboard row navigation: move the selection, keeping it on screen; reaching the bottom pages older posts via ReachedEnd
    private void MoveSelection(int delta)
    {
        if (Entries.Count == 0)
            return;

        var current = ListView.SelectedIndex;

        var newIndex = current < 0
            ? delta > 0 ? 0 : Entries.Count - 1
            : Math.Clamp(current + delta, 0, Entries.Count - 1);

        ListView.SetSelectedIndex(newIndex);
        ListView.EnsureVisible(newIndex);
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        var hasSelection = TrySelected(out _);

        if (ViewButton is not null)
            ViewButton.Enabled = hasSelection;

        if (DeleteButton is not null)
            DeleteButton.Enabled = hasSelection;

        if (ReplyButton is not null)
            ReplyButton.Enabled = hasSelection;
    }
}
