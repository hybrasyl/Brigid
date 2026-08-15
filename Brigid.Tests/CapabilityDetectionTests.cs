#region
using System.Net;
using System.Net.Sockets;
using Brigid.Networking;
using DALib.Networking.Crypto;
using DALib.Networking.Packets.Server;
using DALib.Networking.Wire;
using Hybrasyl.Protocol.Negotiation;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     Lobby capability detection: Brigid reads the server-first <c>0x7E</c> greeting inline, before
///     the receive pump starts, and publishes any capability marker it carries. That placement is
///     what gives a later STARTTLS upgrade a seam where nothing else owns the stream and no peer byte
///     has been buffered past the frame.
/// </summary>
public class CapabilityDetectionTests
{
    private static readonly PacketCodec Codec = new();

    private const int TIMEOUT_MS = 5000;

    /// <summary>
    ///     A server that advertises capability and then never completes a TLS handshake fails the
    ///     connect rather than blocking forever. Neither SslStream nor the negotiator bounds itself,
    ///     so without the client's own deadline this hangs — the same failure the missing-greeting
    ///     timeout closes, one layer up.
    /// </summary>
    /// <remarks>
    ///     Detection of a marker on a well-behaved server is covered by
    ///     <c>TlsUpgradeTests.MarkedGreeting_UpgradesAndEngagesTheDialect</c>, which needs a real
    ///     TLS peer: after a marker is seen the upgrade is automatic, so there is no longer a
    ///     detected-but-not-upgraded state to observe against a plaintext server.
    /// </remarks>
    [Fact]
    public async Task Greeting_WithMarker_ButNoTlsBehindIt_FailsInsteadOfHanging()
    {
        var previous = GameClient.UpgradeTimeout;
        GameClient.UpgradeTimeout = TimeSpan.FromMilliseconds(250);

        try
        {
            using var listener = new LoopbackListener();
            using var client = new GameClient();

            client.ResetCrypto();

            var connect = client.ConnectAsync("127.0.0.1", listener.Port, true, "127.0.0.1");
            var accepted = await listener.AcceptAsync();

            await WriteGreetingAsync(accepted, CapabilityMarker.Current);

            //the server never answers the ClientHello.
            await Assert.ThrowsAnyAsync<Exception>(() => connect);

            Assert.NotNull(client.ServerCapability);
            Assert.Null(client.Negotiated);
        } finally
        {
            GameClient.UpgradeTimeout = previous;
        }
    }

    /// <summary>
    ///     A retail greeting carries no marker, so Brigid stays plaintext. Without this the test above
    ///     would also pass against a detector that published unconditionally.
    /// </summary>
    [Fact]
    public async Task Greeting_WithoutMarker_LeavesCapabilityUnset()
    {
        using var listener = new LoopbackListener();
        using var client = new GameClient();

        client.ResetCrypto();

        var connect = client.ConnectAsync("127.0.0.1", listener.Port, true, "127.0.0.1");
        var accepted = await listener.AcceptAsync();

        await WriteGreetingAsync(accepted, marker: null);
        await connect;

        Assert.Null(client.ServerCapability);
        Assert.IsType<AcceptConnectionPacket>(await DrainOneAsync(client));
    }

    /// <summary>
    ///     A server that sends more immediately after its greeting is one whose upgrade point we
    ///     cannot identify, so the marker is declined rather than acted on — and, critically, the
    ///     trailing packet is not lost: it is carried into the pump and still delivered.
    /// </summary>
    [Fact]
    public async Task Greeting_FollowedByMoreData_DeclinesUpgradeButKeepsBothPackets()
    {
        using var listener = new LoopbackListener();
        using var client = new GameClient();

        client.ResetCrypto();

        var connect = client.ConnectAsync("127.0.0.1", listener.Port, true, "127.0.0.1");
        var accepted = await listener.AcceptAsync();

        //both frames in a single write, so they land in one read alongside the greeting.
        var greeting = Codec.EncodeServer(
            new AcceptConnectionPacket { Message = BuildMarkedMessage(CapabilityMarker.Current) },
            new CryptoState());

        var trailer = Codec.EncodeServer(new RedirectPacket
        {
            IpAddress = IPAddress.Loopback,
            Port = 2611,
            EncryptionSeed = 1,
            EncryptionKey = [1, 2, 3],
            Name = "brigid",
            RedirectId = 7
        }, new CryptoState());

        var combined = new byte[greeting.Length + trailer.Length];
        greeting.Span.CopyTo(combined);
        trailer.Span.CopyTo(combined.AsSpan(greeting.Length));

        await accepted.WriteAsync(combined);
        await accepted.FlushAsync();
        await connect;

        Assert.Null(client.ServerCapability);

        Assert.IsType<AcceptConnectionPacket>(await DrainOneAsync(client));
        Assert.IsType<RedirectPacket>(await DrainOneAsync(client));
    }

    /// <summary>
    ///     A server that accepts and stays silent fails the connect with a legible error rather than
    ///     hanging. Fail-closed on purpose: assuming retail on expiry would let an attacker who cannot
    ///     strip the capability marker get the same plaintext outcome by merely delaying it. The
    ///     connection could not have progressed anyway — the lobby's first client packet is a reply to
    ///     the greeting.
    /// </summary>
    [Fact]
    public async Task SilentServer_FailsTheConnectInsteadOfHanging()
    {
        var previous = GameClient.GreetingTimeout;
        GameClient.GreetingTimeout = TimeSpan.FromMilliseconds(250);

        try
        {
            using var listener = new LoopbackListener();
            using var client = new GameClient();

            client.ResetCrypto();

            var connect = client.ConnectAsync("127.0.0.1", listener.Port, true, "127.0.0.1");

            _ = await listener.AcceptAsync();

            var error = await Assert.ThrowsAsync<TimeoutException>(() => connect);

            //the message must not read as "no marker" — that is the ordinary retail outcome, and is silent.
            Assert.Contains("no greeting", error.Message, StringComparison.OrdinalIgnoreCase);
        } finally
        {
            GameClient.GreetingTimeout = previous;
        }
    }

    /// <summary>A client-first hop reads no greeting and never claims a capability.</summary>
    [Fact]
    public async Task ClientFirstHop_ReadsNoGreetingAndLeavesCapabilityUnset()
    {
        using var listener = new LoopbackListener();
        using var client = new GameClient();

        client.ResetCrypto();
        await client.ConnectAsync("127.0.0.1", listener.Port);

        _ = await listener.AcceptAsync();

        Assert.Null(client.ServerCapability);
        Assert.True(client.Connected);
    }

    private static string BuildMarkedMessage(CapabilityMarker marker)
    {
        //Latin1 round-trips every byte, so the marker survives DALib's string-bodied greeting model.
        var body = marker.BuildGreetingBody();

        return System.Text.Encoding.Latin1.GetString(body.AsSpan(1));
    }

    private static async Task WriteGreetingAsync(Stream stream, CapabilityMarker? marker)
    {
        var greeting = marker is { } m
            ? new AcceptConnectionPacket { Message = BuildMarkedMessage(m) }
            : new AcceptConnectionPacket();

        await stream.WriteAsync(Codec.EncodeServer(greeting, new CryptoState()));
        await stream.FlushAsync();
    }

    private static async Task<IServerPacket> DrainOneAsync(GameClient client)
    {
        var buffer = new List<IServerPacket>();
        using var timeout = new CancellationTokenSource(TIMEOUT_MS);

        while (buffer.Count == 0)
        {
            timeout.Token.ThrowIfCancellationRequested();
            client.DrainPackets(buffer, 1);

            if (buffer.Count == 0)
                await Task.Delay(10, timeout.Token);
        }

        return buffer[0];
    }

    private sealed class LoopbackListener : IDisposable
    {
        private readonly TcpListener Listener;
        private TcpClient? Accepted;

        public LoopbackListener()
        {
            Listener = new TcpListener(IPAddress.Loopback, 0);
            Listener.Start();
        }

        public int Port => ((IPEndPoint)Listener.LocalEndpoint).Port;

        public async Task<Stream> AcceptAsync()
        {
            Accepted = await Listener.AcceptTcpClientAsync()
                                     .WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));

            return Accepted.GetStream();
        }

        public void Dispose()
        {
            Accepted?.Dispose();
            Listener.Dispose();
        }
    }
}
