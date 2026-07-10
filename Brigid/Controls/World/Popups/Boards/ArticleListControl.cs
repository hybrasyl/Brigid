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
///     Public board article list panel using _narlist prefab. Displays a scrollable list of board posts with author, date,
///     and subject. Buttons: View, New, Delete, Hilight, Up (back to boards), Close.
/// </summary>
public sealed class ArticleListControl : PrefabPanel
{
    private const int ROW_HEIGHT = Constants.BOARD_ROW_HEIGHT;
    private const int POSTID_CHARS = 5;
    private const int AUTHOR_CHARS = 14; //wide enough for system authors like "Mundane Gossip" (14) so the date stays aligned
    private const int DATE_CHARS = 5;
    //row-block alignment to the board art, captured from the in-client nudge tool (3px left, 4px up)
    private const int ROW_OFFSET_X = -3;
    private const int ROW_OFFSET_Y = -4;
    private const string SPACER5 = "     ";
    private const string SPACER3 = "   ";

    private static readonly Color SelectedColor = new(100, 149, 237);

    private readonly VirtualizedListView<MailEntry, UILabel> ListView;

    private List<MailEntry> Entries = [];
    private bool LoadingMore;
    private bool MoreMayExist;
    private int TargetX;

    public ushort BoardId { get; private set; }
    public UIButton? CloseButton { get; }
    public UIButton? DeleteButton { get; }
    public UIButton? HighlightButton { get; }
    public UIButton? NewButton { get; }
    public UIButton? UpButton { get; }
    public UIButton? ViewButton { get; }

    public ArticleListControl()
        : base("_narlist", false)
    {
        Name = "ArticleList";
        Visible = false;
        UsesControlStack = true;

        ViewButton = CreateButton("View");
        NewButton = CreateButton("New");
        DeleteButton = CreateButton("Delete");
        UpButton = CreateButton("Up");
        CloseButton = CreateButton("Close");

        if (CloseButton is not null)
            CloseButton.Clicked += () => OnClose?.Invoke();

        if (ViewButton is not null)
            ViewButton.Clicked += () =>
            {
                if (TrySelected(out var i))
                    OnViewPost?.Invoke(Entries[i].PostId);
            };

        if (NewButton is not null)
            NewButton.Clicked += () => OnNewPost?.Invoke();

        if (DeleteButton is not null)
            DeleteButton.Clicked += () =>
            {
                if (TrySelected(out var i))
                    OnDeletePost?.Invoke(Entries[i].PostId);
            };

        if (UpButton is not null)
            UpButton.Clicked += () => OnUp?.Invoke();

        HighlightButton = CreateButton("Hilight");

        if (HighlightButton is not null)
        {
            HighlightButton.Visible = false;

            HighlightButton.Clicked += () =>
            {
                if (TrySelected(out var i))
                    OnHighlight?.Invoke(Entries[i].PostId);
            };
        }

        var articleListRect = GetRect("ArticleList");

        //nudge the whole list block onto the board art. rows are clipped to the list panel's bounds, so the offset must
        //move the bounds (moving the panel + its clip region) — a negative row inset would only clip, not shift. the bar
        //rides along by a few px, which is immaterial against its own art.
        var listBounds = new Rectangle(
            articleListRect.X + ROW_OFFSET_X,
            articleListRect.Y + ROW_OFFSET_Y,
            articleListRect.Width,
            articleListRect.Height);

        ListView = new VirtualizedListView<MailEntry, UILabel>(
            listBounds,
            ROW_HEIGHT,
            (w, i) => new UILabel
            {
                Name = $"Article{i}",
                Width = w,
                Height = ROW_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0,
                //fixed-width columns: clip the subject at the panel edge instead of squishing the line to fit
                ShrinkToFit = false
            },
            BindRow)
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

        label.ForegroundColor = row.Selected
            ? SelectedColor
            : row.Item.IsHighlighted
                ? Color.Yellow
                : TextColors.Default;
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
        //truncate to the fixed column widths so a long author (e.g. "Mundane Gossip") can't push the later columns over
        var author = entry.Author.Length > AUTHOR_CHARS ? entry.Author[..AUTHOR_CHARS] : entry.Author;

        var date = $"{entry.Month,2}/{entry.Day,2}";

        //the subject is the final column and runs to the panel edge, where the label's clip trims it — so the cutoff
        //tracks the control rectangle exactly, independent of font size.
        return $"{entry.PostId,POSTID_CHARS}{SPACER5}{author,-AUTHOR_CHARS}{SPACER5}{date,DATE_CHARS}{SPACER3}{entry.Subject}";
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
    public event HighlightPostHandler? OnHighlight;

    /// <summary>
    ///     Fired when the user scrolls to the oldest loaded row and more posts may exist, mirroring retail's scroll-back
    ///     paging. The short is the last (oldest) PostId held, used as the startPostId for the next older page request.
    /// </summary>
    public event LoadMorePostsHandler? OnLoadMorePosts;

    public event NewPostHandler? OnNewPost;
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

    public void ToggleHighlight(short postId)
    {
        var index = Entries.FindIndex(e => e.PostId == postId);

        if (index < 0)
            return;

        var entry = Entries[index];
        Entries[index] = entry with { IsHighlighted = !entry.IsHighlighted };
        ListView.Refresh();
    }

    /// <summary>
    ///     Shows or hides the Highlight button based on GM status.
    /// </summary>
    public void SetHighlightEnabled(bool enabled)
    {
        if (HighlightButton is not null)
            HighlightButton.Visible = enabled;
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
    ///     Populates the article list from server data (first page).
    /// </summary>
    public void ShowArticles(ushort boardId, List<MailEntry> entries)
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

        if (HighlightButton is { Visible: true })
            HighlightButton.Enabled = hasSelection;
    }
}
