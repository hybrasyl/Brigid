#region
using Brigid.Controls.Components;
using Brigid.Controls.Scrolling;
using Brigid.Models;
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
    //server caps board responses at sbyte.maxvalue posts per page
    private const int MAX_POSTS_PER_PAGE = 127;
    private const int ROW_HEIGHT = Constants.BOARD_ROW_HEIGHT;
    private const int POSTID_CHARS = 5;
    private const int AUTHOR_CHARS = 12;
    private const int DATE_CHARS = 5;
    private const int PREFIX_CHARS = POSTID_CHARS + AUTHOR_CHARS + DATE_CHARS;
    private const string SPACER5 = "     ";
    private const string SPACER3 = "   ";

    private static readonly Color SelectedColor = new(100, 149, 237);

    private readonly VirtualizedListView<MailEntry, UILabel> ListView;
    private readonly int MaxSubjectChars;

    private List<MailEntry> Entries = [];
    private bool HasMorePosts;
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

        ListView = new VirtualizedListView<MailEntry, UILabel>(
            articleListRect,
            ROW_HEIGHT,
            w => new UILabel
            {
                Width = w,
                Height = ROW_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0
            },
            BindRow)
        {
            Selectable = true
        };

        MaxSubjectChars = Math.Max(0, ListView.ContentWidth / TextRenderer.CHAR_WIDTH - PREFIX_CHARS);

        ListView.SelectionChanged += _ => UpdateButtonStates();

        ListView.ItemActivated += i =>
        {
            if ((i >= 0) && (i < Entries.Count))
                OnViewPost?.Invoke(Entries[i].PostId);
        };

        ListView.TrailingActivated += () =>
        {
            if (Entries.Count > 0)
                OnLoadMorePosts?.Invoke(Entries[^1].PostId);
        };

        AddChild(ListView);
    }

    private bool TrySelected(out int index)
    {
        index = ListView.SelectedIndex;

        return (index >= 0) && (index < Entries.Count);
    }

    private void BindRow(UILabel label, VirtualRow<MailEntry> row)
    {
        switch (row.Kind)
        {
            case VirtualRowKind.Item:
                label.ForegroundColor = row.Selected
                    ? SelectedColor
                    : row.Item.IsHighlighted
                        ? Color.Yellow
                        : TextColors.Default;
                label.Text = FormatRow(row.Item);

                break;
            case VirtualRowKind.Trailing:
                label.ForegroundColor = Color.LightGray;
                label.Text = "-- Load More --";

                break;
            default:
                label.Text = string.Empty;

                break;
        }
    }

    public void AppendEntries(List<MailEntry> entries)
    {
        Entries.AddRange(entries);
        HasMorePosts = entries.Count >= MAX_POSTS_PER_PAGE;

        ListView.Refresh(HasMorePosts);
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

        ListView.Refresh(HasMorePosts);
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
        HasMorePosts = entries.Count >= MAX_POSTS_PER_PAGE;

        ListView.SetItems(entries, HasMorePosts);
        ListView.SetSelectedIndex(-1);
        UpdateButtonStates();
        Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            OnUp?.Invoke();
            e.Handled = true;
        }
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
