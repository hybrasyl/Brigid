using System.Net;
using System.Net.Sockets;
using Brigid.Networking;
using Brigid.Networking.Definitions;
using Brigid.Screens;
using DALib.Networking.Packets.Server;
using Xunit;
using ConnectionEvent = Brigid.Screens.WorldScreen.ConnectionEvent;

namespace Brigid.Tests;

/// <summary>
///     BRIG-169: the exit confirm sends the next World-state redirect to Login. When the server ignores the confirm, the
///     grace time ends and the confirm is cleared, so a later world transfer stays in the world. BRIG-170: a redirect
///     whose connect to the next server fails is a lost connection on the world screen, not a silent stop.
/// </summary>
public sealed class ExitRedirectTests
{
    [Theory]
    [InlineData(ConnectionState.Lobby, false, ConnectionState.Login)]
    [InlineData(ConnectionState.Login, false, ConnectionState.World)]
    [InlineData(ConnectionState.World, false, ConnectionState.World)]
    [InlineData(ConnectionState.World, true, ConnectionState.Login)]
    public void TheRedirectTargetFollowsTheStateAndTheExitConfirm(
        ConnectionState state,
        bool exitConfirmed,
        ConnectionState expected)
        => Assert.Equal(expected, ConnectionManager.RedirectTargetFor(state, exitConfirmed));

    [Fact]
    public void AClearedExitConfirmSendsTheNextWorldRedirectBackIntoTheWorld()
    {
        using var manager = new ConnectionManager();

        //not in the world, so no packet goes out; the confirm is still recorded
        manager.RequestExit(false);

        Assert.Equal(ConnectionState.Login, manager.RedirectTargetFor(ConnectionState.World));

        manager.ClearExitConfirm();

        Assert.Equal(ConnectionState.World, manager.RedirectTargetFor(ConnectionState.World));
    }

    //the expected outcome is an object, because ConnectionOutcome is internal (BRIG-181)
    [Theory]
    [InlineData(ConnectionEvent.LoggedOut, false, false, WorldScreen.ConnectionOutcome.EndLogout)]
    [InlineData(ConnectionEvent.Closed, true, false, WorldScreen.ConnectionOutcome.None)]
    [InlineData(ConnectionEvent.Closed, true, true, WorldScreen.ConnectionOutcome.None)]
    [InlineData(ConnectionEvent.Closed, false, true, WorldScreen.ConnectionOutcome.EndLogout)]
    [InlineData(ConnectionEvent.Closed, false, false, WorldScreen.ConnectionOutcome.ConnectionLost)]
    [InlineData(ConnectionEvent.None, false, true, WorldScreen.ConnectionOutcome.None)]
    public void AConnectionEventHasTheOutcomeOfItsCase(
        object connectionEvent,
        bool redirectInProgress,
        bool awaitingLogout,
        object expected)
        => Assert.Equal(expected, WorldScreen.OutcomeFor((ConnectionEvent)connectionEvent, redirectInProgress, awaitingLogout));

    //BRIG-180: the server's close can come on the network thread before the game loop handles the Redirect. The state is
    //then Disconnected, and the redirect must take its target from the state that the connection was in
    [Theory]
    [InlineData(ConnectionState.World, false, ConnectionState.World)]
    [InlineData(ConnectionState.World, true, ConnectionState.Login)]
    [InlineData(ConnectionState.Login, false, ConnectionState.World)]
    public void ARedirectHandledAfterTheCloseKeepsTheTargetOfItsConnection(
        ConnectionState liveState,
        bool exitConfirmed,
        ConnectionState expected)
    {
        using var manager = new ConnectionManager();
        RedirectInfo? received = null;
        manager.OnRedirectReceived += info => received = info;

        if (exitConfirmed)
            manager.RequestExit(false);

        manager.SetStateForTests(liveState);
        manager.SetStateForTests(ConnectionState.Disconnected);

        manager.ProcessPackets([ClosedPortRedirect()]);

        Assert.Equal(expected, received?.TargetState);
    }

    //a redirect to a port with no listener, so the connect that follows it fails at once
    private static RedirectPacket ClosedPortRedirect()
    {
        using var unused = new TcpListener(IPAddress.Loopback, 0);
        unused.Start();

        return new RedirectPacket
        {
            IpAddress = IPAddress.Loopback,
            Port = (ushort)((IPEndPoint)unused.LocalEndpoint).Port,
            EncryptionSeed = 1,
            EncryptionKey = [1, 2, 3],
            Name = "brigid",
            RedirectId = 7
        };
    }

    //the expected event is an object, because ConnectionEvent is internal
    [Theory]
    //the logout redirect reached the login server
    [InlineData(ConnectionState.Connecting, ConnectionState.Login, true, ConnectionEvent.LoggedOut)]

    //a world transfer reached the next world server
    [InlineData(ConnectionState.Connecting, ConnectionState.World, true, ConnectionEvent.None)]

    //a redirect closes the old connection first: not a lost connection
    [InlineData(ConnectionState.World, ConnectionState.Disconnected, true, ConnectionEvent.None)]

    //the connect to the next server failed
    [InlineData(ConnectionState.Connecting, ConnectionState.Disconnected, true, ConnectionEvent.Closed)]

    //a drop with no redirect
    [InlineData(ConnectionState.World, ConnectionState.Disconnected, false, ConnectionEvent.Closed)]
    [InlineData(ConnectionState.Connecting, ConnectionState.Disconnected, false, ConnectionEvent.Closed)]

    //the redirect starts its connect to the next server
    [InlineData(ConnectionState.Disconnected, ConnectionState.Connecting, true, ConnectionEvent.None)]
    public void AStateChangeMeansTheEventOfItsKind(
        ConnectionState oldState,
        ConnectionState newState,
        bool redirectInProgress,
        object expected)
        => Assert.Equal(expected, WorldScreen.ConnectionEventFor(oldState, newState, redirectInProgress));
}
