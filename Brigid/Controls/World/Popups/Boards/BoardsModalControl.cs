#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Boards;

/// <summary>
///     The bulletin-board and mail modal: one from-scratch <see cref="CenteredModalPanel" /> replacing the seven
///     slide-in prefab panels (<c>_nbdlist</c>, <c>_narlist</c>, <c>_nmaill</c>, <c>_narti</c>, <c>_nmailr</c>,
///     <c>_nartin</c>, <c>_nmails</c>) and the cross-panel hide/show dance that wired them together.
///     <list type="bullet">
///         <item>A <b>Boards</b>/<b>Mail</b> tab row selects which family is in view. Because the server owns that
///         choice, a tab click raises <see cref="BoardsRequested" />/<see cref="MailRequested" /> for the host to
///         turn into the matching request; the tab only becomes selected when the reply arrives.</item>
///         <item>Within a tab, an internal <see cref="BoardView" /> stack swaps between the board index, the post
///         list, the read view and the compose view. The bottom action bar's buttons change per view (and per
///         mode) through <c>SetBottomBarActions</c>.</item>
///     </list>
///     The modal is a passive view: every action raises an event, and the host (WorldScreen) sends the packet and
///     calls back in with the reply. Scroll-back paging, keyboard row navigation, body select/copy, and the
///     compose undo reset are all preserved from the panels this replaces.
/// </summary>
public sealed class BoardsModalControl : CenteredModalPanel
{
    /// <summary>Which pane the modal is currently showing.</summary>
    private enum BoardView
    {
        Index,
        List,
        Read,
        Compose
    }

    private const int PANEL_W = 520;
    private const int PANEL_H = 340;

    private const int TAB_W = 76;
    private const int TAB_H = 20;
    private const int TAB_GAP = 4;
    private const int TAB_TO_PANE = 6;
    private const int TAB_COUNT = 2;

    private const int ACTION_W = 56;

    private static readonly string[] TabTitles = ["Boards", "Mail"];

    private readonly SelectableTab[] Tabs = new SelectableTab[TAB_COUNT];

    private readonly BoardIndexPane IndexPane;
    private readonly PostListPane ListPane;
    private readonly PostReadPane ReadPane;
    private readonly PostComposePane ComposePane;

    private readonly TextButton ViewButton;
    private readonly TextButton NewButton;
    private readonly TextButton ReplyButton;
    private readonly TextButton DeleteButton;
    private readonly TextButton HighlightButton;
    private readonly TextButton PrevButton;
    private readonly TextButton NextButton;
    private readonly TextButton UpButton;
    private readonly TextButton SendButton;

    private BoardView View = BoardView.Index;
    private bool AllowHighlight;

    // ── events (the host turns each into a packet) ──

    public event ViewBoardHandler? BoardSelected;
    public event ViewPostHandler? PostSelected;
    public event NewPostHandler? NewRequested;
    public event ReplyPostHandler? ReplyRequested;
    public event DeletePostHandler? DeleteRequested;
    public event HighlightPostHandler? HighlightRequested;
    public event LoadMorePostsHandler? LoadMoreRequested;
    public event PrevHandler? PrevRequested;
    public event NextHandler? NextRequested;
    public event ArticleSendHandler? PostSubmitted;
    public event MailSendHandler? MailSubmitted;

    /// <summary>Raised when Up is used from the post list without a board index to fall back to.</summary>
    public event UpHandler? SessionEndRequested;

    /// <summary>Raised when the Boards tab is clicked — the host requests the board list.</summary>
    public event Action? BoardsRequested;

    /// <summary>Raised when the Mail tab is clicked — the host requests the mailbox.</summary>
    public event Action? MailRequested;

    // ── state the host reads back ──

    /// <summary>The board currently listed or read.</summary>
    public ushort BoardId => View == BoardView.Read ? ReadBoardId : ListPane.BoardId;

    /// <summary>Whether a scroll-paging request is in flight, so a reply routes to append rather than replace.</summary>
    public bool IsPaging => ListPane.IsPaging;

    /// <summary>Whether the post list is on screen and showing <paramref name="boardId" />'s posts.</summary>
    public bool IsListing(ushort boardId) => Visible && (View == BoardView.List) && (ListPane.BoardId == boardId);

    /// <summary>The board the read view is showing; set by the host alongside <see cref="ShowPost" />.</summary>
    public ushort ReadBoardId { get; set; }

    public BoardsModalControl()
        : base("Boards", PANEL_W, PANEL_H)
    {
        Name = "BoardsModal";

        var content = ContentBounds;
        var pane = new Rectangle(content.X, content.Y + TAB_H + TAB_TO_PANE, content.Width, content.Height - TAB_H - TAB_TO_PANE);

        BuildTabRow(content);

        //created in the order they should read on the bar, right-to-left after Close.
        UpButton = AddBottomBarButton("Up", ACTION_W, GoUp);
        SendButton = AddBottomBarButton("Send", ACTION_W, Submit);
        NextButton = AddBottomBarButton("Next", ACTION_W, () => NextRequested?.Invoke());
        PrevButton = AddBottomBarButton("Prev", ACTION_W, () => PrevRequested?.Invoke());
        HighlightButton = AddBottomBarButton("Hilight", ACTION_W, RaiseHighlight);
        DeleteButton = AddBottomBarButton("Delete", ACTION_W, RaiseDelete);
        ReplyButton = AddBottomBarButton("Reply", ACTION_W, RaiseReply);
        NewButton = AddBottomBarButton("New", ACTION_W, () => NewRequested?.Invoke());
        ViewButton = AddBottomBarButton("View", ACTION_W, ActivateSelection);

        IndexPane = new BoardIndexPane(pane);
        IndexPane.BoardActivated += id => BoardSelected?.Invoke(id);
        IndexPane.SelectionChanged += RefreshActions;
        AddChild(IndexPane);

        ListPane = new PostListPane(pane);
        ListPane.PostActivated += id => PostSelected?.Invoke(id);
        ListPane.SelectionChanged += RefreshActions;
        ListPane.LoadMoreRequested += id => LoadMoreRequested?.Invoke(id);
        AddChild(ListPane);

        ReadPane = new PostReadPane(pane);
        AddChild(ReadPane);

        ComposePane = new PostComposePane(pane);
        AddChild(ComposePane);

        SetView(BoardView.Index);
    }

    private void BuildTabRow(Rectangle content)
    {
        for (var i = 0; i < TAB_COUNT; i++)
        {
            var isMailTab = i == 1;

            var tab = new SelectableTab(TabTitles[i], TAB_W, TAB_H)
            {
                X = content.X + i * (TAB_W + TAB_GAP),
                Y = content.Y
            };

            //the server decides what's on screen, so a tab click is a request, not a local swap. The selected
            //state follows the reply (SetPosts/SetBoards), not the click.
            tab.Clicked += () =>
            {
                if (isMailTab)
                    MailRequested?.Invoke();
                else
                    BoardsRequested?.Invoke();
            };

            Tabs[i] = tab;
            AddChild(tab);
        }
    }

    private void SetActiveTab(bool isMail)
    {
        Tabs[0].IsSelected = !isMail;
        Tabs[1].IsSelected = isMail;
    }

    // ── views ──

    private void SetView(BoardView view)
    {
        View = view;

        IndexPane.Visible = view == BoardView.Index;
        ListPane.Visible = view == BoardView.List;
        ReadPane.Visible = view == BoardView.Read;
        ComposePane.Visible = view == BoardView.Compose;

        if (view != BoardView.Compose)
            ComposePane.ClearFocus();

        RefreshActions();
    }

    //the visible action set is a function of (view, mode, selection) — one place, so the bar can never show an
    //action the current view can't service.
    private void RefreshActions()
    {
        switch (View)
        {
            case BoardView.Index:
                SetBottomBarActions(ViewButton);
                ViewButton.SetEnabled(IndexPane.HasSelection);
                SetTitle("Bulletin Boards");

                break;

            case BoardView.List:
            {
                var hasSelection = ListPane.HasSelection;

                if (ListPane.IsMail)
                    SetBottomBarActions(ViewButton, NewButton, ReplyButton, DeleteButton, UpButton);
                else if (AllowHighlight)
                    SetBottomBarActions(ViewButton, NewButton, DeleteButton, HighlightButton, UpButton);
                else
                    SetBottomBarActions(ViewButton, NewButton, DeleteButton, UpButton);

                ViewButton.SetEnabled(hasSelection);
                ReplyButton.SetEnabled(hasSelection);
                DeleteButton.SetEnabled(hasSelection);
                HighlightButton.SetEnabled(hasSelection);

                break;
            }

            case BoardView.Read:
                if (ListPane.IsMail)
                    SetBottomBarActions(PrevButton, NextButton, NewButton, ReplyButton, DeleteButton, UpButton);
                else
                    SetBottomBarActions(PrevButton, NextButton, NewButton, DeleteButton, UpButton);

                PrevButton.SetEnabled(ReadPane.EnablePrev);

                break;

            case BoardView.Compose:
                SetBottomBarActions(SendButton, UpButton);

                break;
        }
    }

    // ── host entry points ──

    /// <summary>Shows the board index (the reply to a board-list request).</summary>
    public void ShowBoardIndex(List<(ushort BoardId, string Name)> boards)
    {
        SetActiveTab(false);
        IndexPane.SetBoards(boards);
        SetView(BoardView.Index);
        Show();
    }

    /// <summary>Shows a board's (or the mailbox's) first page of posts.</summary>
    public void ShowPostList(
        ushort boardId,
        string boardName,
        List<MailEntry> posts,
        bool isMail,
        bool allowHighlight)
    {
        AllowHighlight = allowHighlight && !isMail;
        SetActiveTab(isMail);
        ListPane.SetPosts(boardId, posts, isMail, boardName);
        SetTitle(isMail ? "Mail" : boardName);
        SetView(BoardView.List);
        Show();
    }

    /// <summary>Appends a scroll-back page to the open post list.</summary>
    public void AppendPosts(List<MailEntry> posts) => ListPane.AppendEntries(posts);

    /// <summary>Clears the in-flight paging flag without appending (a reply that arrived after the user left).</summary>
    public void CancelPaging() => ListPane.CancelPaging();

    /// <summary>Shows a single post.</summary>
    public void ShowPost(
        short postId,
        string author,
        int month,
        int day,
        string subject,
        string message,
        bool enablePrev)
    {
        ReadPane.SetPost(
            postId,
            author,
            month,
            day,
            subject,
            message,
            enablePrev);

        SetTitle(subject);
        SetView(BoardView.Read);
        Show();
    }

    /// <summary>Opens the compose view. <paramref name="recipient" /> pre-fills a mail reply.</summary>
    public void ShowCompose(string? recipient = null)
    {
        ComposePane.Reset(ListPane.IsMail, recipient);
        SetTitle(ListPane.IsMail ? "New Mail" : "New Post");
        SetView(BoardView.Compose);
        Show();
    }

    /// <summary>Removes a post from the list after the server confirms a delete.</summary>
    public void RemovePost(short postId) => ListPane.RemoveEntry(postId);

    /// <summary>Flips a post's highlighted flag after the server confirms.</summary>
    public void ToggleHighlight(short postId) => ListPane.ToggleHighlight(postId);

    /// <summary>The post id the read view is on — the cursor for Prev/Next.</summary>
    public short CurrentPostId => ReadPane.CurrentPostId;

    /// <summary>The author to pre-fill a reply with, from whichever view raised it.</summary>
    public string CurrentAuthor => View == BoardView.Read ? ReadPane.CurrentAuthor : ListPane.SelectedAuthor;

    // ── actions ──

    private void ActivateSelection()
    {
        if (View == BoardView.Index)
            IndexPane.ActivateSelected();
        else if (View == BoardView.List)
            ListPane.ActivateSelected();
    }

    //the post an action applies to: the one being read, or the one selected in the list.
    private short? TargetPostId => View == BoardView.Read ? ReadPane.CurrentPostId : ListPane.SelectedPostId;

    private void RaiseDelete()
    {
        if (TargetPostId is { } postId)
            DeleteRequested?.Invoke(postId);
    }

    private void RaiseReply()
    {
        if (TargetPostId is { } postId)
            ReplyRequested?.Invoke(postId);
    }

    private void RaiseHighlight()
    {
        if (ListPane.SelectedPostId is { } postId)
            HighlightRequested?.Invoke(postId);
    }

    private void Submit()
    {
        if (!ComposePane.CanSend)
            return;

        if (ComposePane.IsMail)
            MailSubmitted?.Invoke(ComposePane.Recipient, ComposePane.Subject, ComposePane.Body);
        else
            PostSubmitted?.Invoke(ComposePane.Subject, ComposePane.Body);
    }

    /// <summary>
    ///     Up: read and compose fall back to the post list; the post list falls back to the board index when the
    ///     session was opened from it, and otherwise ends the session (matching the legacy panels' Up behavior).
    /// </summary>
    private void GoUp()
    {
        switch (View)
        {
            case BoardView.Read:
            case BoardView.Compose:
                SetView(BoardView.List);

                break;

            case BoardView.List:
                if (WorldState.Board.WasOpenedFromBoardList && WorldState.Board.AvailableBoards is { Count: > 0 })
                {
                    IndexPane.SetBoards(
                        WorldState.Board
                                  .AvailableBoards
                                  .Select(b => (b.BoardId, b.Name))
                                  .ToList());

                    SetActiveTab(false);
                    SetView(BoardView.Index);
                } else
                    SessionEndRequested?.Invoke();

                break;

            case BoardView.Index:
                Hide();

                break;
        }
    }

    // ── input ──

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (View)
        {
            case BoardView.Index:
                if (HandleListKey(e, IndexPane.MoveSelection, IndexPane.VisibleRows, IndexPane.ActivateSelected, null))
                    return;

                break;

            case BoardView.List:
                if (HandleListKey(e, ListPane.MoveSelection, ListPane.VisibleRows, ListPane.ActivateSelected, RaiseDelete))
                    return;

                break;

            case BoardView.Read:
                if (e.Key == Keys.Escape)
                {
                    GoUp();
                    e.Handled = true;

                    return;
                }

                //give the selectable body its clipboard/caret keys before the base swallows the rest.
                base.OnKeyDown(e);
                ReadPane.ForwardKey(e);

                return;

            case BoardView.Compose:
                if (e.Key == Keys.Escape)
                {
                    GoUp();
                    e.Handled = true;

                    return;
                }

                if ((e.Key == Keys.Enter) && ComposePane.HandleEnter())
                {
                    e.Handled = true;

                    return;
                }

                break;
        }

        base.OnKeyDown(e);
    }

    //shared row-navigation keys for the two list views. Escape steps back rather than closing outright, matching
    //the legacy panels (which mapped Escape to Up).
    private bool HandleListKey(
        KeyDownEvent e,
        Action<int> moveSelection,
        int visibleRows,
        Action activate,
        Action? delete)
    {
        switch (e.Key)
        {
            case Keys.Escape:
                GoUp();

                break;

            case Keys.Up:
                moveSelection(-1);

                break;

            case Keys.Down:
                moveSelection(1);

                break;

            case Keys.PageUp:
                moveSelection(-visibleRows);

                break;

            case Keys.PageDown:
                moveSelection(visibleRows);

                break;

            case Keys.Enter:
                activate();

                break;

            case Keys.Delete:
                delete?.Invoke();

                break;

            default:
                return false;
        }

        e.Handled = true;

        return true;
    }
}
