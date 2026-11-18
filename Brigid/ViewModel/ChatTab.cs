namespace Brigid.ViewModel;

/// <summary>A tab of the chat box of the new HUD: its name and the channels that it shows (BRIG-127 D24, D26).</summary>
public sealed record ChatTab(string Name, IReadOnlySet<ChatChannel> Channels)
{
    /// <summary>The orange bar and the notices (D10).</summary>
    public static readonly ChatTab System = new("System", new HashSet<ChatChannel> { ChatChannel.System });

    /// <summary>Hybrasyl's combat log (D25); empty on retail.</summary>
    public static readonly ChatTab Combat = new("Combat", new HashSet<ChatChannel> { ChatChannel.Combat });

    /// <summary>The player channels.</summary>
    public static readonly ChatTab Chat = new(
        "Chat",
        new HashSet<ChatChannel>
        {
            ChatChannel.Say, ChatChannel.Shout, ChatChannel.Npc, ChatChannel.Whisper, ChatChannel.Group, ChatChannel.Guild
        });

    /// <summary>The three tabs that every chat box has, in their order.</summary>
    public static readonly IReadOnlyList<ChatTab> Fixed = [System, Combat, Chat];

    /// <summary>True when the tab shows the entry: an entry with text, in one of the tab's channels.</summary>
    public bool Shows(ChatEntry entry) => (entry.Text.Length > 0) && Channels.Contains(entry.Channel);
}
