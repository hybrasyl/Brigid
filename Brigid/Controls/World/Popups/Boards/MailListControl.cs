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
    private const int PREFIX_CHARS = POSTID_CHARS + AUTHOR_CHARS + DATE_CHARS;

    private readonly Rectangle MailListRect;
    private readonly int MaxSubjectChars;
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
    public string CurrentAuthor
        => (SelectedIndex >= 0) && (SelectedIndex < Entries.Count) ? Entries[SelectedIndex].Author : string.Empty;
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
                if ((SelectedIndex >= 0) && (SelectedIndex < Entries.Count))
                    OnViewPost?.Invoke(Entries[SelectedIndex].PostId);
            };

        if (NewButton is not null)
            NewButton.Clicked += () => OnNewMail?.Invoke();

        if (ReplyButton is not null)
            ReplyButton.Clicked += () =>
            {
                if ((SelectedIndex >= 0) && (SelectedIndex < Entries.Count))
                    OnReplyPost?.Invoke(Entries[SelectedIndex].PostId);
            };

        if (DeleteButton is not null)
            DeleteButton.Clicked += () =>
            {
                if ((SelectedIndex >= 0) && (SelectedIndex < Entries.Count))
                    OnDeletePost?.Invoke(Entries[SelectedIndex].PostId);
            };

        if (UpButton is not null)
            UpButton.Clicked += () => OnUp?.Invoke();

        MailListRect = GetRect("MailList");
        MaxVisibleRows = MailListRect.Height > 0 ? MailListRect.Height / ROW_HEIGHT : 0;

        //scrollbar
        ScrollBar = new ScrollBarControl
        {
            Name = "ScrollBar",
            X = MailListRect.X + MailListRect.Width - ScrollBarControl.DEFAULT_WIDTH,
            Y = MailListRect.Y,
            Height = MailListRect.Height
        };

        ScrollBar.OnValueChanged += v =>
        {
            ScrollOffset = v;
            DataVersion++;
            MaybeRequestOlder();
        };
        AddChild(ScrollBar);

        //row labels — one per visible row, columns via fixed-width string formatting
        var usableWidth = MailListRect.Width - ScrollBarControl.DEFAULT_WIDTH;
        MaxSubjectChars = Math.Max(0, (usableWidth - TEXT_INDENT) / TextRenderer.CHAR_WIDTH - PREFIX_CHARS);

        RowLabels = new UILabel[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            RowLabels[i] = new UILabel
            {
                X = MailListRect.X + TEXT_INDENT,
                Y = MailListRect.Y + i * ROW_HEIGHT,
                Width = usableWidth - TEXT_INDENT,
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
        //truncate to the fixed column widths so a long author can't push the later columns over
        var author = entry.Author.Length > AUTHOR_CHARS ? entry.Author[..AUTHOR_CHARS] : entry.Author;
        var subject = entry.Subject.Length > MaxSubjectChars ? entry.Subject[..MaxSubjectChars] : entry.Subject;

        return $"{entry.PostId,-POSTID_CHARS}{author,-AUTHOR_CHARS}{entry.Month + "/" + entry.Day,-DATE_CHARS}{subject}";
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

                var textColor = isSelected ? new Color(100, 149, 237) : TextColors.Default;

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

        var localX = e.ScreenX - ScreenX - MailListRect.X;
        var localY = e.ScreenY - ScreenY - MailListRect.Y;

        //exclude the scrollbar strip on the right so a click on it never selects/opens the row beneath
        if ((localX < 0) || (localX >= MailListRect.Width - ScrollBarControl.DEFAULT_WIDTH) || (localY < 0) || (localY >= MailListRect.Height))
            return;

        var row = localY / ROW_HEIGHT;

        if (row >= MaxVisibleRows)
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

        var localX = e.ScreenX - ScreenX - MailListRect.X;
        var localY = e.ScreenY - ScreenY - MailListRect.Y;

        //exclude the scrollbar strip on the right so a click on it never selects/opens the row beneath
        if ((localX < 0) || (localX >= MailListRect.Width - ScrollBarControl.DEFAULT_WIDTH) || (localY < 0) || (localY >= MailListRect.Height))
            return;

        var row = localY / ROW_HEIGHT;

        if (row >= MaxVisibleRows)
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

        ReplyButton?.Enabled = hasSelection;
    }

    private void UpdateScrollBar()
    {
        ScrollBar.TotalItems = Entries.Count;
        ScrollBar.VisibleItems = MaxVisibleRows;
        ScrollBar.MaxValue = Math.Max(0, Entries.Count - MaxVisibleRows);
    }
}