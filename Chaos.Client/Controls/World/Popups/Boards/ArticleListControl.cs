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
///     Public board article list panel using _narlist prefab. Displays a scrollable list of board posts with author, date,
///     and subject. Buttons: View, New, Delete, Hilight, Up (back to boards), Close.
/// </summary>
public sealed class ArticleListControl : PrefabPanel
{
    //server caps board responses at sbyte.maxvalue posts per page
    private const int MAX_POSTS_PER_PAGE = 127;
    private const int ROW_HEIGHT = Constants.BOARD_ROW_HEIGHT;
    private const int POSTID_CHARS = 5;
    private const int AUTHOR_CHARS = 12;
    private const int DATE_CHARS = 5;
    private const int PREFIX_CHARS = POSTID_CHARS + AUTHOR_CHARS + DATE_CHARS;
    private const string SPACER5 = "     ";
    private const string SPACER3 = "   ";

    private readonly Rectangle ArticleListRect;
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
        };

        AddChild(ScrollBar);

        //row labels — one per visible row, columns via fixed-width string formatting
        var usableWidth = ArticleListRect.Width - ScrollBarControl.DEFAULT_WIDTH;
        MaxSubjectChars = Math.Max(0, usableWidth / TextRenderer.CHAR_WIDTH - PREFIX_CHARS);

        RowLabels = new UILabel[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            RowLabels[i] = new UILabel
            {
                X = ArticleListRect.X,
                Y = ArticleListRect.Y + i * ROW_HEIGHT,
                Width = usableWidth,
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

        var date = $"{entry.Month,2}/{entry.Day,2}";

        return $"{entry.PostId,POSTID_CHARS}{SPACER5}{entry.Author,-AUTHOR_CHARS}{SPACER5}{date,DATE_CHARS}{SPACER3}{subject}";
    }

    public override void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
    }

    public event CloseHandler? OnClose;
    public event DeletePostHandler? OnDeletePost;
    public event HighlightPostHandler? OnHighlight;

    /// <summary>
    ///     Fired when the user clicks the "Load More" row at the bottom of a full page. The short is the last visible PostId
    ///     to use as the startPostId for the next page request.
    /// </summary>
    public event LoadMorePostsHandler? OnLoadMorePosts;

    public event NewPostHandler? OnNewPost;
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

        var localX = e.ScreenX - ScreenX - ArticleListRect.X;
        var localY = e.ScreenY - ScreenY - ArticleListRect.Y;

        if ((localX < 0) || (localX >= ArticleListRect.Width) || (localY < 0) || (localY >= ArticleListRect.Height))
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

        var localX = e.ScreenX - ScreenX - ArticleListRect.X;
        var localY = e.ScreenY - ScreenY - ArticleListRect.Y;

        if ((localX < 0) || (localX >= ArticleListRect.Width) || (localY < 0) || (localY >= ArticleListRect.Height))
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

        if (HighlightButton is { Visible: true })
            HighlightButton.Enabled = hasSelection;
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