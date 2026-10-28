using Brigid.Data;
using Brigid.Screens;
using Brigid.Systems;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     BRIG-105: a server picked in Config from the main menu becomes the lobby server with no restart, and the server
///     caches follow it. Only a pick of an entry that is not the current server changes the server. The tests read the
///     lobby server and change no static state.
/// </summary>
public sealed class LobbyServerSwitchTests
{
    [Theory]
    [InlineData("qa.hybrasyl.com", 2610, "qa.hybrasyl.com", 2610, true)]
    [InlineData("QA.Hybrasyl.com", 2610, "qa.hybrasyl.com", 2610, true)]
    [InlineData(" qa.hybrasyl.com ", 2610, "qa.hybrasyl.com", 2610, true)]
    [InlineData("qa.hybrasyl.com", 2611, "qa.hybrasyl.com", 2610, false)]
    [InlineData("da0.kru.com", 2610, "qa.hybrasyl.com", 2610, false)]
    public void IsSameServer_ComparesTheHostWithNoCaseAndThePort(
        string host,
        int port,
        string otherHost,
        int otherPort,
        bool expected)
        => Assert.Equal(expected, GlobalSettings.IsSameServer(host, port, otherHost, otherPort));

    [Fact]
    public void OnlyAPickOfAnotherServerChangesTheServer()
    {
        var current = new ServerEntry { Host = GlobalSettings.LobbyHost, Port = GlobalSettings.LobbyPort };
        var other = new ServerEntry { Host = "brigid-test.example.org", Port = GlobalSettings.LobbyPort + 1 };

        //no list
        Assert.False(LauncherScreen.PicksNewServer(null, picked: true));

        //no pick: a client that DA_HOST started on a server that the list lacks keeps that server
        Assert.False(LauncherScreen.PicksNewServer(other, picked: false));

        //a pick of an entry that is not the server that the client uses now, also the entry that was selected when
        //Config opened
        Assert.True(LauncherScreen.PicksNewServer(other, picked: true));

        //a pick of the server that the client uses now
        Assert.False(LauncherScreen.PicksNewServer(current, picked: true));
    }

    [Fact]
    public void TheMapAndMetafileCachesShareTheServerScope()
    {
        Assert.Equal(Path.GetFileName(AppPaths.MetaFileDir), Path.GetFileName(AppPaths.MapsDir));
        Assert.Equal("maps", Path.GetFileName(Path.GetDirectoryName(AppPaths.MapsDir)));
    }
}
