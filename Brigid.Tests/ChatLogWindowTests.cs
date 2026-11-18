#region
using Brigid.Controls.World.Hud.Main;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-145: the window of a chat tab over the one chat log. It shows the newest lines of its channels, keeps its
///     place while the player reads back, and wraps only the entries that it reads (BRIG-1). The wrapper here splits an
///     entry at each '|', so a test sets the lines of an entry in its text.
/// </summary>
public sealed class ChatLogWindowTests
{
    private static readonly HashSet<ChatChannel> SayOnly = [ChatChannel.Say];

    private readonly Chat Log = new();
    private readonly List<long> WrapCalls = [];

    private ChatLogWindow NewWindow(int lineCount, IReadOnlySet<ChatChannel>? channels = null)
        => new(Log, lineCount, Wrap) { Tab = new ChatTab("test", channels ?? SayOnly) };

    private IReadOnlyList<string> Wrap(long sequence, ChatEntry entry)
    {
        WrapCalls.Add(sequence);

        return entry.Text.Split('|');
    }

    private void Say(params string[] texts)
    {
        foreach (var text in texts)
            Log.Add(ChatChannel.Say, text, Color.White);
    }

    private static string[] Text(ChatLogWindow window) => window.VisibleLines().Select(line => line.Text).ToArray();

    [Fact]
    public void TheWindowShowsTheNewestLinesAtTheBottom()
    {
        Say("a", "b", "c", "d");

        Assert.Equal(["b", "c", "d"], Text(NewWindow(3)));
    }

    [Fact]
    public void AWindowMadeAfterTheMessagesShowsThem()
    {
        Say("a", "b");
        var window = NewWindow(5);

        Assert.Equal(["a", "b"], Text(window));
        Assert.True(window.IsAtNewest);
    }

    [Fact]
    public void TheWindowShowsOnlyItsChannelsAndNoEmptyEntry()
    {
        Say("a");
        Log.Add(ChatChannel.System, "notice", Color.Orange);
        Log.Add(ChatChannel.Whisper, "w", Color.Blue);
        Say("");
        Say("b");

        var window = NewWindow(5, new HashSet<ChatChannel> { ChatChannel.Say, ChatChannel.Whisper });

        Assert.Equal(["a", "w", "b"], Text(window));
        Assert.Equal(3, window.MatchCount);
        Assert.Equal(Color.Blue, window.VisibleLines()[1].Color);
    }

    [Fact]
    public void ScrollBackMovesOneLineAtATimeThroughAWrappedEntry()
    {
        Say("a", "b1|b2|b3", "c");
        var window = NewWindow(2);

        Assert.Equal(["b3", "c"], Text(window));

        Assert.True(window.ScrollBack(1));
        Assert.Equal(["b2", "b3"], Text(window));

        Assert.True(window.ScrollBack(1));
        Assert.Equal(["b1", "b2"], Text(window));

        Assert.True(window.ScrollBack(1));
        Assert.Equal(["a", "b1"], Text(window));

        //the top line is the oldest line
        Assert.False(window.ScrollBack(1));
        Assert.Equal(["a", "b1"], Text(window));
        Assert.False(window.IsAtNewest);
    }

    [Fact]
    public void ScrollForwardComesBackToTheNewestEntry()
    {
        Say("a", "b1|b2|b3", "c");
        var window = NewWindow(2);
        window.ScrollBack(3);

        Assert.True(window.ScrollForward(2));
        Assert.Equal(["b2", "b3"], Text(window));

        Assert.True(window.ScrollForward(1));
        Assert.Equal(["b3", "c"], Text(window));
        Assert.True(window.IsAtNewest);
        Assert.False(window.ScrollForward(1));
    }

    [Fact]
    public void AWindowThatShowsAllItsLinesDoesNotScroll()
    {
        Say("a", "b");
        var window = NewWindow(3);

        Assert.False(window.ScrollBack(1));
        Assert.True(window.IsAtNewest);
    }

    [Fact]
    public void TheWindowFollowsNewEntriesAtTheNewestAndKeepsItsPlaceWhenScrolledBack()
    {
        Say("a", "b", "c");
        var window = NewWindow(2);

        Say("d");
        Assert.Equal(["c", "d"], Text(window));

        window.ScrollBack(1);
        Assert.Equal(["b", "c"], Text(window));

        Say("e");
        Assert.Equal(["b", "c"], Text(window));
        Assert.Equal(2, window.EntriesBelow);
    }

    [Fact]
    public void APlaceThatTheCapRemovesMovesToTheOldestEntry()
    {
        for (var i = 0; i < 5; i++)
            Say($"m{i}");

        var window = NewWindow(2);
        window.ScrollBack(10);
        Assert.Equal(["m0", "m1"], Text(window));

        for (var i = 5; i < 1003; i++)
            Say($"m{i}");

        //m3 is the oldest entry now, at the top, and the window does not follow the new entries
        Assert.Equal("m3", Log.Entries[0].Text);
        Assert.Equal(["m3", "m4"], Text(window));
        Assert.False(window.ScrollBack(1));
        Assert.False(window.IsAtNewest);
        Assert.True(window.ScrollForward(1));
        Assert.Equal(["m4", "m5"], Text(window));
    }

    [Fact]
    public void ScrollToEntryPutsThatEntryAtTheBottom()
    {
        Say("a", "b", "c", "d");
        var window = NewWindow(2);

        window.ScrollToEntry(2);
        Assert.Equal(["a", "b"], Text(window));
        Assert.Equal(2, window.EntriesBelow);

        window.ScrollToEntry(0);
        Assert.True(window.IsAtNewest);
        Assert.Equal(0, window.EntriesBelow);
    }

    [Fact]
    public void AChangeOfTabGoesBackToTheNewestEntry()
    {
        Say("a", "b", "c");
        var window = NewWindow(1);
        window.ScrollBack(1);

        window.Tab = new ChatTab("other", new HashSet<ChatChannel> { ChatChannel.Say });

        Assert.True(window.IsAtNewest);
        Assert.Equal(["c"], Text(window));
    }

    [Fact]
    public void TheWindowWrapsOnlyTheEntriesThatItReads()
    {
        for (var i = 0; i < 1000; i++)
            Say($"m{i}");

        var window = NewWindow(6);
        _ = window.VisibleLines();
        window.ScrollBack(3);
        _ = window.VisibleLines();

        Assert.True(WrapCalls.Distinct().Count() <= 12, $"{WrapCalls.Distinct().Count()} entries wrapped");
    }

    [Fact]
    public void AClearedLogShowsNothing()
    {
        Say("a", "b");
        var window = NewWindow(1);
        Assert.True(window.ScrollBack(1));

        Log.Clear();

        Assert.Empty(window.VisibleLines());
        Assert.False(window.ScrollBack(1));
        Assert.Equal(0, window.MatchCount);
    }

    //a tab with few entries: when the cap removes the bottom entry and the oldest lines reach the newest one, the window
    //follows the new entries again
    [Fact]
    public void APlaceThatTheCapRemovesFollowsWhenTheTabHasFewLines()
    {
        Say("a", "b", "c");
        var window = NewWindow(2);
        window.ScrollBack(1);
        Assert.False(window.IsAtNewest);

        for (var i = 0; i < 1000; i++)
            Log.Add(ChatChannel.System, $"n{i}", Color.Orange);

        Say("d", "e");

        Assert.Equal(["d", "e"], Text(window));
        Assert.True(window.IsAtNewest);

        Say("f");
        Assert.Equal(["e", "f"], Text(window));
    }

    //the oldest line goes to the top, also when the oldest entry wraps to more lines than the window shows
    [Fact]
    public void APlaceThatTheCapRemovesPutsTheOldestLineAtTheTop()
    {
        Say("o", "p", "a1|a2|a3", "b");
        var window = NewWindow(2);
        window.ScrollBack(10);
        Assert.Equal(["o", "p"], Text(window));

        for (var i = 0; i < 998; i++)
            Log.Add(ChatChannel.System, $"n{i}", Color.Orange);

        Assert.Equal("a1|a2|a3", Log.Entries[0].Text);
        Assert.Equal(["a1", "a2"], Text(window));
    }

    [Fact]
    public void TheWindowGivesTheNumberOfEntriesThatItShows()
    {
        Say("a", "b1|b2", "c");

        Assert.Equal(2, NewWindow(3).Visible().Entries);
        Assert.Equal(3, NewWindow(4).Visible().Entries);
    }

    [Fact]
    public void AShorterWindowShowsFewerLines()
    {
        Say("a", "b", "c", "d");
        var window = NewWindow(3);

        window.LineCount = 2;

        Assert.Equal(["c", "d"], Text(window));
    }
}
