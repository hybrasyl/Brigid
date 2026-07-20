#region
using Brigid.Models;
using DALib.Networking.Packets.Server;
#endregion

namespace Brigid.ViewModel;

/// <summary>
///     Authoritative bulletin board / mail state. Only one board can be viewed at a time. Fires events when the server
///     sends board display commands.
/// </summary>
public sealed class Board
{
    private readonly List<MailEntry> PostList = [];

    /// <summary>
    ///     The available boards list (from BoardList response).
    /// </summary>
    public ICollection<(ushort BoardId, string Name)>? AvailableBoards { get; private set; }

    /// <summary>
    ///     The currently viewed board ID.
    /// </summary>
    public ushort BoardId { get; private set; }

    /// <summary>
    ///     The currently viewed post, or null if viewing the list.
    /// </summary>
    public PostData? CurrentPost { get; private set; }

    /// <summary>
    ///     Whether the previous-page button should be enabled for the post list.
    /// </summary>
    public bool EnablePrevButton { get; private set; }

    /// <summary>
    ///     Whether the current board is a public bulletin board (vs personal mail).
    /// </summary>
    public bool IsPublicBoard { get; private set; }

    /// <summary>
    ///     Whether a board session is currently open (any board panel visible).
    /// </summary>
    public bool IsSessionOpen { get; private set; }

    /// <summary>
    ///     The posts from the most recent board response only. On a scroll-paging fetch this holds just the newly
    ///     returned page, not the full accumulated list — the list control accumulates the displayed set. Read this as
    ///     "the latest page", not "what is on screen".
    /// </summary>
    public IReadOnlyList<MailEntry> Posts => PostList;

    /// <summary>
    ///     Fired when a board list is received (multiple boards available).
    /// </summary>
    public event BoardListReceivedHandler? BoardListReceived;

    public void Clear()
    {
        PostList.Clear();
        CurrentPost = null;
        AvailableBoards = null;
    }

    /// <summary>
    ///     Closes the board session and resets all state.
    /// </summary>
    public void CloseSession()
    {
        if (!IsSessionOpen)
            return;

        IsSessionOpen = false;
        Clear();
        SessionClosed?.Invoke();
    }

    /// <summary>
    ///     The display name for a board id, from the board list. Null when the board was pushed by the server
    ///     without a preceding list (a signpost click), which carries only the id.
    /// </summary>
    public string? GetBoardName(ushort boardId)
    {
        if (AvailableBoards is null)
            return null;

        foreach (var entry in AvailableBoards)
            if (entry.BoardId == boardId)
                return entry.Name;

        return null;
    }

    public void HandleResponse(string message, bool success) => ResponseReceived?.Invoke(message, success);

    /// <summary>
    ///     Opens a new board session.
    /// </summary>
    public void OpenSession()
    {
        IsSessionOpen = true;
    }

    /// <summary>
    ///     Fired when the post list is shown or updated (new page appended).
    /// </summary>
    public event PostListChangedHandler? PostListChanged;

    /// <summary>
    ///     Fired when a single post is displayed for reading.
    /// </summary>
    public event PostViewedHandler? PostViewed;

    /// <summary>
    ///     Fired when a server response message is received (submit/delete/highlight result).
    /// </summary>
    public event BoardResponseReceivedHandler? ResponseReceived;

    /// <summary>
    ///     Fired when the board session is closed (all panels should hide).
    /// </summary>
    public event SessionClosedHandler? SessionClosed;

    public void ShowBoardList(IList<BoardListEntry> boards)
    {
        AvailableBoards = boards
                          .Select(b => (b.Id, b.Name))
                          .ToList();
        BoardListReceived?.Invoke();
    }

    public void ShowPost(
        short postId,
        string author,
        int month,
        int day,
        string subject,
        string message,
        bool enablePrev)
    {
        CurrentPost = new PostData(
            postId,
            author,
            month,
            day,
            subject,
            message);
        EnablePrevButton = enablePrev;
        PostViewed?.Invoke();
    }

    public void ShowPostList(ushort boardId, List<MailEntry> posts, bool isPublic)
    {
        BoardId = boardId;
        PostList.Clear();
        PostList.AddRange(posts);
        IsPublicBoard = isPublic;
        CurrentPost = null;
        PostListChanged?.Invoke();
    }

    public readonly record struct PostData(
        short PostId,
        string Author,
        int MonthOfYear,
        int DayOfMonth,
        string Subject,
        string Message);
}