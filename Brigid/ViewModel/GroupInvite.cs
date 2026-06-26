#region
using Chaos.Networking.Entities.Server;
#endregion

namespace Brigid.ViewModel;

/// <summary>
///     Authoritative group invite state. Fires events when group invites or recruitment info arrives.
/// </summary>
public sealed class GroupInvite
{
    /// <summary>
    ///     The current group invite args, or null if no invite is pending.
    /// </summary>
    public DisplayGroupInviteArgs? Current { get; private set; }

    public void Clear() => Current = null;

    /// <summary>
    ///     Fired when a group-related interaction is received from the server.
    /// </summary>
    public event GroupInviteReceivedHandler? Received;

    public void Set(DisplayGroupInviteArgs args)
    {
        Current = args;
        Received?.Invoke();
    }
}