using Brigid.Data;
using Brigid.Networking.Definitions;
using Brigid.Screens;
using Brigid.Systems;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     BRIG-105: a server picked in Config from the main menu becomes the lobby server with no restart, and the server
///     caches follow it. A return with the same server keeps the lobby connection when the lobby was ready.
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

    [Theory]
    [InlineData(false, true, ConnectionState.Login, true)]
    [InlineData(true, true, ConnectionState.Login, false)]
    [InlineData(false, false, ConnectionState.Login, false)]
    [InlineData(false, true, ConnectionState.Disconnected, false)]
    [InlineData(false, true, ConnectionState.Lobby, false)]
    [InlineData(false, true, ConnectionState.Connecting, false)]
    public void TheLobbyConnectionStaysOnlyForTheSameServerAndAReadyLobby(
        bool serverChanged,
        bool lobbyWasReady,
        ConnectionState state,
        bool expected)
        => Assert.Equal(expected, LauncherScreen.KeepsLobbyConnection(serverChanged, lobbyWasReady, state));

    [Fact]
    public void TheReturnButtonReadsConnectOnlyForAnotherServer()
    {
        var current = new ServerEntry { Host = GlobalSettings.LobbyHost.ToUpperInvariant(), Port = GlobalSettings.LobbyPort };
        var otherPort = new ServerEntry { Host = GlobalSettings.LobbyHost, Port = GlobalSettings.LobbyPort + 1 };

        Assert.Equal("Return to Main Menu", LauncherScreen.ReturnLabel(null));
        Assert.Equal("Return to Main Menu", LauncherScreen.ReturnLabel(current));
        Assert.Equal("Connect", LauncherScreen.ReturnLabel(otherPort));
    }

    [Fact]
    public void SetLobbyServer_ChangesTheServerAndTheCacheScope_AndTheSameServerChangesNothing()
    {
        var previousHost = GlobalSettings.LobbyHost;
        var previousPort = GlobalSettings.LobbyPort;
        var previousScope = Path.GetFileName(AppPaths.MetaFileDir);

        //other tests read GlobalSettings.IsCursed (the retail chat cap), so the test host keeps the retail state of the
        //host that it replaces
        var host = ServerProfileKey.IsRetail(previousHost) ? "brigid-test.kru.com" : "brigid-test.example.org";

        try
        {
            Assert.True(GlobalSettings.SetLobbyServer(host, 2611));

            Assert.Equal(host, GlobalSettings.LobbyHost);
            Assert.Equal(2611, GlobalSettings.LobbyPort);

            var scope = ServerProfileKey.Resolve(host, null);
            Assert.Equal(scope, Path.GetFileName(AppPaths.MetaFileDir));
            Assert.Equal(scope, Path.GetFileName(AppPaths.MapsDir));
            Assert.Equal("maps", Path.GetFileName(Path.GetDirectoryName(AppPaths.MapsDir)));

            Assert.False(GlobalSettings.SetLobbyServer(host.ToUpperInvariant(), 2611));
            Assert.Equal(host, GlobalSettings.LobbyHost);
        } finally
        {
            GlobalSettings.LobbyHost = previousHost;
            GlobalSettings.LobbyPort = previousPort;
            AppPaths.SetServerScope(previousScope);
        }
    }
}
