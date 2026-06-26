#region
using Brigid.Collections;
using Chaos.Extensions.Common;
#endregion

namespace Brigid.ViewModel;

public delegate void GroupChangedHandler();

/// <summary>
///     Tracks the player's current group membership — members and leader.
/// </summary>
public sealed class GroupState
{
    public string? LeaderName { get; private set; }
    public List<string> Members { get; private set; } = [];
    public bool InGroup => Members.Count > 0;
    public bool IsLeader => InGroup && LeaderName?.EqualsI(WorldState.PlayerName) is true;

    /// <summary>
    ///     True when the local player has published a group recruitment box that has
    ///     not yet been removed or auto-cleared by joining a group.
    /// </summary>
    public bool HasActiveGroupBox { get; private set; }

    public event GroupChangedHandler? Changed;

    /// <summary>
    ///     Sets HasActiveGroupBox = true. Called when the client sends CreateGroupbox
    ///     (optimistic) or receives ShowGroupBox with a self-source name.
    /// </summary>
    public void MarkGroupBoxActive()
    {
        if (HasActiveGroupBox)
            return;

        HasActiveGroupBox = true;
        Changed?.Invoke();
    }

    /// <summary>
    ///     Sets HasActiveGroupBox = false. Called when the client sends RemoveGroupBox,
    ///     when the player joins a group (server auto-clears the box), or on logout reset.
    /// </summary>
    public void MarkGroupBoxInactive()
    {
        if (!HasActiveGroupBox)
            return;

        HasActiveGroupBox = false;
        Changed?.Invoke();
    }

    /// <summary>
    ///     Clears group membership state (members + leader). Does NOT touch
    ///     <see cref="HasActiveGroupBox"/> — that flag is independent of group
    ///     membership (a solo player can have an active recruitment box). The
    ///     self-profile handler calls Clear() on every non-group response, so
    ///     wiping the flag here would erase it on every refresh for recruiting
    ///     solo players. Use <see cref="MarkGroupBoxInactive"/> to clear the
    ///     flag, or <see cref="ResetAll"/> for a full session reset (logout).
    /// </summary>
    public void Clear()
    {
        Members = [];
        LeaderName = null;
        Changed?.Invoke();
    }

    /// <summary>
    ///     Full session reset — clears membership AND the active-groupbox flag.
    ///     Called on logout / disconnect.
    /// </summary>
    public void ResetAll()
    {
        Members = [];
        LeaderName = null;
        HasActiveGroupBox = false;
        Changed?.Invoke();
    }

    /// <summary>
    ///     Parses the server's GroupString format and updates state. Format: "Group members\n* Leader\n  Member2\nTotal N"
    /// </summary>
    public void ParseAndSet(string groupString)
    {
        var members = new List<string>();
        string? leader = null;

        foreach (var line in groupString.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWithI("Group members") || trimmed.StartsWithI("Total ") || (trimmed.Length == 0))
                continue;

            if (trimmed.StartsWithI("* "))
            {
                var name = trimmed[2..];
                leader = name;
                members.Add(name);
            } else
                members.Add(trimmed);
        }

        Members = members;
        LeaderName = leader;

        // Server auto-clears GroupBox when a player joins a group (see
        // docs/research/group-protocol-spec.md §Validation rules).
        if (members.Count >= 2)
            HasActiveGroupBox = false;

        Changed?.Invoke();
    }
}