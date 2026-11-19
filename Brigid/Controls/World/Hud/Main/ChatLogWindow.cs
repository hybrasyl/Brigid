#region
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     The lines that a chat tab shows: a window of <see cref="LineCount" /> lines over the entries of the chat log that
///     the tab shows, the newest at the bottom (BRIG-145). The window wraps only the entries that it reads, through the
///     wrapper that the view gives, so its cost does not grow with the backlog (BRIG-1).
///     <para>
///         The position is the sequence number of the bottom entry and the number of its lines that are below the
///         bottom. With no bottom entry, the window follows the newest entry. When the cap of the log removes the bottom
///         entry, the oldest line goes to the top of the window. The window checks its position each time that it reads
///         the log, so a read can change the position.
///     </para>
/// </summary>
internal sealed class ChatLogWindow
{
    /// <summary>The wrapped lines of an entry: at least one line for an entry with text.</summary>
    internal delegate IReadOnlyList<string> Wrapper(long sequence, ChatEntry entry);

    private static readonly ChatTab NoTab = new(string.Empty, new HashSet<ChatChannel>());

    private readonly Chat Log;
    private readonly Wrapper Wrap;
    private ChatTab ShownTab = NoTab;
    private long? BottomSequence;
    private int HiddenLines;

    public ChatLogWindow(Chat log, int lineCount, Wrapper wrap)
    {
        Log = log;
        LineCount = lineCount;
        Wrap = wrap;
    }

    /// <summary>The number of lines that the window shows. The chat box changes it while the chat input shows.</summary>
    public int LineCount { get; set; }

    /// <summary>True while the window follows the newest entry.</summary>
    public bool IsAtNewest => BottomSequence is null;

    /// <summary>
    ///     The tab whose entries the window shows. A change goes back to the newest entry; the tab that the window shows
    ///     keeps its place.
    /// </summary>
    public ChatTab Tab
    {
        get => ShownTab;
        set
        {
            if (ReferenceEquals(ShownTab, value))
                return;

            ShownTab = value;
            ScrollToNewest();
        }
    }

    /// <summary>The number of entries that the window can show.</summary>
    public int MatchCount
    {
        get
        {
            var count = 0;

            for (var i = 0; i < Log.Entries.Count; i++)
                if (ShownTab.Shows(Log.Entries[i]))
                    count++;

            return count;
        }
    }

    /// <summary>The number of entries that the window can show below its bottom entry: 0 at the newest entry.</summary>
    public int EntriesBelow
    {
        get
        {
            var bottom = BottomIndex();

            if (bottom < 0)
                return 0;

            var count = 0;

            for (var i = bottom + 1; i < Log.Entries.Count; i++)
                if (ShownTab.Shows(Log.Entries[i]))
                    count++;

            return count;
        }
    }

    /// <summary>Goes back to the newest entry, which the window then follows.</summary>
    public void ScrollToNewest()
    {
        BottomSequence = null;
        HiddenLines = 0;
    }

    /// <summary>
    ///     Puts the bottom of the window on the entry that has <paramref name="entriesBelow" /> entries of the window below
    ///     it, with all its lines; 0 follows the newest entry. The scroll bar of the view sets the position this way.
    /// </summary>
    public void ScrollToEntry(int entriesBelow)
    {
        if (entriesBelow <= 0)
        {
            ScrollToNewest();

            return;
        }

        var below = 0;

        for (var i = Log.Entries.Count - 1; i >= 0; i--)
        {
            if (!ShownTab.Shows(Log.Entries[i]))
                continue;

            if (below == entriesBelow)
            {
                BottomSequence = Log.FirstSequence + i;
                HiddenLines = 0;

                return;
            }

            below++;
        }
    }

    /// <summary>Moves the window <paramref name="lines" /> lines to older entries. Returns false when it did not move.</summary>
    public bool ScrollBack(int lines)
    {
        var moved = false;

        for (var n = 0; (n < lines) && CanScrollBack(); n++)
        {
            var bottom = BottomIndex();
            var lineCount = LinesOf(bottom).Count;

            if (HiddenLines + 1 < lineCount)
            {
                BottomSequence = Log.FirstSequence + bottom;
                HiddenLines++;
            } else
            {
                BottomSequence = Log.FirstSequence + PreviousIndex(bottom);
                HiddenLines = 0;
            }

            moved = true;
        }

        return moved;
    }

    /// <summary>Moves the window <paramref name="lines" /> lines to newer entries. Returns false when it did not move.</summary>
    public bool ScrollForward(int lines)
    {
        var moved = false;

        for (var n = 0; (n < lines) && !IsAtNewest; n++)
        {
            var bottom = BottomIndex();

            if (bottom < 0)
            {
                ScrollToNewest();

                return moved;
            }

            if (HiddenLines > 0)
                HiddenLines--;
            else
            {
                var next = NextIndex(bottom);

                if (next < 0)
                {
                    ScrollToNewest();

                    return true;
                }

                BottomSequence = Log.FirstSequence + next;
                HiddenLines = LinesOf(next).Count - 1;
            }

            FollowAtNewest();
            moved = true;
        }

        return moved;
    }

    /// <summary>The lines that the window shows, from the top line down; fewer than <see cref="LineCount" /> while the tab has few.</summary>
    public IReadOnlyList<(string Text, Color Color)> VisibleLines() => Visible().Lines;

    /// <summary>
    ///     The lines that the window shows, from the top line down, and the number of entries that they come from: the
    ///     page of the scroll bar.
    /// </summary>
    public (IReadOnlyList<(string Text, Color Color)> Lines, int Entries) Visible()
    {
        var shown = new List<(string Text, Color Color)>(LineCount);
        var entries = 0;
        var index = BottomIndex();
        var hidden = HiddenLines;

        while ((index >= 0) && (shown.Count < LineCount))
        {
            var entry = Log.Entries[index];
            var lines = LinesOf(index);
            entries++;

            for (var i = lines.Count - 1 - Math.Min(hidden, lines.Count - 1); (i >= 0) && (shown.Count < LineCount); i--)
                shown.Add((lines[i], entry.Color));

            hidden = 0;
            index = PreviousIndex(index);
        }

        shown.Reverse();

        return (shown, entries);
    }

    //true when the window has a line above its top line
    private bool CanScrollBack()
    {
        var index = BottomIndex();

        if (index < 0)
            return false;

        var lines = LinesOf(index).Count - Math.Min(HiddenLines, LinesOf(index).Count - 1);

        while (lines <= LineCount)
        {
            index = PreviousIndex(index);

            if (index < 0)
                return false;

            lines += LinesOf(index).Count;
        }

        return true;
    }

    //at the newest entry with all its lines, the window follows the new entries again
    private void FollowAtNewest()
    {
        if ((HiddenLines == 0) && (NextIndex(BottomIndex()) < 0))
            ScrollToNewest();
    }

    //the log index of the bottom entry, or -1 when the window shows no entry
    private int BottomIndex()
    {
        if (BottomSequence is not { } sequence)
            return PreviousIndex(Log.Entries.Count);

        var index = sequence - Log.FirstSequence;

        if ((index >= 0) && (index < Log.Entries.Count))
            return (int)index;

        //the cap removed the bottom entry: the window goes to the oldest lines, with the oldest line at the top
        BottomSequence = null;
        HiddenLines = 0;
        var lines = 0;

        for (var i = NextIndex(-1); i >= 0; i = NextIndex(i))
        {
            lines += LinesOf(i).Count;

            if (lines < LineCount)
                continue;

            BottomSequence = Log.FirstSequence + i;
            HiddenLines = lines - LineCount;
            FollowAtNewest();

            return BottomSequence is null ? PreviousIndex(Log.Entries.Count) : i;
        }

        //the tab has fewer lines than the window: the window shows all of them, and follows
        return PreviousIndex(Log.Entries.Count);
    }

    private int PreviousIndex(int index)
    {
        for (var i = Math.Min(index, Log.Entries.Count) - 1; i >= 0; i--)
            if (ShownTab.Shows(Log.Entries[i]))
                return i;

        return -1;
    }

    private int NextIndex(int index)
    {
        for (var i = index + 1; i < Log.Entries.Count; i++)
            if (ShownTab.Shows(Log.Entries[i]))
                return i;

        return -1;
    }

    private IReadOnlyList<string> LinesOf(int index)
    {
        var lines = Wrap(Log.FirstSequence + index, Log.Entries[index]);

        return lines.Count > 0 ? lines : [string.Empty];
    }
}
