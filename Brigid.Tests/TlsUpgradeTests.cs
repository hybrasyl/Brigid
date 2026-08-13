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
            TlsOptions = (host, _) => TlsConfig.ClientOptions(host, AcceptOnly(certificate), X509RevocationMode.NoCheck)
        };

        client.ResetCrypto();
        await client.ConnectAsync("localhost", server.Port, true).WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));

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
            TlsOptions = (host, _) => TlsConfig.ClientOptions(host, AcceptOnly(certificate), X509RevocationMode.NoCheck)
        };

        client.ResetCrypto();
        await client.ConnectAsync("localhost", server.Port, true).WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));

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
            TlsOptions = (host, _) => TlsConfig.ClientOptions(host, (_, _, _, _) => false, X509RevocationMode.NoCheck)
        };

        client.ResetCrypto();

        await Assert.ThrowsAnyAsync<Exception>(
            () => client.ConnectAsync("localhost", server.Port, true)
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
            TlsOptions = (host, _) => TlsConfig.ClientOptions(host, AcceptOnly(certificate), X509RevocationMode.NoCheck)
        };

        client.ResetCrypto();
        await client.ConnectAsync("localhost", lobby.Port, true).WaitAsync(TimeSpan.FromMilliseconds(TIMEOUT_MS));
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

        private readonly TcpListener Listener;
        private readonly TaskCompletionSource<DialectChoice> Choice = new();
        private readonly CancellationTokenSource Cts = new();

        public TlsLoopbackServer(X509Certificate2 certificate, CapabilityMarker? marker, bool greetFirst = true)
        {
            Listener = new TcpListener(IPAddress.Loopback, 0);
            Listener.Start();

            _ = RunAsync(certificate, marker, greetFirst);
        }

        public int Port => ((IPEndPoint)Listener.LocalEndpoint).Port;

        public Task<DialectChoice> ChoiceTask => Choice.Task;

        private async Task RunAsync(X509Certificate2 certificate, CapabilityMarker? marker, bool greetFirst)
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

                await using var tls = new SslStream(stream, false);
                await tls.AuthenticateAsServerAsync(TlsConfig.ServerOptions(certificate), Cts.Token);

                var result = await DialectNegotiator.NegotiateAsServerAsync(tls, Policy, Cts.Token);
                Choice.TrySetResult(result.Choice);

                //hold the connection open so the client's pumps see a live stream.
                await Task.Delay(Timeout.Infinite, Cts.Token);
            } catch (Exception ex)
            {
                Choice.TrySetException(ex);
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
