#region
using System.Net;
using System.Net.Sockets;
using Brigid.Networking;
using DALib.Networking.Crypto;
using DALib.Networking.Packets.Client;
using DALib.Networking.Packets.Server;
using DALib.Networking.Wire;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     Loopback coverage for <see cref="GameClient" />'s transport: the receive pump reading the
///     connection's stream, and the send pump draining the outbound queue onto it. These exist
///     because the transport moved from raw <c>Socket</c> calls onto a <see cref="Stream" /> (so a
///     STARTTLS upgrade can swap an <c>SslStream</c> in), and nothing else in the suite executes
///     either path.
/// </summary>
/// <remarks>
///     DALib's codec is a fixture here, not the subject — the frames are encoded and decoded with
///     the same codec on purpose, so any failure is attributable to the plumbing between them
///     rather than to framing.
/// </remarks>
public class GameClientTransportTests
{
    private static readonly PacketCodec Codec = new();

    private const int TIMEOUT_MS = 5000;

    /// <summary>
    ///     A frame written to the accepted socket reaches the game loop as a typed packet — the
    ///     receive pump's stream read, framing, and inbound queue end to end.
    /// </summary>
    [Fact]
    public async Task ReceivePump_SurfacesAServerFrameWrittenToTheStream()
    {
        using var listener = new LoopbackListener();
        using var client = new GameClient();

        client.ResetCrypto();
        await client.ConnectAsync("127.0.0.1", listener.Port);

        var accepted = await listener.AcceptAsync();
        var greeting = Codec.EncodeServer(new AcceptConnectionPacket(), new CryptoState());

        await accepted.WriteAsync(greeting);
        await accepted.FlushAsync();

        var drained = await DrainOneAsync(client);

        var packet = Assert.IsType<AcceptConnectionPacket>(drained);
        Assert.Equal("CONNECTED SERVER", packet.Message);
    }

    /// <summary>
    ///     Sends leave the client in the order they were enqueued. Ordering is the invariant the
    ///     send path exists to hold: the codec allocates a crypto ordinal per encrypted frame under
    ///     the send lock, so frames reaching the wire out of order desynchronise the server's
    ///     decrypt.
    /// </summary>
    [Fact]
    public async Task SendPump_WritesFramesInEnqueueOrder()
    {
        const int COUNT = 25;

        using var listener = new LoopbackListener();
        using var client = new GameClient();

        client.ResetCrypto();
        await client.ConnectAsync("127.0.0.1", listener.Port);

        var accepted = await listener.AcceptAsync();

        for (var i = 0; i < COUNT; i++)
            client.Send(
                new ClientJoinPacket
                {
                    EncryptionSeed = 0,
                    EncryptionKey = [],
                    Name = "brigid",
                    RedirectId = (uint)i
                });

        var received = await ReadClientPacketsAsync(accepted, COUNT);

        Assert.Equal(
            Enumerable.Range(0, COUNT)
                      .Select(i => (uint)i),
            received.Cast<ClientJoinPacket>()
                    .Select(p => p.RedirectId));
    }

    /// <summary>
    ///     A send issued after disconnect is dropped rather than faulting on the disposed stream —
    ///     the race the send queue is completed to close.
    /// </summary>
    [Fact]
    public async Task Send_AfterDisconnect_IsDroppedWithoutThrowing()
    {
        using var listener = new LoopbackListener();
        using var client = new GameClient();

        client.ResetCrypto();
        await client.ConnectAsync("127.0.0.1", listener.Port);

        _ = await listener.AcceptAsync();

        client.Disconnect();

        client.Send(
            new ClientJoinPacket
            {
                EncryptionSeed = 0,
                EncryptionKey = [],
                Name = "brigid",
                RedirectId = 1
            });

        Assert.False(client.Connected);
    }

    private static async Task<IServerPacket> DrainOneAsync(GameClient client)
    {
        var buffer = new List<IServerPacket>();
        using var timeout = new CancellationTokenSource(TIMEOUT_MS);

        while (buffer.Count == 0)
        {
            timeout.Token.ThrowIfCancellationRequested();
            client.DrainPackets(buffer);

            if (buffer.Count == 0)
                await Task.Delay(10, timeout.Token);
        }

        return buffer[0];
    }

    private static async Task<List<IClientPacket>> ReadClientPacketsAsync(Stream stream, int count)
    {
        var packets = new List<IClientPacket>();
        var crypto = new CryptoState();
        var buffer = new byte[8192];
        var filled = 0;

        using var timeout = new CancellationTokenSource(TIMEOUT_MS);

        while (packets.Count < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled), timeout.Token);

            if (read == 0)
                break;

            filled += read;

            var offset = 0;

            while (Codec.TryGetClientPacket(
                       buffer.AsMemory(offset, filled - offset), crypto, out var packet, out var consumed))
            {
                packets.Add(packet!);
                offset += consumed;
            }

            if (offset > 0)
            {
                buffer.AsMemory(offset, filled - offset)
                      .CopyTo(buffer);
                filled -= offset;
            }
        }

        return packets;
    }

    /// <summary>A listener bound to an ephemeral loopback port, exposing the accepted stream.</summary>
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
