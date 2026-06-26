#region
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Chaos.Cryptography;
using Chaos.Extensions.Common;
using Chaos.Networking.Abstractions.Definitions;
using Chaos.Networking.Entities.Client;
using Chaos.Networking.Entities.Server;
using Chaos.Packets;
using Chaos.Packets.Abstractions;
#endregion

namespace Brigid.Networking;

/// <summary>
///     Client-side networking implementation for the Dark Ages protocol. Handles TCP connection, packet framing,
///     encryption, and serialization.
/// </summary>
public sealed class GameClient : IDisposable
{
    private const int RECEIVE_BUFFER_SIZE = ushort.MaxValue * 8;
    private const int INITIAL_SEND_ARGS_COUNT = 5;
    private readonly ConcurrentQueue<ServerPacket> InboundQueue = new();
    private readonly PacketSerializer PacketSerializer;
    private readonly ConcurrentQueue<SocketAsyncEventArgs> SendArgsPool = new();

    private readonly Lock SendLock = new();
    private readonly ServerPacketHandler?[] ServerHandlers = new ServerPacketHandler?[byte.MaxValue + 1];
    private int ConnectionGeneration;
    private bool Disposed;
    private volatile bool IsAlive;
    private int ReceiveCount;
    private CancellationTokenSource? ReceiveCts;
    private IMemoryOwner<byte>? ReceiveMemoryOwner;
    private Task? ReceiveTask;
    private int Sequence;

    private Socket? Socket;

    /// <summary>
    ///     The crypto instance used for encryption/decryption. Updated during connection handshake.
    /// </summary>
    public Crypto Crypto { get; set; } = new();

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
        PacketSerializer = BuildPacketSerializer();
        IndexHandlers();

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
    ///     Deserializes a <see cref="ServerPacket" /> into a strongly-typed args instance.
    /// </summary>
    /// <typeparam name="T">The args type to deserialize into.</typeparam>
    /// <param name="serverPacket">The server packet to deserialize.</param>
    public T Deserialize<T>(in ServerPacket serverPacket) where T: IPacketSerializable
    {
        var span = serverPacket.Data.AsSpan(0, serverPacket.Length);
        var isEncrypted = serverPacket.IsEncrypted;
        var packet = new Packet(ref span, isEncrypted);

        //the buffer in serverpacket.data has already been decrypted,
        //so we reconstruct a packet for the serializer to read
        //(the packet constructor expects the full wire bytes including header)
        return PacketSerializer.Deserialize<T>(in packet);
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
    public int DrainPackets(List<ServerPacket> buffer, int maxCount = int.MaxValue)
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
    ///     Fired when a packet is received that is not handled internally (heartbeat/sync). The subscriber receives the raw
    ///     <see cref="ServerPacket" /> for deferred deserialization.
    /// </summary>
    public event PacketReceivedHandler? OnPacketReceived;

    /// <summary>
    ///     Sends a serializable packet to the server. Thread-safe.
    /// </summary>
    public void Send<T>(T args) where T: IPacketSerializable
    {
        var packet = PacketSerializer.Serialize(args);
        Send(ref packet);
    }

    /// <summary>
    ///     Sends a raw packet to the server. Thread-safe.
    /// </summary>
    public void Send(ref Packet packet)
    {
        if (!Connected)
            return;

        //log outbound bytes BEFORE encryption so the hex matches what the wire-format design says we should be sending.
        //first 64 bytes of payload is enough to identify any packet we'd want to inspect; longer packets get truncated.
        var preview = Math.Min(packet.Length, 64);
        var hex = Convert.ToHexString(packet.Buffer[..preview]);
        NoticeDebugLog.Write($"outbound opcode=0x{packet.OpCode:X2} len={packet.Length} hex={hex}");

        packet.IsEncrypted = Crypto.IsClientEncrypted(packet.OpCode);
        SocketAsyncEventArgs args;

        using (SendLock.EnterScope())
        {
            if (packet.IsEncrypted)
            {
                packet.Sequence = (byte)Sequence++;
                Encrypt(ref packet);
            }

            (var owner, var length) = packet.TransferOwnership();
            args = DequeueSendArgs(owner, length);
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
    ///     Resets the outbound packet sequence counter, typically after a redirect or new connection.
    /// </summary>
    /// <param name="newSequence">The new sequence value.</param>
    public void SetSequence(byte newSequence) => Sequence = newSequence;

    private void StartReceiveLoop()
    {
        ReceiveMemoryOwner = MemoryPool<byte>.Shared.Rent(RECEIVE_BUFFER_SIZE);
        ReceiveCount = 0;
        IsAlive = true;

        var generation = Interlocked.Increment(ref ConnectionGeneration);
        ReceiveCts = new CancellationTokenSource();
        ReceiveTask = Task.Run(() => ReceiveLoopAsync(ReceiveCts.Token, generation), ReceiveCts.Token);
    }

    private delegate void ServerPacketHandler(in Packet packet);

    #region Private
    private void IndexHandlers()
    {
        ServerHandlers[(byte)ServerOpCode.HeartBeat] = HandleHeartBeat;
        ServerHandlers[(byte)ServerOpCode.SynchronizeTicks] = HandleSynchronizeTicks;
    }

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
        var buffer = ReceiveMemoryOwner!.Memory.Span;
        var offset = 0;
        var shouldReset = false;

        while (ReceiveCount > 3)
        {
            var packetLength = (buffer[offset + 1] << 8) + buffer[offset + 2] + 3;

            if (ReceiveCount < packetLength)
                break;

            if (ReceiveCount < 4)
                break;

            try
            {
                var packetSpan = buffer.Slice(offset, packetLength);
                HandleReceivedPacket(packetSpan);
            } catch
            {
                shouldReset = true;
            }

            ReceiveCount -= packetLength;
            offset += packetLength;
        }

        if (shouldReset)
            ReceiveCount = 0;

        if (ReceiveCount > 0)
            buffer.Slice(offset, ReceiveCount)
                  .CopyTo(buffer);
    }

    private void HandleReceivedPacket(Span<byte> rawPacket)
    {
        var opCode = rawPacket[3];
        var isEncrypted = Crypto.IsServerEncrypted(opCode);
        var packet = new Packet(ref rawPacket, isEncrypted);

        if (isEncrypted)
            Crypto.ClientDecrypt(ref packet.Buffer, packet.OpCode, packet.Sequence);

        //dispatch to registered handler (e.g. heartbeat, synchronizeticks auto-responders)
        var handler = ServerHandlers[opCode];

        if (handler is not null)
        {
            handler(in packet);

            return;
        }

        //default: enqueue for game loop consumption (rented buffer returned after deserialization)
        var wireLength = rawPacket.Length;
        var rented = ArrayPool<byte>.Shared.Rent(wireLength);
        rawPacket.CopyTo(rented);

        var serverPacket = new ServerPacket(
            opCode,
            packet.Sequence,
            isEncrypted,
            rented,
            wireLength);

        InboundQueue.Enqueue(serverPacket);
        OnPacketReceived?.Invoke(serverPacket);
    }

    private void HandleHeartBeat(in Packet packet)
    {
        var args = PacketSerializer.Deserialize<HeartBeatArgs>(in packet);

        Send(
            new HeartBeatResponseArgs
            {
                First = args.Second,
                Second = args.First
            });
    }

    private void HandleSynchronizeTicks(in Packet packet)
    {
        var args = PacketSerializer.Deserialize<SynchronizeTicksArgs>(in packet);

        Send(
            new SynchronizeTicksResponseArgs
            {
                ServerTicks = args.Ticks,
                ClientTicks = (uint)Environment.TickCount
            });
    }

    private void Encrypt(ref Packet packet) => Crypto.ClientEncrypt(ref packet.Buffer, packet.OpCode, packet.Sequence);

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

    private static PacketSerializer BuildPacketSerializer()
    {
        var converters = new Dictionary<Type, IPacketConverter>();

        var instances = typeof(IPacketConverter<>).LoadImplementations()
                                                  .Select(type => (IPacketConverter)Activator.CreateInstance(type)!);

        foreach (var instance in instances)
        {
            var argsType = instance.GetType()
                                   .GetInterfaces()
                                   .Where(i => i.IsGenericType)
                                   .First(i => i.GetGenericTypeDefinition() == typeof(IPacketConverter<>))
                                   .GetGenericArguments()
                                   .First();

            converters.TryAdd(argsType, instance);
        }

        return new PacketSerializer(Encoding.GetEncoding(949), converters);
    }
    #endregion
}

/// <summary>
///     Represents a received server packet with its raw wire bytes for deferred deserialization.
/// </summary>
public readonly record struct ServerPacket(
    byte OpCode,
    byte Sequence,
    bool IsEncrypted,
    byte[] Data,
    int Length);