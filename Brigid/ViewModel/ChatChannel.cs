namespace Brigid.ViewModel;

/// <summary>
///     The channel of a chat entry: what the retail message types let the client tell apart (BRIG-127 D26). The chat
///     tabs filter the one chat log by channel.
///     <para>
///         Add a channel at the end, and do not rename one: the custom chat tabs save the channel names in
///         <c>brigid.cfg</c>. BRIG-23 adds channels here; its wire channel type in <c>Brigid.Networking</c> has another
///         name, and the client maps it to this enum.
///     </para>
/// </summary>
public enum ChatChannel
{
    /// <summary>A public message of an Aisling, or one with no known source.</summary>
    Say,
    Shout,

    /// <summary>A public message of a creature or a merchant.</summary>
    Npc,
    Whisper,
    Group,
    Guild,

    /// <summary>The orange bar: server notices, and the notices of the client.</summary>
    System,

    /// <summary>A line of Hybrasyl's combat log (<see cref="CombatLog" />), which arrives as a group message.</summary>
    Combat
}
