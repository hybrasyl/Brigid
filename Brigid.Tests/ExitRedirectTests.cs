using Brigid.Networking;
using Brigid.Networking.Definitions;
using Brigid.Screens;
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
