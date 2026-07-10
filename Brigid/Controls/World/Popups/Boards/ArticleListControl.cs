#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Models;
using Brigid.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
    //row-block alignment to the board art, captured from the in-client nudge tool (2px left, 4px up)
    private const int ROW_OFFSET_X = -2;
    private const int ROW_OFFSET_Y = -4;
    private const string SPACER5 = "     ";
    private const string SPACER3 = "   ";

    private readonly Rectangle ArticleListRect;
    private readonly int MaxVisibleRows;
    private readonly UILabel[] RowLabels;
    private readonly ScrollBarControl ScrollBar;
    private int DataVersion;

    private List<MailEntry> Entries = [];
    private bool LoadingMore;
    private bool MoreMayExist;
    private int RenderedVersion = -1;
    private int ScrollOffset;
    private int SelectedIndex = -1;
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
                if ((SelectedIndex >= 0) && (SelectedIndex < Entries.Count))
                    OnViewPost?.Invoke(Entries[SelectedIndex].PostId);
            };

        if (NewButton is not null)
            NewButton.Clicked += () => OnNewPost?.Invoke();

        if (DeleteButton is not null)
            DeleteButton.Clicked += () =>
            {
                if ((SelectedIndex >= 0) && (SelectedIndex < Entries.Count))
                    OnDeletePost?.Invoke(Entries[SelectedIndex].PostId);
            };

        if (UpButton is not null)
            UpButton.Clicked += () => OnUp?.Invoke();

        HighlightButton = CreateButton("Hilight");

        if (HighlightButton is not null)
        {
            HighlightButton.Visible = false;

            HighlightButton.Clicked += () =>
            {
                if ((SelectedIndex >= 0) && (SelectedIndex < Entries.Count))
                    OnHighlight?.Invoke(Entries[SelectedIndex].PostId);
            };
        }

        ArticleListRect = GetRect("ArticleList");
        MaxVisibleRows = ArticleListRect.Height > 0 ? ArticleListRect.Height / ROW_HEIGHT : 0;

        //scrollbar
        ScrollBar = new ScrollBarControl
        {
            Name = "ScrollBar",
            X = ArticleListRect.X + ArticleListRect.Width - ScrollBarControl.DEFAULT_WIDTH,
            Y = ArticleListRect.Y,
            Height = ArticleListRect.Height
        };

        ScrollBar.OnValueChanged += v =>
        {
            ScrollOffset = v;
            DataVersion++;
            MaybeRequestOlder();
        };

        AddChild(ScrollBar);

        //row labels — one per visible row, columns via fixed-width string formatting
        var usableWidth = ArticleListRect.Width - ScrollBarControl.DEFAULT_WIDTH;

        RowLabels = new UILabel[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            RowLabels[i] = new UILabel
            {
                Name = $"Article{i}",
                X = ArticleListRect.X + ROW_OFFSET_X,
                Y = ArticleListRect.Y + ROW_OFFSET_Y + i * ROW_HEIGHT,
                Width = usableWidth,
                Height = ROW_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0,
                //fixed-width columns: clip overflow at the panel edge instead of squishing the line to fit
                ShrinkToFit = false
            };

            AddChild(RowLabels[i]);
        }
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
        DataVersion++;

        UpdateScrollBar();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        RefreshLabels();
        base.Draw(spriteBatch);
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
    ///     Requests the next older page when the view has scrolled to the oldest loaded row, paging is not exhausted, and
    ///     no request is already in flight. Mirrors retail, which re-sends 0x02 continuously as the user scrolls back.
    /// </summary>
    private void MaybeRequestOlder()
    {
        if (!MoreMayExist || LoadingMore || (Entries.Count == 0))
            return;

        if (ScrollOffset + MaxVisibleRows < Entries.Count)
            return;

        LoadingMore = true;
        OnLoadMorePosts?.Invoke(Entries[^1].PostId);
    }

    private void RefreshLabels()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var entryIndex = ScrollOffset + i;

            if (entryIndex < Entries.Count)
            {
                var entry = Entries[entryIndex];
                var isSelected = entryIndex == SelectedIndex;

                var textColor = isSelected
                    ? new Color(100, 149, 237)
                    : entry.IsHighlighted
                        ? Color.Yellow
                        : TextColors.Default;

                RowLabels[i].ForegroundColor = textColor;
                RowLabels[i].Text = FormatRow(entry);
            } else
                RowLabels[i].Text = string.Empty;
        }
    }

    /// <summary>
    ///     Appends additional entries from a subsequent page to the existing list.
    /// </summary>
    public void RemoveEntry(short postId)
    {
        var index = Entries.FindIndex(e => e.PostId == postId);

        if (index < 0)
            return;

        Entries.RemoveAt(index);

        if (SelectedIndex >= Entries.Count)
            SelectedIndex = Entries.Count - 1;

        DataVersion++;
        UpdateScrollBar();
    }

    public void ToggleHighlight(short postId)
    {
        var index = Entries.FindIndex(e => e.PostId == postId);

        if (index < 0)
            return;

        var entry = Entries[index];
        Entries[index] = entry with { IsHighlighted = !entry.IsHighlighted };
        DataVersion++;
    }

    /// <summary>
    ///     Shows or hides the Highlight button based on GM status.
    /// </summary>
    public void SetHighlightEnabled(bool enabled)
    {
        HighlightButton?.Visible = enabled;
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
        SelectedIndex = -1;
        ScrollOffset = 0;
        DataVersion++;

        UpdateScrollBar();
        UpdateButtonStates();
        Show();
    }

    public override void OnClick(ClickEvent e)
    {
        base.OnClick(e);

        if (e.Button != MouseButton.Left)
            return;

        var localX = e.ScreenX - ScreenX - ArticleListRect.X;
        var localY = e.ScreenY - ScreenY - ArticleListRect.Y;

        //exclude the scrollbar strip on the right so a click on it never selects/opens the row beneath
        if ((localX < 0) || (localX >= ArticleListRect.Width - ScrollBarControl.DEFAULT_WIDTH) || (localY < 0) || (localY >= ArticleListRect.Height))
            return;

        var row = (localY - ROW_OFFSET_Y) / ROW_HEIGHT;

        if ((row < 0) || (row >= MaxVisibleRows))
            return;

        var entryIndex = ScrollOffset + row;

        if (entryIndex >= Entries.Count)
            return;

        SelectedIndex = entryIndex;
        DataVersion++;
        UpdateButtonStates();
    }

    public override void OnDoubleClick(DoubleClickEvent e)
    {
        base.OnDoubleClick(e);

        if (e.Button != MouseButton.Left)
            return;

        var localX = e.ScreenX - ScreenX - ArticleListRect.X;
        var localY = e.ScreenY - ScreenY - ArticleListRect.Y;

        //exclude the scrollbar strip on the right so a click on it never selects/opens the row beneath
        if ((localX < 0) || (localX >= ArticleListRect.Width - ScrollBarControl.DEFAULT_WIDTH) || (localY < 0) || (localY >= ArticleListRect.Height))
            return;

        var row = (localY - ROW_OFFSET_Y) / ROW_HEIGHT;

        if ((row < 0) || (row >= MaxVisibleRows))
            return;

        var entryIndex = ScrollOffset + row;

        if (entryIndex >= Entries.Count)
            return;

        SelectedIndex = entryIndex;
        DataVersion++;
        UpdateButtonStates();
        OnViewPost?.Invoke(Entries[entryIndex].PostId);
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
                MoveSelection(-MaxVisibleRows);
                e.Handled = true;

                break;
            case Keys.PageDown:
                MoveSelection(MaxVisibleRows);
                e.Handled = true;

                break;
            case Keys.Enter:
                if ((SelectedIndex >= 0) && (SelectedIndex < Entries.Count))
                    OnViewPost?.Invoke(Entries[SelectedIndex].PostId);

                e.Handled = true;

                break;
            case Keys.Delete:
                if ((SelectedIndex >= 0) && (SelectedIndex < Entries.Count))
                    OnDeletePost?.Invoke(Entries[SelectedIndex].PostId);

                e.Handled = true;

                break;
        }
    }

    //keyboard row navigation: move the selection, keeping it on screen and paging older posts if it reaches the bottom.
    private void MoveSelection(int delta)
    {
        if (Entries.Count == 0)
            return;

        var newIndex = SelectedIndex < 0
            ? delta > 0 ? 0 : Entries.Count - 1
            : Math.Clamp(SelectedIndex + delta, 0, Entries.Count - 1);

        SelectedIndex = newIndex;
        EnsureVisible(newIndex);
        DataVersion++;
        UpdateButtonStates();
    }

    private void EnsureVisible(int index)
    {
        if (index < ScrollOffset)
            SetScrollOffset(index);
        else if (index >= ScrollOffset + MaxVisibleRows)
            SetScrollOffset(index - MaxVisibleRows + 1);
    }

    private void SetScrollOffset(int offset)
    {
        var clamped = Math.Clamp(offset, 0, ScrollBar.MaxValue);

        if (clamped == ScrollOffset)
            return;

        ScrollOffset = clamped;
        ScrollBar.Value = clamped;
        MaybeRequestOlder();
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        if (ScrollBar.TotalItems <= ScrollBar.VisibleItems)
            return;

        var newValue = Math.Clamp(ScrollBar.Value - e.Delta, 0, ScrollBar.MaxValue);

        if (newValue != ScrollBar.Value)
        {
            ScrollBar.Value = newValue;
            ScrollOffset = newValue;
            DataVersion++;
            MaybeRequestOlder();
        }

        e.Handled = true;
    }

    private void UpdateButtonStates()
    {
        var hasSelection = (SelectedIndex >= 0) && (SelectedIndex < Entries.Count);

        ViewButton?.Enabled = hasSelection;

        DeleteButton?.Enabled = hasSelection;

        if (HighlightButton is { Visible: true })
            HighlightButton.Enabled = hasSelection;
    }

    private void UpdateScrollBar()
    {
        ScrollBar.TotalItems = Entries.Count;
        ScrollBar.VisibleItems = MaxVisibleRows;
        ScrollBar.MaxValue = Math.Max(0, Entries.Count - MaxVisibleRows);
    }
}