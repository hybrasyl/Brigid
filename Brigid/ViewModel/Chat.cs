#region
using Brigid.Collections;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.ViewModel;

/// <summary>
///     Authoritative chat and message state: the one chat log, in which each message is one entry with its channel. The
///     chat tabs of the new HUD are filtered views of the log (BRIG-145, BRIG-1). The classic and large HUDs keep the
///     orange bar history, which holds the entries that <see cref="ShowsInOrangeBar" /> selects, until BRIG-156.
/// </summary>
public sealed class Chat
{
    private const int MAX_ENTRIES = 1000;
    private const int MAX_HISTORY = 1000;

    private readonly CircularBuffer<ChatEntry> Log = new(MAX_ENTRIES);
    private readonly CircularBuffer<ChatEntry> OrangeBarHistory = new(MAX_HISTORY);
    private long AddedCount;

    /// <summary>The chat log, oldest first. It holds the last 1000 entries.</summary>
    public IReadOnlyList<ChatEntry> Entries => Log;

    /// <summary>
    ///     The sequence number of <c>Entries[0]</c>: the entry at index i has the number <c>FirstSequence + i</c>. The
    ///     number of an entry does not change while the cap removes older entries, and a clear does not reset it.
    /// </summary>
    public long FirstSequence => AddedCount - Log.Count;

    /// <summary>Adds one message to the log, and to the orange bar history when its channel shows there.</summary>
    public void Add(ChatChannel channel, string text, Color color)
    {
        var entry = new ChatEntry(text, color, channel);
        Log.Add(entry);
        AddedCount++;

        if (ShowsInOrangeBar(channel))
            OrangeBarHistory.Add(entry);

        EntryAdded?.Invoke(entry);
    }

    /// <summary>Clears the log and the orange bar history.</summary>
    public void Clear()
    {
        Log.Clear();
        OrangeBarHistory.Clear();
        Cleared?.Invoke();
    }

    /// <summary>Fired when the log and the history are cleared.</summary>
    public event ClearedHandler? Cleared;

    /// <summary>Fired after an entry is added to the log.</summary>
    public event ChatEntryAddedHandler? EntryAdded;

    /// <summary>The orange bar history of the classic and large HUDs, oldest first.</summary>
    public IReadOnlyList<ChatEntry> GetOrangeBarHistory() => OrangeBarHistory;

    /// <summary>True when the legacy chat panel shows the channel: every channel but the system notices.</summary>
    public static bool ShowsInChatPanel(ChatChannel channel) => channel != ChatChannel.System;

    /// <summary>
    ///     True when the legacy orange bar and its history show the channel: the system notices, and the whispers,
    ///     the group and the guild messages and the combat log lines (group messages), which the orange bar repeats.
    /// </summary>
    public static bool ShowsInOrangeBar(ChatChannel channel)
        => channel is ChatChannel.System or ChatChannel.Whisper or ChatChannel.Group or ChatChannel.Guild or ChatChannel.Combat;
}

/// <summary>One message of the chat log: its text, its display colour and its channel.</summary>
public readonly record struct ChatEntry(string Text, Color Color, ChatChannel Channel);
