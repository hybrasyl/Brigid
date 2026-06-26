#region
using Brigid.Models;
#endregion

namespace Brigid.ViewModel;

/// <summary>
///     Authoritative world list (online players) state. Fires a change event when the list is updated by the server.
/// </summary>
public sealed class WorldList
{
    /// <summary>
    ///     The current list of online players, or empty if not yet received.
    /// </summary>
    public IReadOnlyList<WorldListEntry> Entries { get; private set; } = [];

    /// <summary>
    ///     The total number of players online (may differ from Entries count due to filtering).
    /// </summary>
    public ushort TotalOnline { get; private set; }

    /// <summary>
    ///     Fired when the world list is updated with new data.
    /// </summary>
    public event ChangedHandler? Changed;

    /// <summary>
    ///     Clears the world list.
    /// </summary>
    public void Clear()
    {
        Entries = [];
        TotalOnline = 0;
    }

    /// <summary>
    ///     Updates the world list with new data from the server. Fires <see cref="Changed" />.
    /// </summary>
    public void Update(IReadOnlyList<WorldListEntry> entries, ushort totalOnline)
    {
        Entries = entries;
        TotalOnline = totalOnline;
        Changed?.Invoke();
    }
}