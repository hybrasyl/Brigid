using Brigid.Screens;
using Brigid.Systems.Keybinds;
using Brigid.ViewModel;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     BRIG-111 item 7: two PR #60 fixes in the world messages. J6: the Shift+F history redrew only when its count
///     changed, and the count of its 1000-entry ring buffer stops changing when the buffer is full, so the panel now
///     follows the event. BRIG-28: the "map did not load" message names the refresh key that the player has.
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

    [Fact]
    public void TheIncompleteMapMessageNamesTheRefreshKey()
    {
        var refreshKey = KeybindCatalog.Format(Keybinds.Effective(CommandId.World_Refresh).Primary);

        Assert.Equal(
            $"The map did not load completely. Press {refreshKey} to load it again.",
            WorldScreen.IncompleteMapMessage());
    }
}
