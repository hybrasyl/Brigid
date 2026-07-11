namespace Brigid.Networking;

/// <summary>
///     Board/mail (0x3B) protocol constants shared between the outbound page request and the client-side scroll paging, so
///     the "a full page arrived, there may be more" threshold can never drift from the size of the batch we ask for.
/// </summary>
public static class BoardProtocol
{
    /// <summary>
    ///     Number of posts requested per page — the magnitude of the 0x02 <c>navOffset</c>. The retail client's
    ///     first-page open sends <c>navOffset = -16</c> ("load the 16 most recent"); Brigid mirrors that. The list
    ///     controls page for older posts whenever a full page of this size arrives and the user scrolls to the bottom.
    /// </summary>
    public const int PageSize = 16;
}
