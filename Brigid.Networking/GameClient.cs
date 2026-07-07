#region
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DALib.Networking.Crypto;
using DALib.Networking.Packets.Server;
using DALib.Networking.Wire;
#endregion

namespace Brigid.Networking;

/// <summary>
///     Client-side networking implementation for the Dark Ages (DOOMVAS v1) protocol. Handles the TCP connection and
///     hands framing, encryption, and (de)serialization to DALib's <see cref="PacketCodec" />. Inbound packets surface
///     as typed <see cref="IServerPacket" /> values via <see cref="DrainPackets" />; outbound packets are sent as typed
///     <see cref="IClientPacket" /> values via <see cref="Send" />.
/// </summary>
public sealed class GameClient : IDisposable
{
    private const int RECEIVE_BUFFER_SIZE = ushort.MaxValue * 8;
    private const int INITIAL_SEND_ARGS_COUNT = 5;

    //the codec is stateless and shared across every connection; only CryptoState is per-connection.
    private static readonly PacketCodec Codec = new();

    private readonly ConcurrentQueue<IServerPacket> InboundQueue = new();
    private readonly ConcurrentQueue<SocketAsyncEventArgs> SendArgsPool = new();

    private readonly Lock SendLock = new();
    private int ConnectionGeneration;
    private bool Disposed;
    private volatile bool IsAlive;
    private int ReceiveCount;
    private CancellationTokenSource? ReceiveCts;
    private IMemoryOwner<byte>? ReceiveMemoryOwner;
    private Task? ReceiveTask;

    private Socket? Socket;

    /// <summary>
    ///     This connection's crypto state (seed, key, key table, and ordinal counters). Replaced wholesale at each
    ///     handshake via <see cref="ResetCrypto" /> / <see cref="ApplyCryptoKey" />.
    /// </summary>
    public CryptoState Crypto { get; private set; } = new();

    /// <summary>
    ///     Whether the client is currently connected to a server.
    /// </summary>
    public bool Connected => IsAlive && (Socket?.Connected ?? false);

    /// <summary>
    ///     The remote endpoint of the active socket, or null when not connected.
    /// </summary>
    public IPEndPoint? RemoteEndPoint => Connected ? Socket?.RemoteEndPoint as IPEndPoint : null;

    /// <summary>
    ///     Queries the OS kernel's smoothed round-trip-time estimate for the gameplay socket. Uses Windows
    ///     <c>SIO_TCP_INFO</c> (TCP_INFO_v0), Linux <c>getsockopt(IPPROTO_TCP, TCP_INFO)</c>, or macOS
    ///     <c>getsockopt(IPPROTO_TCP, TCP_CONNECTION_INFO)</c> depending on platform. Returns false when not connected,
    ///     on unsupported platforms, or when the underlying call fails (kernel too old, transient socket state, etc.).
    ///     Caller treats false as "no measurement".
    /// </summary>
    public bool TryGetTcpSmoothedRttMs(out long rttMs)
    {
        rttMs = 0;

        var socket = Socket;

        if (socket is null || !Connected)
            return false;

        if (OperatingSystem.IsWindows())
            return TryGetWindowsTcpRttMs(socket, out rttMs);

        if (OperatingSystem.IsLinux())
            return TryGetLinuxTcpRttMs(socket, out rttMs);

        if (OperatingSystem.IsMacOS())
            return TryGetMacTcpRttMs(socket, out rttMs);

        return false;
    }

    private static bool TryGetWindowsTcpRttMs(Socket socket, out long rttMs)
    {
        rttMs = 0;

        //SIO_TCP_INFO = _WSAIORW(IOC_VENDOR, 39) = IOC_INOUT|IOC_VENDOR|39 = 0xD8000027.
        //Input  : uint32 version (0 selects TCP_INFO_v0, supported since Win10 1703).
        //Output : TCP_INFO_v0 struct, 104 bytes. RttUs (uint32) sits at offset 20,
        //         after State(4) + Mss(4) + ConnectionTimeMs(8) + TimestampsEnabled(1) + 3 bytes alignment padding.
        const int SIO_TCP_INFO = unchecked((int)0xD8000027U);
        const int RTT_US_OFFSET = 20;
        const int TCP_INFO_V0_SIZE = 104;

        var inBuf = new byte[sizeof(uint)];
        var outBuf = new byte[TCP_INFO_V0_SIZE];

        try
        {
            var bytesWritten = socket.IOControl(SIO_TCP_INFO, inBuf, outBuf);

            if (bytesWritten < RTT_US_OFFSET + sizeof(uint))
                return false;

            var rttUs = BinaryPrimitives.ReadUInt32LittleEndian(outBuf.AsSpan(RTT_US_OFFSET, sizeof(uint)));
            rttMs = rttUs / 1000;

            return true;
        } catch (SocketException)
        {
            return false;
        } catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static bool TryGetLinuxTcpRttMs(Socket socket, out long rttMs)
    {
        rttMs = 0;

        //getsockopt(IPPROTO_TCP, TCP_INFO) returns the kernel's `struct tcp_info`. The struct is append-only
        //across kernel versions, so reading the first 72 bytes is safe everywhere since Linux 2.6.
        //tcpi_rtt (uint32, microseconds) sits at offset 68 — past the fixed-size byte prefix and 16 leading uint32s.
        const int IPPROTO_TCP = 6;
        const int TCP_INFO = 11;
        const int RTT_US_OFFSET = 68;
        const int READ_SIZE = 72;

        Span<byte> buf = stackalloc byte[READ_SIZE];

        try
        {
            var bytesRead = socket.GetRawSocketOption(IPPROTO_TCP, TCP_INFO, buf);

            if (bytesRead < RTT_US_OFFSET + sizeof(uint))
                return false;

            var rttUs = BinaryPrimitives.ReadUInt32LittleEndian(buf.Slice(RTT_US_OFFSET, sizeof(uint)));
            rttMs = rttUs / 1000;

            return true;
        } catch (SocketException)
        {
            return false;
        } catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static bool TryGetMacTcpRttMs(Socket socket, out long rttMs)
    {
        rttMs = 0;

        //getsockopt(IPPROTO_TCP, TCP_CONNECTION_INFO) returns macOS's `struct tcp_connection_info` (xnu bsd/netinet/tcp.h).
        //Different option name from Linux — macOS has no plain TCP_INFO. Available since macOS 10.10.
        //tcpi_srtt (uint32, MILLISECONDS — already the unit we want) sits at offset 44, after the leading byte/uint32 prefix.
        const int IPPROTO_TCP = 6;
        const int TCP_CONNECTION_INFO = 0x106;
        const int SRTT_MS_OFFSET = 44;
        const int READ_SIZE = 48;

        Span<byte> buf = stackalloc byte[READ_SIZE];

        try
        {
            var bytesRead = socket.GetRawSocketOption(IPPROTO_TCP, TCP_CONNECTION_INFO, buf);

            if (bytesRead < SRTT_MS_OFFSET + sizeof(uint))
                return false;

            rttMs = BinaryPrimitives.ReadUInt32LittleEndian(buf.Slice(SRTT_MS_OFFSET, sizeof(uint)));

            return true;
        } catch (SocketException)
        {
            return false;
        } catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="GameClient" /> class.
    /// </summary>
    public GameClient()
    {
        for (var i = 0; i < INITIAL_SEND_ARGS_COUNT; i++)
        {
            var args = new SocketAsyncEventArgs();
            args.Completed += ReuseSendArgs;
            SendArgsPool.Enqueue(args);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Disposed)
            return;

        Disposed = true;
        Disconnect();
    }

    /// <summary>
    ///     Connects synchronously to the specified host and port and begins receiving packets.
    /// </summary>
    /// <param name="host">The server hostname or IP address.</param>
    /// <param name="port">The server port.</param>
    public void Connect(string host, int port)
    {
        if (Disposed)
            throw new ObjectDisposedException(nameof(GameClient));

        Disconnect();

        Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        Socket.Connect(host, port);

        StartReceiveLoop();
    }

    /// <summary>
    ///     Connects asynchronously to the specified host and port and begins receiving packets.
    /// </summary>
    /// <param name="host">The server hostname or IP address.</param>
    /// <param name="port">The server port.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        if (Disposed)
            throw new ObjectDisposedException(nameof(GameClient));

        Disconnect();

        Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        await Socket.ConnectAsync(host, port, ct);

        StartReceiveLoop();
    }

    /// <summary>
    ///     Disconnects from the current server and cleans up resources.
    /// </summary>
    public void Disconnect()
    {
        if (!IsAlive)
            return;

        IsAlive = false;

        try
        {
            ReceiveCts?.Cancel();
        } catch
        {
            /* ignored */
        }

        try
        {
            Socket?.Shutdown(SocketShutdown.Both);
        } catch
        {
            /* ignored */
        }

        try
        {
            Socket?.Dispose();
        } catch
        {
            /* ignored */
        }

        Socket = null;

        //wait for the receive task to fully exit before cleaning up.
        //this prevents a race where the old task's finally block runs after
        //a new connection sets isalive=true, killing the new connection.
        try
        {
            ReceiveTask?.GetAwaiter()
                       .GetResult();
        } catch
        {
            /* ignored */
        }

        ReceiveTask = null;

        try
        {
            ReceiveMemoryOwner?.Dispose();
        } catch
        {
            /* ignored */
        }

        ReceiveMemoryOwner = null;
        ReceiveCount = 0;
        ReceiveCts?.Dispose();
        ReceiveCts = null;

        OnDisconnected?.Invoke();
    }

    /// <summary>
    ///     Drains queued inbound packets into the provided buffer. Call from the game loop thread.
    /// </summary>
    /// <param name="buffer">The list to append dequeued packets to.</param>
    /// <param name="maxCount">Maximum number of packets to drain per call.</param>
    /// <returns>The number of packets drained.</returns>
    public int DrainPackets(List<IServerPacket> buffer, int maxCount = int.MaxValue)
    {
        var count = 0;

        while ((count < maxCount) && InboundQueue.TryDequeue(out var pkt))
        {
            buffer.Add(pkt);
            count++;
        }

        return count;
    }

    /// <summary>
    ///     Fired when the client is disconnected from the server.
    /// </summary>
    public event DisconnectedHandler? OnDisconnected;

    /// <summary>
    ///     Sends a typed packet to the server. Thread-safe. Framing, encryption, and ordinal allocation are handled by
    ///     the codec from the packet's opcode.
    /// </summary>
    /// <param name="packet">The client packet to send.</param>
    public void Send(IClientPacket packet)
    {
        if (!Connected)
            return;

        ReadOnlyMemory<byte> wire;
        SocketAsyncEventArgs args;

        using (SendLock.EnterScope())
        {
            //EncodeClient advances Crypto.ClientOrdinal for encrypted opcodes; hold the lock across encode+dispatch so
            //ordinals stay monotonic and frames hit the socket in ordinal order.
            wire = Codec.EncodeClient(packet, Crypto);

            NoticeDebugLog.Write($"outbound opcode=0x{packet.Opcode:X2} len={wire.Length} hex={HexPreview(wire.Span)}");

            var owner = MemoryPool<byte>.Shared.Rent(wire.Length);
            wire.Span.CopyTo(owner.Memory.Span);
            args = DequeueSendArgs(owner, wire.Length);
        }

        try
        {
            var completedSynchronously = !Socket!.SendAsync(args);

            if (completedSynchronously)
                ReuseSendArgs(this, args);
        } catch
        {
            ReuseSendArgs(this, args);
        }
    }

    /// <summary>
    ///     Replaces the crypto state with a fresh, unkeyed <see cref="CryptoState" />. Called before the lobby connect,
    ///     where the only packets exchanged (Version, AcceptConnection, CryptoKey) are unencrypted.
    /// </summary>
    public void ResetCrypto() => Crypto = new CryptoState();

    /// <summary>
    ///     Installs fresh per-connection key material. This is the single re-key seam, used at both the lobby
    ///     <c>0x00 CryptoKey</c> handshake and every <c>0x03 Redirect</c> hop. A fresh <see cref="CryptoState" /> is
    ///     built each call, so the ordinal counters reset to zero and the new connection never inherits the previous
    ///     hop's key — required for servers that rotate key material per redirect (e.g. Hybrasyl).
    /// </summary>
    /// <param name="seed">Encryption seed (salt-table selector).</param>
    /// <param name="key">Encryption key for <see cref="EncryptMethod.Normal" /> packets.</param>
    /// <param name="keyTableSeed">
    ///     Seed string for the MD5 key table used by <see cref="EncryptMethod.MD5Key" /> packets — the character name at
    ///     the login->world hop. Null/empty falls back to <c>"default"</c> (lobby/login hops, which never exercise the
    ///     table).
    /// </param>
    public void ApplyCryptoKey(byte seed, byte[] key, string? keyTableSeed)
    {
        var crypto = new CryptoState
        {
            EncryptionSeed = seed,
            EncryptionKey = key
        };

        crypto.GenerateKeyTable(string.IsNullOrEmpty(keyTableSeed) ? "default" : keyTableSeed);

        Crypto = crypto;
    }

    /// <summary>
    ///     Resets the outbound (C->S) ordinal counter. Normally redundant — <see cref="ApplyCryptoKey" /> and
    ///     <see cref="ResetCrypto" /> already start a fresh counter — but retained for explicit handshake parity.
    /// </summary>
    /// <param name="newSequence">The new ordinal value.</param>
    public void SetSequence(byte newSequence) => Crypto.ClientOrdinal = newSequence;

    private void StartReceiveLoop()
    {
        ReceiveMemoryOwner = MemoryPool<byte>.Shared.Rent(RECEIVE_BUFFER_SIZE);
        ReceiveCount = 0;
        IsAlive = true;

        var generation = Interlocked.Increment(ref ConnectionGeneration);
        ReceiveCts = new CancellationTokenSource();
        ReceiveTask = Task.Run(() => ReceiveLoopAsync(ReceiveCts.Token, generation), ReceiveCts.Token);
    }

    #region Private
    private async Task ReceiveLoopAsync(CancellationToken ct, int generation)
    {
        try
        {
            while (!ct.IsCancellationRequested && IsAlive)
            {
                var memory = ReceiveMemoryOwner!.Memory[ReceiveCount..];

                if (memory.Length == 0)
                {
                    //buffer overflow — reset
                    ReceiveCount = 0;
                    memory = ReceiveMemoryOwner.Memory;
                }

                int bytesRead;

                try
                {
                    bytesRead = await Socket!.ReceiveAsync(memory, SocketFlags.None, ct);
                } catch (OperationCanceledException)
                {
                    break;
                } catch
                {
                    break;
                }

                if (bytesRead == 0)
                    break;

                ReceiveCount += bytesRead;

                ProcessReceivedData();
            }
        } finally
        {
            //only fire ondisconnected if this is still the active connection.
            //during redirects, a new connection may already be established.
            if (IsAlive && (generation == Volatile.Read(ref ConnectionGeneration)))
            {
                IsAlive = false;
                OnDisconnected?.Invoke();
            }
        }
    }

    private void ProcessReceivedData()
    {
        var offset = 0;

        while (ReceiveCount - offset >= WireFrameHeaderLength)
        {
            var remaining = ReceiveMemoryOwner!.Memory.Slice(offset, ReceiveCount - offset);
            var span = remaining.Span;

            if (span[0] != WireFrameMarker)
            {
                //stream desync — the only safe recovery is to drop the buffered remainder and resynchronize on the
                //next socket read.
                NoticeDebugLog.Write($"!!! frame marker mismatch 0x{span[0]:X2}; dropping {ReceiveCount - offset} buffered byte(s)");
                offset = ReceiveCount;

                break;
            }

            var frameLength = WireFrameHeaderLength + ((span[1] << 8) | span[2]);

            if (frameLength > ReceiveCount - offset)
                break; //incomplete frame; wait for more bytes

            var opcode = frameLength > WireFrameHeaderLength ? span[WireFrameHeaderLength] : (byte)0;

            try
            {
                if (Codec.TryGetServerPacket(remaining[..frameLength], Crypto, out var packet, out _))
                    DispatchInbound(packet!);
            } catch (Exception ex)
            {
                //a single malformed/unknown frame should not poison the rest of the buffer; log and skip past it.
                NoticeDebugLog.Write($"!!! inbound parse threw opcode=0x{opcode:X2} len={frameLength}: {ex.GetType().Name}: {ex.Message}");
            }

            offset += frameLength;
        }

        ReceiveCount -= offset;

        if (ReceiveCount > 0)
            ReceiveMemoryOwner!.Memory.Slice(offset, ReceiveCount)
                              .CopyTo(ReceiveMemoryOwner.Memory);
    }

    private void DispatchInbound(IServerPacket packet)
    {
        switch (packet)
        {
            //heartbeats are answered on the receive thread and never surface to the game loop.
            case ByteHeartbeatPacket byteHeartbeat:
                Send(
                    new DALib.Networking.Packets.Client.ByteHeartbeatPacket
                    {
                        First = byteHeartbeat.Second,
                        Second = byteHeartbeat.First
                    });

                return;
            case TickHeartbeatPacket tickHeartbeat:
                Send(
                    new DALib.Networking.Packets.Client.TickHeartbeatPacket
                    {
                        ServerTick = tickHeartbeat.ServerTick,
                        ClientTick = (uint)Environment.TickCount
                    });

                return;
            default:
                InboundQueue.Enqueue(packet);

                return;
        }
    }

    private SocketAsyncEventArgs DequeueSendArgs(IMemoryOwner<byte> owner, int length)
    {
        if (!SendArgsPool.TryDequeue(out var args))
        {
            args = new SocketAsyncEventArgs();
            args.Completed += ReuseSendArgs;
        }

        args.UserToken = owner;
        args.SetBuffer(owner.Memory[..length]);

        return args;
    }

    private static void ReuseSendArgs(object? sender, SocketAsyncEventArgs args)
    {
        if (args.UserToken is IMemoryOwner<byte> owner)
        {
            owner.Dispose();
            args.UserToken = null;
        }

        if (sender is GameClient client)
            client.SendArgsPool.Enqueue(args);
    }

    private static string HexPreview(ReadOnlySpan<byte> wire)
    {
        //first 64 bytes is enough to identify any packet we'd want to inspect; longer frames get truncated.
        var preview = Math.Min(wire.Length, 64);

        return Convert.ToHexString(wire[..preview]);
    }

    //outer frame: [0xAA marker][u16-BE body length]. Mirrors DALib's internal WireFrame constants, which are not public.
    private const byte WireFrameMarker = 0xAA;
    private const int WireFrameHeaderLength = 3;
    #endregion
}
