#region
using Chaos.Networking.Entities.Server;
#endregion

namespace Brigid.ViewModel;

/// <summary>
///     Authoritative player attributes state. Stores the latest <see cref="AttributesArgs" /> from the server. Fires a
///     change event for UI reconciliation.
/// </summary>
public sealed class PlayerAttributes
{
    /// <summary>
    ///     The most recently received attributes from the server, or null if not yet received.
    /// </summary>
    public AttributesArgs? Current { get; private set; }

    /// <summary>
    ///     Fired when attributes are updated by the server.
    /// </summary>
    public event ChangedHandler? Changed;

    /// <summary>
    ///     Clears the stored attributes.
    /// </summary>
    public void Clear() => Current = null;

    /// <summary>
    ///     Updates the stored attributes and fires <see cref="Changed" />.
    /// </summary>
    public void Update(AttributesArgs attrs)
    {
        Current = attrs;
        Changed?.Invoke();
    }
}