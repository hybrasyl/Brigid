using Brigid.Data;
using Brigid.Screens;
using Brigid.Systems;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     BRIG-105: a server picked in Config from the main menu becomes the lobby server with no restart, and the server
///     caches follow it. Only a pick of another entry changes the server. The tests read the lobby server and change no
///     static state.
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
    public void SetLobbyServer_WithTheSameServerChangesNothing()
    {
        var host = GlobalSettings.LobbyHost;

        Assert.False(GlobalSettings.SetLobbyServer($" {host.ToUpperInvariant()} ", GlobalSettings.LobbyPort));
        Assert.Equal(host, GlobalSettings.LobbyHost);
    }

    [Fact]
    public void OnlyAPickOfAnotherEntryChangesTheServer()
    {
        var current = new ServerEntry { Host = GlobalSettings.LobbyHost, Port = GlobalSettings.LobbyPort };
        var other = new ServerEntry { Host = "brigid-test.example.org", Port = GlobalSettings.LobbyPort + 1 };

        //no list, or the entry that was selected when Config opened
        Assert.False(LauncherScreen.PicksNewServer(null, current.Key));
        Assert.False(LauncherScreen.PicksNewServer(other, other.Key));

        //DA_HOST started the client on a server that the list lacks: a return with the saved pick keeps that server
        Assert.False(LauncherScreen.PicksNewServer(other, other.Key.ToUpperInvariant()));

        //another entry, which is not the server that the client uses now
        Assert.True(LauncherScreen.PicksNewServer(other, current.Key));

        //another entry that names the server that the client uses now
        Assert.False(LauncherScreen.PicksNewServer(current, other.Key));
    }

    [Fact]
    public void TheMapAndMetafileCachesShareTheServerScope()
    {
        Assert.Equal(Path.GetFileName(AppPaths.MetaFileDir), Path.GetFileName(AppPaths.MapsDir));
        Assert.Equal("maps", Path.GetFileName(Path.GetDirectoryName(AppPaths.MapsDir)));
    }
}
