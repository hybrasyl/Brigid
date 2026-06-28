#region
using DALib.Networking.Packets.Server;
#endregion

namespace Brigid.ViewModel;

/// <summary>
///     Authoritative group invite state. Fires events when group invites or recruitment info arrives.
/// </summary>
public sealed class GroupInvite
{
    /// <summary>
    ///     The current group response packet, or null if no invite is pending.
    /// </summary>
    public GroupResponsePacket? Current { get; private set; }

    public void Clear() => Current = null;

    /// <summary>
    ///     Fired when a group-related interaction is received from the server.
    /// </summary>
    public event GroupInviteReceivedHandler? Received;

    public void Set(GroupResponsePacket response)
    {
        Current = response;
        Received?.Invoke();
    }
}