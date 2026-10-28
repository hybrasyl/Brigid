using Brigid.Screens;
using Brigid.Systems.Keybinds;
using Brigid.ViewModel;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     BRIG-111 item 7: two PR #60 fixes in the world messages. The Shift+F history panel stopped redrawing when its
///     1000-entry ring buffer was full, because it compared the count; it now redraws on the Chat event. The panel
///     needs game data, so the test pins what it relies on: the full buffer still raises one event for each message.
///     BRIG-28: the "map did not load" message names the refresh key that the player has.
/// </summary>
public sealed class WorldMessageTests
{
    [Fact]
    public void AFullOrangeBarHistoryStillRaisesAnEventForEachMessage()
    {
        var chat = new Chat();
        var added = 0;
        chat.OrangeBarMessageAdded += _ => added++;

        for (var i = 0; i < 1005; i++)
            chat.AddOrangeBarMessage($"message {i}");

        var history = chat.GetOrangeBarHistory();

        Assert.Equal(1005, added);
        Assert.Equal(1000, history.Count);
        Assert.Equal("message 1004", history[^1].Text);
    }

    //a rebound key shows: a message with "F5" written in it fails here
    [Fact]
    public void TheIncompleteMapMessageNamesTheRefreshKeyThatThePlayerHas()
        => Assert.Equal(
            "The map did not load completely. Press F6 to load it again.",
            WorldScreen.IncompleteMapMessage(new KeyChord(Keys.F6)));
}
