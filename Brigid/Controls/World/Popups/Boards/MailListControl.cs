#region
using Brigid.Controls.Components;
using Brigid.Controls.Scrolling;
using Brigid.Models;
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
    //server caps board responses at sbyte.maxvalue posts per page
    private const int MAX_POSTS_PER_PAGE = 127;
    private const int ROW_HEIGHT = Constants.BOARD_ROW_HEIGHT;
    private const int TEXT_INDENT = 24;
    private const int POSTID_CHARS = 6;
    private const int AUTHOR_CHARS = 17;
    private const int DATE_CHARS = 7;
    private const int PREFIX_CHARS = POSTID_CHARS + AUTHOR_CHARS + DATE_CHARS;

    private static readonly Color SelectedColor = new(100, 149, 237);

    private readonly VirtualizedListView<MailEntry, UILabel> ListView;
    private readonly int MaxSubjectChars;

    private List<MailEntry> Entries = [];
    private bool HasMorePosts;
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
            w => new UILabel
            {
                Width = w,
                Height = ROW_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0
            },
            BindRow,
            rowInsetX: TEXT_INDENT)
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
                label.ForegroundColor = row.Selected ? SelectedColor : TextColors.Default;
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

        if (ReplyButton is not null)
            ReplyButton.Enabled = hasSelection;
    }
}
