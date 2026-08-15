#region
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Brigid.Networking;
using DALib.Networking.Crypto;
using DALib.Networking.Packets.Server;
using DALib.Networking.Wire;
using Hybrasyl.Protocol;
using Hybrasyl.Protocol.Negotiation;
using Hybrasyl.Protocol.Packets;
using Hybrasyl.Protocol.Wire;
using Hybrasyl.Protocol.Transport;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     End-to-end STARTTLS coverage against a real <c>SslStream</c> server on loopback: the marked
///     greeting, the TLS 1.3 handshake, and the dialect negotiation inside it. Uses a self-signed
///     certificate with an accept-this-one validator, which is also the shape the trust-on-first-use
///     flow will supply.
/// </summary>
public class TlsUpgradeTests
{
    private static readonly PacketCodec Codec = new();

    private const int TIMEOUT_MS = 10000;

    /// <summary>
    ///     A marked greeting drives the upgrade: TLS comes up and the dialect is engaged, with the
    ///     negotiation itself carried inside the encrypted channel.
    /// </summary>
    [Fact]
    public async Task MarkedGreeting_UpgradesAndEngagesTheDialect()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var server = new TlsLoopbackServer(certificate, CapabilityMarker.Current);
        using var client = new GameClient
        {
            TlsOptions = host => TlsConfig.ClientOptions(host, AcceptOnly(certificate), X509RevocationMode.NoCheck)
        };

        client.ResetCrypto();
        await client.ConnectAsync("localhost", server.Port, true, "localhost").WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));

        Assert.NotNull(client.ServerCapability);
        Assert.True(client.UpgradeToTls);

        var negotiated = client.Negotiated;
        Assert.NotNull(negotiated);
        Assert.Equal(ConnectionMode.DialectOverTls, negotiated!.Value.Mode);
        Assert.Equal(Dialect.V1, negotiated.Value.Dialect);

        //the greeting must still drive ConnectionManager's handler, which sends the client's first packet.
        Assert.IsType<AcceptConnectionPacket>(await DrainOneAsync(client));

        //the server side must have seen the same resolution, derived rather than signalled.
        var choice = await server.ChoiceTask.WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));
        Assert.Equal(Dialect.V1, choice.Chosen);
    }

    /// <summary>
    ///     An unmarked greeting leaves the connection plaintext. Without this the test above would
    ///     also pass against a client that upgraded unconditionally.
    /// </summary>
    [Fact]
    public async Task UnmarkedGreeting_StaysPlaintextAndNeverNegotiates()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var server = new TlsLoopbackServer(certificate, marker: null);
        using var client = new GameClient
        {
            TlsOptions = host => TlsConfig.ClientOptions(host, AcceptOnly(certificate), X509RevocationMode.NoCheck)
        };

        client.ResetCrypto();
        await client.ConnectAsync("localhost", server.Port, true, "localhost").WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));

        Assert.Null(client.ServerCapability);
        Assert.False(client.UpgradeToTls);
        Assert.Null(client.Negotiated);
    }

    /// <summary>
    ///     A server whose certificate the client rejects fails the connect rather than silently
    ///     continuing in plaintext — falling back would hand a network attacker the downgrade the
    ///     upgrade exists to prevent.
    /// </summary>
    [Fact]
    public async Task RejectedCertificate_FailsTheConnectRatherThanFallingBack()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var server = new TlsLoopbackServer(certificate, CapabilityMarker.Current);
        using var client = new GameClient
        {
            TlsOptions = host => TlsConfig.ClientOptions(host, (_, _, _, _) => false, X509RevocationMode.NoCheck)
        };

        client.ResetCrypto();

        await Assert.ThrowsAnyAsync<Exception>(
            () => client.ConnectAsync("localhost", server.Port, true, "localhost")
                        .WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS)));

        Assert.Null(client.Negotiated);
    }

    /// <summary>
    ///     Capability learned at the lobby survives disconnect, so a client-first login/world hop
    ///     upgrades before it speaks. Resetting it per connection would make every hop after the
    ///     lobby plaintext — which is precisely where the credentials travel.
    /// </summary>
    [Fact]
    public async Task CapabilitySurvivesDisconnect_SoClientFirstHopsUpgrade()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var lobby = new TlsLoopbackServer(certificate, CapabilityMarker.Current);
        using var client = new GameClient
        {
            TlsOptions = host => TlsConfig.ClientOptions(host, AcceptOnly(certificate), X509RevocationMode.NoCheck)
        };

        client.ResetCrypto();
        await client.ConnectAsync("localhost", lobby.Port, true, "localhost").WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));
        Assert.True(client.UpgradeToTls);

        client.Disconnect();
        Assert.True(client.UpgradeToTls);

        //a client-first hop: no greeting, and the client opens with a ClientHello.
        using var world = new TlsLoopbackServer(certificate, marker: null, greetFirst: false);

        await client.ConnectAsync("localhost", world.Port).WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));

        var negotiated = client.Negotiated;
        Assert.NotNull(negotiated);
        Assert.Equal(ConnectionMode.DialectOverTls, negotiated!.Value.Mode);
    }

    /// <summary>
    ///     The greeting is not visible to the game loop until the connection can answer it. It is read
    ///     inline, before the upgrade, and the loop drains on its own thread — so a handshake that
    ///     outlasts a frame would otherwise let the greeting's handler run while no send pump exists,
    ///     and its reply is discarded. The server says nothing further until the client speaks, so the
    ///     session stalls with both ends healthy and waiting.
    /// </summary>
    [Fact]
    public async Task Greeting_IsNotDispatchableUntilTheConnectionCanReply()
    {
        using var certificate = CreateSelfSignedCertificate();

        using var server = new TlsLoopbackServer(
            certificate,
            CapabilityMarker.Current,
            handshakeDelay: TimeSpan.FromMilliseconds(400));

        using var client = new GameClient
        {
            TlsOptions = host => TlsConfig.ClientOptions(host, AcceptOnly(certificate), X509RevocationMode.NoCheck)
        };

        client.ResetCrypto();

        var connect = client.ConnectAsync("localhost", server.Port, true, "localhost");
        var drained = new List<IServerPacket>();

        //poll the way the game loop does while the upgrade is still in flight.
        while (!connect.IsCompleted)
        {
            client.DrainPackets(drained);

            Assert.Empty(drained);
            Assert.False(client.Connected);

            await Task.Delay(10);
        }

        await connect.WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));

        //and it is not lost either — it arrives once the pump that carries the reply is up.
        Assert.IsType<AcceptConnectionPacket>(await DrainOneAsync(client));
        Assert.True(client.Connected);
    }

    /// <summary>
    ///     The seam the redirect scheme rests on: what reaches the TLS options factory is the server
    ///     identity, not the address that was dialled. Without the split, a hop redirected to by address
    ///     puts an IP literal to the certificate check and to the pin lookup, and neither can match.
    /// </summary>
    [Fact]
    public async Task Upgrade_ValidatesAgainstTheIdentityRatherThanTheDialledAddress()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var server = new TlsLoopbackServer(certificate, CapabilityMarker.Current);

        var seen = new List<string>();

        using var client = new GameClient
        {
            TlsOptions = identity =>
            {
                seen.Add(identity);

                return TlsConfig.ClientOptions(identity, AcceptOnly(certificate), X509RevocationMode.NoCheck);
            }
        };

        client.ResetCrypto();

        //dialled by address, authenticated as a name — the shape every redirect hop takes.
        await client.ConnectAsync("127.0.0.1", server.Port, true, "localhost")
                    .WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));

        Assert.Equal(["localhost"], seen);
        Assert.NotNull(client.Negotiated);
    }

    /// <summary>
    ///     Omitting the identity leaves the dialled host doing both jobs, which is what the lobby hop
    ///     wants: it is reached by the name the user configured.
    /// </summary>
    [Fact]
    public async Task Upgrade_WithoutAnIdentity_ValidatesAgainstTheDialledHost()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var server = new TlsLoopbackServer(certificate, CapabilityMarker.Current);

        var seen = new List<string>();

        using var client = new GameClient
        {
            TlsOptions = identity =>
            {
                seen.Add(identity);

                return TlsConfig.ClientOptions(identity, AcceptOnly(certificate), X509RevocationMode.NoCheck);
            }
        };

        client.ResetCrypto();
        await client.ConnectAsync("localhost", server.Port, true, "localhost").WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));

        Assert.Equal(["localhost"], seen);
    }

    /// <summary>
    ///     A whole extension frame out and back: encoded on the negotiated dialect, carried inside TLS
    ///     alongside retail framing, decoded by the peer, and routed home by byte 0 rather than by any
    ///     connection state. The token proves the round trip is this probe's reply and not merely
    ///     traffic — a client that echoed its own send, or replied to the wrong probe, fails here.
    /// </summary>
    [Fact]
    public async Task ExtensionFrame_RoundTripsOnTheNegotiatedDialect()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var server = new TlsLoopbackServer(certificate, CapabilityMarker.Current, echo: true);
        using var client = new GameClient
        {
            TlsOptions = host => TlsConfig.ClientOptions(host, AcceptOnly(certificate), X509RevocationMode.NoCheck)
        };

        client.ResetCrypto();
        await client.ConnectAsync("localhost", server.Port, true, "localhost").WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));

        Assert.True(client.DialectEngaged);

        const ulong TOKEN = 0x0123_4567_89AB_CDEFUL;
        client.SendExtension(new ClientEcho(TOKEN));

        var reply = await DrainOneExtensionAsync(client);

        Assert.Equal(TOKEN, Assert.IsType<ClientEcho>(reply).Token);
    }

    /// <summary>
    ///     No dialect, no extension frame. The send is refused rather than encoded against a default
    ///     dialect, which would put a frame on the wire that the peer's router hands to a codec it never
    ///     built — a decode error on a live connection.
    /// </summary>
    [Fact]
    public async Task ExtensionSend_IsRefusedWhenNoDialectIsEngaged()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var server = new TlsLoopbackServer(certificate, marker: null);
        using var client = new GameClient
        {
            TlsOptions = host => TlsConfig.ClientOptions(host, AcceptOnly(certificate), X509RevocationMode.NoCheck)
        };

        client.ResetCrypto();
        await client.ConnectAsync("localhost", server.Port, true, "localhost").WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));

        Assert.False(client.DialectEngaged);

        //must not throw, and must not reach the wire.
        client.SendExtension(new ClientEcho(1));

        var drained = new List<IExtensionServerPacket>();
        client.DrainExtensionPackets(drained);

        Assert.Empty(drained);
    }

    private static async Task<IExtensionServerPacket> DrainOneExtensionAsync(GameClient client)
    {
        var buffer = new List<IExtensionServerPacket>();
        using var timeout = new CancellationTokenSource(TIMEOUT_MS);

        while (buffer.Count == 0)
        {
            timeout.Token.ThrowIfCancellationRequested();
            client.DrainExtensionPackets(buffer);

            if (buffer.Count == 0)
                await Task.Delay(10, timeout.Token);
        }

        return buffer[0];
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

    private static RemoteCertificateValidationCallback AcceptOnly(X509Certificate2 expected)
        => (_, presented, _, _) => presented is not null && presented.GetCertHashString() == expected.GetCertHashString();

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var subjectAlternativeName = new SubjectAlternativeNameBuilder();
        subjectAlternativeName.AddDnsName("localhost");
        request.CertificateExtensions.Add(subjectAlternativeName.Build());

        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        //SChannel rejects the ephemeral key CreateSelfSigned produces, so round-trip through PKCS#12.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }

    /// <summary>
    ///     A minimal server half: optionally greets, accepts the TLS handshake, and runs the server
    ///     side of the dialect negotiation.
    /// </summary>
    private sealed class TlsLoopbackServer : IDisposable
    {
        private static readonly ServerDialectPolicy Policy = ServerDialectPolicy.Create(Dialect.V1, Dialect.V1);
        private static readonly ExtensionCodec ExtensionCodec = new();

        private readonly TcpListener Listener;
        private readonly TaskCompletionSource<DialectChoice> Choice = new();
        private readonly CancellationTokenSource Cts = new();

        public TlsLoopbackServer(
            X509Certificate2 certificate,
            CapabilityMarker? marker,
            bool greetFirst = true,
            TimeSpan handshakeDelay = default,
            bool echo = false)
        {
            Listener = new TcpListener(IPAddress.Loopback, 0);
            Listener.Start();

            _ = RunAsync(certificate, marker, greetFirst, handshakeDelay, echo);
        }

        public int Port => ((IPEndPoint)Listener.LocalEndpoint).Port;

        public Task<DialectChoice> ChoiceTask => Choice.Task;

        private async Task RunAsync(
            X509Certificate2 certificate,
            CapabilityMarker? marker,
            bool greetFirst,
            TimeSpan handshakeDelay,
            bool echo)
        {
            try
            {
                using var accepted = await Listener.AcceptTcpClientAsync(Cts.Token);
                var stream = accepted.GetStream();

                if (greetFirst)
                {
                    var greeting = marker is { } m
                        ? new AcceptConnectionPacket
                        {
                            Message = System.Text.Encoding.Latin1.GetString(m.BuildGreetingBody()
                                                                             .AsSpan(1))
                        }
                        : new AcceptConnectionPacket();

                    await stream.WriteAsync(Codec.EncodeServer(greeting, new CryptoState()), Cts.Token);
                    await stream.FlushAsync(Cts.Token);
                }

                if (marker is null && greetFirst)
                    return; //plaintext client; nothing further to do.

                //widens the window between the greeting and a completed upgrade, which on loopback is
                //otherwise shorter than a frame — the reason a real network found this and tests did not.
                if (handshakeDelay > TimeSpan.Zero)
                    await Task.Delay(handshakeDelay, Cts.Token);

                await using var tls = new SslStream(stream, false);
                await tls.AuthenticateAsServerAsync(TlsConfig.ServerOptions(certificate), Cts.Token);

                var result = await DialectNegotiator.NegotiateAsServerAsync(tls, Policy, Cts.Token);
                Choice.TrySetResult(result.Choice);

                if (echo)
                {
                    await EchoLoopAsync(tls, result.Resolution);

                    return;
                }

                //hold the connection open so the client's pumps see a live stream.
                await Task.Delay(Timeout.Infinite, Cts.Token);
            } catch (Exception ex)
            {
                Choice.TrySetException(ex);
            }
        }

        /// <summary>
        ///     Mirrors the real server's ClientEcho handler: decode the probe, return the token verbatim
        ///     at the same opcode. Reading a whole extension frame is the point — a reply built without
        ///     decoding would pass a client that never encoded correctly.
        /// </summary>
        private async Task EchoLoopAsync(SslStream tls, DialectResolution resolution)
        {
            var codec = ExtensionCodec.ForConnection(resolution);
            var buffer = new byte[4096];
            var filled = 0;

            while (!Cts.IsCancellationRequested)
            {
                var read = await tls.ReadAsync(buffer.AsMemory(filled), Cts.Token);

                if (read == 0)
                    return;

                filled += read;

                while (codec.TryDecodeClient(buffer.AsMemory(0, filled), out var packet, out var consumed))
                {
                    buffer.AsSpan(consumed, filled - consumed)
                          .CopyTo(buffer);

                    filled -= consumed;

                    if (packet is ClientEcho probe)
                    {
                        await tls.WriteAsync(codec.EncodeServer(new ClientEcho(probe.Token)), Cts.Token);
                        await tls.FlushAsync(Cts.Token);
                    }
                }
            }
        }

        public void Dispose()
        {
            Cts.Cancel();
            Listener.Dispose();
            Cts.Dispose();
        }
    }
}
