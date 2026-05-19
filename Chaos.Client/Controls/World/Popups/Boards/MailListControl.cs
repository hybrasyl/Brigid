#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups.Boards;

/// <summary>
///     Mail list panel using _nmaill prefab. Displays a scrollable list of mail posts with author, date, and subject.
///     Buttons: View, New, Reply, Delete, Up (back to boards), Quit (close).
/// </summary>
public sealed class MailListControl : PrefabPanel
{
    //server caps board responses at sbyte.maxvalue posts per page
    private const int MAX_POSTS_PER_PAGE = 127;
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
    private bool HasMorePosts;
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
                PaddingTop = 0
            };

            AddChild(RowLabels[i]);
        }
    }

    public void AppendEntries(List<MailEntry> entries)
    {
        Entries.AddRange(entries);
        HasMorePosts = entries.Count >= MAX_POSTS_PER_PAGE;
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
        var subject = entry.Subject.Length > MaxSubjectChars ? entry.Subject[..MaxSubjectChars] : entry.Subject;

        return $"{entry.PostId,-POSTID_CHARS}{entry.Author,-AUTHOR_CHARS}{entry.Month + "/" + entry.Day,-DATE_CHARS}{subject}";
    }

    public override void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
    }

    public event CloseHandler? OnClose;
    public event DeletePostHandler? OnDeletePost;

    /// <summary>
    ///     Fired when the user clicks the "Load More" row at the bottom of a full page. The short is the last visible PostId
    ///     to use as the startPostId for the next page request.
    /// </summary>
    public event LoadMorePostsHandler? OnLoadMorePosts;

    public event NewMailHandler? OnNewMail;
    public event ReplyPostHandler? OnReplyPost;
    public event UpHandler? OnUp;
    public event ViewPostHandler? OnViewPost;

    private void RefreshLabels()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var entryIndex = ScrollOffset + i;

            if (HasMorePosts && (entryIndex == Entries.Count))
            {
                RowLabels[i].ForegroundColor = Color.LightGray;
                RowLabels[i].Text = "-- Load More --";
            } else if (entryIndex < Entries.Count)
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
        HasMorePosts = entries.Count >= MAX_POSTS_PER_PAGE;
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

        if ((localX < 0) || (localX >= MailListRect.Width) || (localY < 0) || (localY >= MailListRect.Height))
            return;

        var row = localY / ROW_HEIGHT;

        if (row >= MaxVisibleRows)
            return;

        var entryIndex = ScrollOffset + row;

        //"load more" row
        if (HasMorePosts && (entryIndex == Entries.Count))
        {
            if (Entries.Count > 0)
                OnLoadMorePosts?.Invoke(Entries[^1].PostId);

            return;
        }

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

        if ((localX < 0) || (localX >= MailListRect.Width) || (localY < 0) || (localY >= MailListRect.Height))
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
        if (e.Key == Keys.Escape)
        {
            OnUp?.Invoke();
            e.Handled = true;
        }
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
        //add 1 virtual row for the "load more" indicator when more posts exist
        var totalRows = Entries.Count + (HasMorePosts ? 1 : 0);

        ScrollBar.TotalItems = totalRows;
        ScrollBar.VisibleItems = MaxVisibleRows;
        ScrollBar.MaxValue = Math.Max(0, totalRows - MaxVisibleRows);
    }
}