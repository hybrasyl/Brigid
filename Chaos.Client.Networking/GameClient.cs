#region
using System.Buffers;
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

namespace Chaos.Client.Networking;

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
    ///     The remote endpoint of the active socket, or null when not connected. Used by latency monitoring to ping the
    ///     currently connected world server (which differs from the lobby host after a redirect).
    /// </summary>
    public IPEndPoint? RemoteEndPoint => Connected ? Socket?.RemoteEndPoint as IPEndPoint : null;

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