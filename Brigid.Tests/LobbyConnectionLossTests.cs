using Brigid.Networking.Definitions;
using Brigid.Screens;
using Xunit;

namespace Brigid.Tests;

// A server can send a Redirect and close in one read. The lobby heard the close on the network thread before the game
// loop handled the Redirect, so a login form that waited showed "connection lost" while the redirect went on to the
// world (BRIG-180). The lobby decides in Update now, after the game loop handled the Redirect.
public sealed class LobbyConnectionLossTests
{
    [Theory]
    //no redirect: a close is a lost connection
    [InlineData(false, ConnectionState.Disconnected, true)]
    //the Redirect came before the close, and its connect started
    [InlineData(true, ConnectionState.Connecting, false)]
    //the redirect's connect failed
    [InlineData(true, ConnectionState.Disconnected, true)]
    public void ACloseIsALostConnectionUnlessARedirectGoesOn(bool followingRedirect, ConnectionState state, bool expected)
        => Assert.Equal(expected, LobbyLoginScreen.IsLostConnection(followingRedirect, state));
}
