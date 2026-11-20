using System.Net;
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
///     whose connect to the next server fails is a lost connection on the world screen, not a silent stop. BRIG-180: a
///     redirect that the game loop handles after the server's close goes where its own connection sent it. BRIG-181:
///     what the world screen does with each connection event.
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

    //the game loop set Connecting for a redirect, and the close of the old connection came after it: the hop goes on
    [Fact]
    public void ACloseDuringTheConnectOfARedirectKeepsConnecting()
    {
        using var manager = new ConnectionManager();
        manager.SetStateForTests(ConnectionState.Connecting);

        manager.HandleDisconnected(manager.Client.CurrentConnection);

        Assert.Equal(ConnectionState.Connecting, manager.State);
    }

    //the server close of the old connection can raise its event after a redirect's new connection reached the world; a
    //Disconnected then stopped the world actions on a live socket. A close of a connection that is not the current one
    //changes nothing, and a close of the current one still goes to Disconnected
    [Fact]
    public void ALateCloseOfAReplacedConnectionLeavesTheStateAsItIs()
    {
        using var manager = new ConnectionManager();
        manager.SetStateForTests(ConnectionState.World);

        manager.HandleDisconnected(manager.Client.CurrentConnection - 1);

        Assert.Equal(ConnectionState.World, manager.State);

        manager.HandleDisconnected(manager.Client.CurrentConnection);

        Assert.Equal(ConnectionState.Disconnected, manager.State);
    }

    //the expected outcome is an object, because ConnectionOutcome is internal (BRIG-181)
    [Theory]
    [InlineData(ConnectionEvent.LoggedOut, false, false, false, WorldScreen.ConnectionOutcome.EndLogout)]
    [InlineData(ConnectionEvent.LoggedOut, false, false, true, WorldScreen.ConnectionOutcome.EndLogout)]
    [InlineData(ConnectionEvent.Closed, true, false, false, WorldScreen.ConnectionOutcome.None)]
    [InlineData(ConnectionEvent.Closed, true, true, false, WorldScreen.ConnectionOutcome.None)]
    [InlineData(ConnectionEvent.Closed, true, false, true, WorldScreen.ConnectionOutcome.None)]
    [InlineData(ConnectionEvent.Closed, false, true, false, WorldScreen.ConnectionOutcome.EndLogout)]
    [InlineData(ConnectionEvent.Closed, false, false, false, WorldScreen.ConnectionOutcome.ConnectionLost)]

    //a Redirect and a close came between the drain and the update: the Redirect still waits in the queue
    [InlineData(ConnectionEvent.Closed, false, false, true, WorldScreen.ConnectionOutcome.DecideNextFrame)]
    [InlineData(ConnectionEvent.Closed, false, true, true, WorldScreen.ConnectionOutcome.DecideNextFrame)]
    [InlineData(ConnectionEvent.None, false, true, false, WorldScreen.ConnectionOutcome.None)]
    public void AConnectionEventHasTheOutcomeOfItsCase(
        object connectionEvent,
        bool redirectInProgress,
        bool awaitingLogout,
        bool packetsWait,
        object expected)
        => Assert.Equal(
            expected,
            WorldScreen.ConnectionOutcomeFor((ConnectionEvent)connectionEvent, redirectInProgress, awaitingLogout, packetsWait));

    //a close that waits for the packets before it goes back for the next frame; without that, a real close that came with
    //packets in the queue never showed "Connection lost"
    [Fact]
    public void ACloseThatWaitsForPacketsIsDecidedAtTheNextFrame()
    {
        var pending = (int)ConnectionEvent.Closed;

        Assert.Equal(WorldScreen.ConnectionOutcome.DecideNextFrame, WorldScreen.TakeConnectionOutcome(ref pending, false, false, true));
        Assert.Equal((int)ConnectionEvent.Closed, pending);

        Assert.Equal(WorldScreen.ConnectionOutcome.ConnectionLost, WorldScreen.TakeConnectionOutcome(ref pending, false, false, false));
        Assert.Equal((int)ConnectionEvent.None, pending);
    }

    //a close that waits for its packets can hold the logout Redirect; a grace time that ended in that frame cleared the
    //exit confirm, and the logout went back to the world
    [Theory]
    [InlineData(false, WorldScreen.ConnectionOutcome.None, false)]
    [InlineData(true, WorldScreen.ConnectionOutcome.None, true)]
    [InlineData(false, WorldScreen.ConnectionOutcome.DecideNextFrame, true)]
    [InlineData(false, WorldScreen.ConnectionOutcome.ConnectionLost, false)]
    public void TheExitGraceTimeWaitsForARedirectAndForAClosesPackets(bool redirectInProgress, object outcome, bool waits)
        => Assert.Equal(waits, WorldScreen.ExitGraceWaits(redirectInProgress, (WorldScreen.ConnectionOutcome)outcome));

    //the network thread can record a logout between the take and the put back; the logout stays
    [Fact]
    public void ALogoutRecordedSinceTheTakeStays()
    {
        var pending = (int)ConnectionEvent.LoggedOut;

        Assert.Equal(WorldScreen.ConnectionOutcome.EndLogout, WorldScreen.TakeConnectionOutcome(ref pending, false, false, true));
        Assert.Equal((int)ConnectionEvent.None, pending);
    }

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

        //the handler alone: ProcessPackets would also start the connect to the next server
        manager.HandlePacket(
            new RedirectPacket
            {
                IpAddress = IPAddress.Loopback,
                Port = 2611,
                EncryptionSeed = 1,
                EncryptionKey = [1, 2, 3],
                Name = "brigid",
                RedirectId = 7
            });

        Assert.Equal(expected, received?.TargetState);
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
