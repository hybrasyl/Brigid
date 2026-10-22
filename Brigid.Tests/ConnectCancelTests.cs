#region
using System.Net;
using System.Net.Sockets;
using Brigid.Networking;
using Brigid.Networking.Definitions;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-107 phase 4: the lobby ends a wait that the player cancels, or that is too long, with Disconnect and a new
///     lobby connect. A connect can still be in progress then, for example the redirect to the world server. Disconnect
///     and a newer connect stop it, a newer connect waits for it to end, and a stopped connect does not change the state
///     or report an error.
/// </summary>
/// <remarks>
///     The loopback server accepts the connection and sends no greeting, so a lobby connect waits until it is stopped.
/// </remarks>
[Collection("GameClientTimeouts")]
public class ConnectCancelTests
{
    private const int TIMEOUT_MS = 5000;

    [Fact]
    public async Task Disconnect_StopsAConnectThatWaits()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new GameClient();
        client.ResetCrypto();

        var connect = client.ConnectAsync("127.0.0.1", Port(listener), true, "127.0.0.1");
        using var accepted = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));

        client.Disconnect();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect.WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS)));
        Assert.False(client.Connected);
    }

    [Fact]
    public async Task ANewerConnect_StopsTheConnectThatWaits_AndThenConnects()
    {
        using var silent = new TcpListener(IPAddress.Loopback, 0);
        using var next = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        next.Start();
        using var client = new GameClient();
        client.ResetCrypto();

        var first = client.ConnectAsync("127.0.0.1", Port(silent), true, "127.0.0.1");
        using var accepted = await silent.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));

        var second = client.ConnectAsync("127.0.0.1", Port(next));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS)));
        await second.WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));
        Assert.True(client.Connected);
    }

    [Fact]
    public async Task Disconnect_DuringALobbyConnect_SetsDisconnected_AndReportsNoError()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var manager = new ConnectionManager();
        var errors = 0;
        manager.OnError += _ => errors++;

        var connect = manager.ConnectToLobbyAsync("127.0.0.1", Port(listener), 0);
        using var accepted = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));
        Assert.Equal(ConnectionState.Connecting, manager.State);

        manager.Disconnect();
        await connect.WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));

        Assert.Equal(ConnectionState.Disconnected, manager.State);
        Assert.Equal(0, errors);
    }

    [Fact]
    public async Task AStoppedLobbyConnect_DoesNotChangeTheStateOfTheNewerOne()
    {
        using var silent = new TcpListener(IPAddress.Loopback, 0);
        using var next = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        next.Start();
        using var manager = new ConnectionManager();
        var errors = 0;
        manager.OnError += _ => errors++;

        var first = manager.ConnectToLobbyAsync("127.0.0.1", Port(silent), 0);
        using var firstAccepted = await silent.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));

        var second = manager.ConnectToLobbyAsync("127.0.0.1", Port(next), 0);
        await first.WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));
        using var secondAccepted = await next.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));

        //the second connect still waits for its greeting, and the first one did not end it
        Assert.Equal(ConnectionState.Connecting, manager.State);
        Assert.Equal(0, errors);

        manager.Disconnect();
        await second.WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));
    }

    private static int Port(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;
}
