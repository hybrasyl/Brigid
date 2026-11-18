#region
using System.Diagnostics.CodeAnalysis;
#endregion

namespace Brigid.ViewModel;

/// <summary>
///     A tab of the chat box of the new HUD: its name and the channels that it shows (BRIG-127 D24, D26). The three fixed
///     tabs are here; the player adds up to three custom tabs (BRIG-145), which <c>brigid.cfg</c> keeps as one
///     <see cref="ToSetting" /> line each. A tab is one object: the chat box finds a tab by reference, and an edit
///     makes a new tab in its place.
/// </summary>
public sealed class ChatTab(string name, IReadOnlySet<ChatChannel> channels)
{
    /// <summary>The longest name of a custom tab.</summary>
    public const int MAX_NAME = 8;

    /// <summary>The name in the tab row; unique in the row, in any case.</summary>
    public string Name { get; } = name;

    /// <summary>The channels whose entries the tab shows.</summary>
    public IReadOnlySet<ChatChannel> Channels { get; } = channels;

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

    /// <summary>
    ///     True when <paramref name="name" /> can name a custom tab: 1 to <see cref="MAX_NAME" /> letters, digits and
    ///     spaces, with no space at the start or the end.
    /// </summary>
    public static bool IsValidName(string name)
        => (name.Length is > 0 and <= MAX_NAME)
           && (name.Trim().Length == name.Length)
           && name.All(c => char.IsLetterOrDigit(c) || (c == ' '));

    /// <summary>True when the tab can be a custom tab: a name that <see cref="IsValidName" /> takes, and at least one channel.</summary>
    public bool IsValidCustom => IsValidName(Name) && (Channels.Count > 0);

    /// <summary>The value of the tab's <c>ChatTab</c> line in <c>brigid.cfg</c>: the name, then the channels in their order.</summary>
    public string ToSetting() => $"{Name}|{string.Join(",", Channels.Order())}";

    /// <summary>
    ///     Reads a <see cref="ToSetting" /> value. False for a value with a name that <see cref="IsValidName" /> refuses,
    ///     no channel, or a channel that <see cref="ChatChannel" /> does not name.
    /// </summary>
    public static bool TryParseSetting(string value, [NotNullWhen(true)] out ChatTab? tab)
    {
        tab = null;
        var parts = value.Split('|');

        if (parts.Length != 2)
            return false;

        var channels = new HashSet<ChatChannel>();

        foreach (var name in parts[1].Split(','))
        {
            //only a channel name: Enum.TryParse also takes a number
            if (!Enum.GetNames<ChatChannel>().Contains(name))
                return false;

            channels.Add(Enum.Parse<ChatChannel>(name));
        }

        tab = new ChatTab(parts[0], channels);

        if (tab.IsValidCustom)
            return true;

        tab = null;

        return false;
    }
}
