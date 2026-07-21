#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Models;
using Brigid.Systems.Keybinds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Boards;

/// <summary>
///     The bulletin-board and mail modal: one from-scratch <see cref="CenteredModalPanel" /> replacing the seven
///     slide-in prefab panels (<c>_nbdlist</c>, <c>_narlist</c>, <c>_nmaill</c>, <c>_narti</c>, <c>_nmailr</c>,
///     <c>_nartin</c>, <c>_nmails</c>) and the cross-panel hide/show dance that wired them together.
///     An internal <see cref="BoardView" /> stack swaps between the board index, the post list, the read view and
///     the compose view, and the bottom action bar's buttons change per view through <c>SetBottomBarActions</c>.
///     The mailbox is just another board (id 0) reached from the index, so it needs no separate surface — only a
///     mode flag, for the Reply action and the compose recipient field.
///     <para>
///         The modal is a passive view: every action raises an event, and the host (WorldScreen) sends the packet
///         and calls back in with the reply. Scroll-back paging, keyboard row navigation, body select/copy, and the
///         compose undo reset are all preserved from the panels this replaces.
///     </para>
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
    private const int PANEL_H = 320;

    private const int ACTION_W = 56;

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

    //the family and board the current view belongs to. Held here rather than read off the post list, because the
    //server can push a post (or the mailbox) with no list ever having been shown.
    private bool IsMail;
    private ushort ActiveBoardId;

    //the post list's title, so stepping back up from read/compose restores it instead of leaving the post's
    //subject in the title band.
    private string ListTitle = string.Empty;

    //the board index as last shown, and whether the session reached the post list through it — memoized so Up
    //can step back without reaching into WorldState.
    private List<(ushort BoardId, string Name)> IndexBoards = [];
    private bool OpenedFromIndex;
    private bool PendingIndexOpen;

    // ── events (the host turns each into a packet) ──

    public event ViewBoardHandler? BoardSelected;
    public event ViewPostHandler? PostSelected;
    public event DeletePostHandler? DeleteRequested;
    public event HighlightPostHandler? HighlightRequested;
    public event LoadMorePostsHandler? LoadMoreRequested;
    /// <summary>Raised to step to the neighbouring post while reading.</summary>
    public event StepPostHandler? StepRequested;

    /// <summary>
    ///     Raised when a composed post is submitted. The recipient is null for a bulletin-board post and the
    ///     addressee for mail — one event, because there is one compose view.
    /// </summary>
    public event SubmitPostHandler? SubmitRequested;

    /// <summary>Raised when Up is used from the post list without a board index to fall back to.</summary>
    public event UpHandler? SessionEndRequested;

    // ── state the host reads back ──

    /// <summary>The board the current view belongs to — the target for every action the host turns into a packet.</summary>
    public ushort BoardId => ActiveBoardId;

    /// <summary>
    ///     Whether a scroll-back page for <paramref name="boardId" /> is in flight, so the host routes that board's
    ///     reply to <see cref="AppendPosts" /> rather than replacing the list. Qualified by board: a request pending
    ///     on one board must never claim another's reply.
    /// </summary>
    public bool IsPagingFor(ushort boardId) => ListPane.IsPagingFor(boardId);

    /// <summary>Whether the post list is on screen and showing <paramref name="boardId" />'s posts.</summary>
    public bool IsListing(ushort boardId) => Visible && (View == BoardView.List) && ListPane.IsShowing(boardId);

    public BoardsModalControl()
        : base("Boards", PANEL_W, PANEL_H)
    {
        Name = "BoardsModal";

        var pane = ContentBounds;

        IndexPane = new BoardIndexPane(pane);

        //armed here, consumed by the matching ShowPostList reply — so a board the server pushes later doesn't
        //inherit "you got here from the index" and offer Up back into a stale one.
        IndexPane.BoardActivated += id =>
        {
            PendingIndexOpen = true;
            BoardSelected?.Invoke(id);
        };

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

        //after the panes, since Prev/Next read the read pane's cursor. Created in the order they should read on
        //the bar, which packs right-to-left after Close.
        UpButton = AddBottomBarButton("Up", ACTION_W, GoUp);
        SendButton = AddBottomBarButton("Send", ACTION_W, Submit);
        NextButton = AddBottomBarButton("Next", ACTION_W, () => StepRequested?.Invoke(ReadPane.CurrentPostId, true));
        PrevButton = AddBottomBarButton("Prev", ACTION_W, () => StepRequested?.Invoke(ReadPane.CurrentPostId, false));
        HighlightButton = AddBottomBarButton("Hilight", ACTION_W, RaiseHighlight);
        DeleteButton = AddBottomBarButton("Delete", ACTION_W, RaiseDelete);
        ReplyButton = AddBottomBarButton("Reply", ACTION_W, () => ShowCompose(CurrentAuthor));

        //composing is a pure view transition, so it stays here: only the modal knows whether the party row should
        //be an editable recipient (new mail) or the read-only author display (a board post).
        NewButton = AddBottomBarButton("New", ACTION_W, () => ShowCompose(IsMail ? null : WorldState.PlayerName));
        ViewButton = AddBottomBarButton("View", ACTION_W, ActivateSelection);

        SetView(BoardView.Index);
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
                SetTitle(ListTitle);

                if (IsMail)
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
                if (IsMail)
                    SetBottomBarActions(PrevButton, NextButton, NewButton, ReplyButton, DeleteButton, UpButton);
                else
                    SetBottomBarActions(PrevButton, NextButton, NewButton, DeleteButton, UpButton);

                PrevButton.SetEnabled(ReadPane.EnablePrev);

                //the read view always has a post to act on, whatever the list's selection happened to be.
                ReplyButton.SetEnabled(true);
                DeleteButton.SetEnabled(true);

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
        IsMail = false;
        IndexBoards = boards;
        IndexPane.SetBoards(boards);
        SetView(BoardView.Index);
        Show();
    }

    /// <summary>Shows a board's (or the mailbox's) first page of posts.</summary>
    public void ShowPostList(
        ushort boardId,
        string? boardName,
        List<MailEntry> posts,
        bool isMail,
        bool allowHighlight)
    {
        IsMail = isMail;
        ActiveBoardId = boardId;
        OpenedFromIndex = PendingIndexOpen;
        PendingIndexOpen = false;
        AllowHighlight = allowHighlight && !isMail;

        ListTitle = isMail ? "Mail" : boardName ?? "Board";

        ListPane.SetPosts(boardId, posts, isMail, ListTitle);
        SetView(BoardView.List);
        Show();
    }

    /// <summary>Appends a scroll-back page to the open post list.</summary>
    public void AppendPosts(List<MailEntry> posts) => ListPane.AppendEntries(posts);

    /// <summary>Clears the in-flight paging flag without appending (a reply that arrived after the user left).</summary>
    public void CancelPaging() => ListPane.CancelPaging();

    /// <summary>
    ///     Shows a single post. The family and board come from the caller, not from whatever the post list last
    ///     held — the server can push a post with no list ever having been shown.
    /// </summary>
    public void ShowPost(
        ushort boardId,
        bool isMail,
        short postId,
        string author,
        int month,
        int day,
        string subject,
        string message,
        bool enablePrev)
    {
        IsMail = isMail;
        ActiveBoardId = boardId;

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

    /// <summary>
    ///     Opens the compose view against the board currently in view. <paramref name="party" /> is the recipient
    ///     for a mail reply, or the author to display on a board post.
    /// </summary>
    public void ShowCompose(string? party = null)
    {
        ComposePane.Reset(IsMail, party);
        SetTitle(IsMail ? "New Mail" : "New Post");
        SetView(BoardView.Compose);
        Show();
    }

    public override void Hide()
    {
        ComposePane.ClearFocus();

        //closing the modal ends the board session, which is where the legacy board-list flag was cleared —
        //otherwise a later signpost-pushed board would still offer Up back into a stale index.
        OpenedFromIndex = false;
        PendingIndexOpen = false;
        base.Hide();
    }

    /// <summary>
    ///     Removes a post from the list after the server confirms a delete. A delete confirmed while reading that
    ///     post steps back to the list, so the modal is never left showing a post that no longer exists.
    /// </summary>
    public void RemovePost(short postId)
    {
        ListPane.RemoveEntry(postId);

        if ((View == BoardView.Read) && (ReadPane.CurrentPostId == postId))
            ReturnToList();
    }

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

    private void RaiseHighlight()
    {
        if (ListPane.SelectedPostId is { } postId)
            HighlightRequested?.Invoke(postId);
    }

    private void Submit()
    {
        if (!ComposePane.CanSend)
            return;

        SubmitRequested?.Invoke(IsMail ? ComposePane.Recipient : null, ComposePane.Subject, ComposePane.Body);
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
                ReturnToList();

                break;

            case BoardView.List:
                if (OpenedFromIndex && (IndexBoards.Count > 0))
                {
                    IsMail = false;
                    IndexPane.SetBoards(IndexBoards);
                    SetView(BoardView.Index);
                } else
                    SessionEndRequested?.Invoke();

                break;

            case BoardView.Index:
                Hide();

                break;
        }
    }

    /// <summary>
    ///     Steps back to the post list — but only when the list is actually showing this board. A post pushed by
    ///     the server for a different board (signpost, mail notification) leaves the list holding someone else's
    ///     rows, and showing those under the active board's id would target every action at the wrong board.
    /// </summary>
    private void ReturnToList()
    {
        if (ListPane.IsShowing(ActiveBoardId))
        {
            SetView(BoardView.List);

            return;
        }

        if (OpenedFromIndex && (IndexBoards.Count > 0))
        {
            IsMail = false;
            IndexPane.SetBoards(IndexBoards);
            SetView(BoardView.Index);
        } else
            SessionEndRequested?.Invoke();
    }

    // ── input ──

    public override void OnKeyDown(KeyDownEvent e)
    {
        //the modal swallows every key it doesn't use, so the world hotkey can't reach the screen — honour the
        //board toggle here so W still closes the board UI the way it always has.
        if (Keybinds.Matches(e, CommandId.World_ToggleBoard))
        {
            Hide();
            e.Handled = true;

            return;
        }

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

                //the body gets its clipboard/caret keys first; the base then swallows whatever is left so world
                //hotkeys can't fire underneath.
                ReadPane.ForwardKey(e);
                base.OnKeyDown(e);

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
                if (delete is null)
                    return false;

                delete();

                break;

            default:
                return false;
        }

        e.Handled = true;

        return true;
    }
}
