#region
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Net.Sockets;
using System.Threading.Channels;
using DALib.Networking.Crypto;
using DALib.Networking.Packets.Server;
using DALib.Networking.Wire;
using Hybrasyl.Protocol;
using Hybrasyl.Protocol.Negotiation;
using Hybrasyl.Protocol.Wire;
using Hybrasyl.Protocol.Transport;
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

    //the greeting is one small retail frame; a full retail frame can never exceed marker + u16 length.
    private const int GREETING_READ_BUFFER_SIZE = ushort.MaxValue + 3;

    /// <summary>
    ///     How long to wait for a server-first hop's greeting before declaring the connection dead.
    ///     Generous on purpose: this bounds a <em>liveness</em> failure, and is never allowed to decide
    ///     anything about capability. Internal-settable so tests need not wait it out.
    /// </summary>
    internal static TimeSpan GreetingTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     How long the TLS handshake and dialect negotiation together may take before the connection
    ///     is declared dead. A server can advertise capability and then fail to complete a handshake,
    ///     which would otherwise block forever — neither <c>SslStream</c> nor the negotiator imposes a
    ///     bound of its own. Mirrors the server's own handshake timeout. Internal-settable for tests.
    /// </summary>
    internal static TimeSpan UpgradeTimeout { get; set; } = TimeSpan.FromSeconds(10);

    //the codec is stateless and shared across every connection; only CryptoState is per-connection.
    private static readonly PacketCodec Codec = new();

    //the extension dispatch table, scanning the shared library only: a shape both ends speak must be
    //declared there, and registering a consumer-local type here would encode a frame no server can
    //resolve — a decode error on a live connection, with nothing failing at build or startup.
    private static readonly ExtensionCodec Extensions = new();

    private readonly ConcurrentQueue<IServerPacket> InboundQueue = new();

    private readonly ConcurrentQueue<IExtensionServerPacket> ExtensionQueue = new();

    private readonly Lock SendLock = new();
    private int ConnectionGeneration;
    private bool Disposed;
    private volatile bool IsAlive;
    private int ReceiveCount;
    private CancellationTokenSource? ReceiveCts;
    private IMemoryOwner<byte>? ReceiveMemoryOwner;
    private Task? ReceiveTask;

    //encoded frames queued for the send pump. Written under SendLock so channel order matches the
    //order the codec allocated crypto ordinals in; drained by the single SendTask.
    private Channel<byte[]>? SendQueue;
    private Task? SendTask;

    private Socket? Socket;

    /// <summary>
    ///     The byte stream this connection reads and writes. A <see cref="NetworkStream" /> for a
    ///     plaintext connection; the STARTTLS upgrade replaces it with an <c>SslStream</c> wrapping
    ///     the same socket, which is why all traffic goes through a stream rather than the socket
    ///     even before TLS exists.
    /// </summary>
    private Stream? Transport;

    /// <summary>
    ///     This connection's crypto state (seed, key, key table, and ordinal counters). Replaced wholesale at each
    ///     handshake via <see cref="ResetCrypto" /> / <see cref="ApplyCryptoKey" />.
    /// </summary>
    public CryptoState Crypto { get; private set; } = new();

    /// <summary>
    ///     The capability marker carried on the server's opening greeting, or null when no greeting was
    ///     read, the server sent none, or the greeting could not be trusted as an upgrade point. Null
    ///     means retail forever — silence is the fallback, decided by content rather than by a clock.
    /// </summary>
    public CapabilityMarker? ServerCapability { get; private set; }

    //the parsed greeting, held between the inline read and StartPumps so it cannot be dispatched while
    //the connection has no way to answer it. Touched only on the connect path, before any pump exists.
    private IServerPacket? PendingGreeting;

    /// <summary>
    ///     Whether this server family speaks TLS. Learned once from the lobby's capability marker and
    ///     <em>deliberately not reset by <see cref="Disconnect" /></em>: login and world are
    ///     client-first, so Brigid must decide to upgrade before the server says anything, and the
    ///     lobby marker is the only thing that can tell it that is safe.
    /// </summary>
    public bool UpgradeToTls { get; set; }

    /// <summary>
    ///     Supplies the TLS options for one server identity — the name the certificate is validated
    ///     against, pinned under, and sent as SNI. Null means platform default (system-root)
    ///     validation with no pinning.
    /// </summary>
    /// <remarks>
    ///     A factory rather than a stored callback because the identity can change mid-session: a
    ///     redirect to a server outside the lobby's identity authenticates as itself, and a validator
    ///     built for the previous hop would validate against the wrong name. It is consulted afresh on
    ///     every upgrade. The port is deliberately absent — a certificate is bound to a hostname, not
    ///     to a port, and all three hops of one server present the same certificate.
    /// </remarks>
    public Func<string, SslClientAuthenticationOptions>? TlsOptions { get; set; }

    /// <summary>The dialect this connection negotiated, or null when no TLS upgrade occurred.</summary>
    public DialectResolution? Negotiated { get; private set; }

    /// <summary>
    ///     The codec bound to this connection's dialect, or null when none is engaged. Null is the
    ///     invariant that no extension frame belongs on this connection: the retail modes carry
    ///     <c>0xAA</c> frames, which are DALib's business.
    /// </summary>
    private DialectCodec? ExtensionChannel;

    /// <summary>Whether an extension dialect is engaged, so extension frames may be sent.</summary>
    /// <remarks>
    ///     Distinct from having seen the capability marker, which only says the server family is
    ///     capable. Gating sends on the marker would put an extension frame on a connection that
    ///     resolved to a retail mode.
    /// </remarks>
    public bool DialectEngaged => ExtensionChannel is not null;

    /// <summary>
    ///     The version string reported to the server in the dialect negotiation, for its records.
    ///     Defaults to this assembly's version; the client overrides it with its display version.
    /// </summary>
    public string ClientVersion { get; set; } =
        typeof(GameClient).Assembly.GetName()
                          .Version?.ToString()
        ?? "0.0.0";

    /// <summary>The single dialect this client release speaks.</summary>
    private static readonly ClientDialectPolicy DialectPolicy = new(Dialect.V1);

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
    /// <summary>
    ///     Connects asynchronously to the specified host and port and begins receiving packets.
    /// </summary>
    /// <param name="host">The server hostname or IP address.</param>
    /// <param name="port">The server port.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task ConnectAsync(string host, int port, CancellationToken ct = default)
        => ConnectAsync(host, port, false, host, ct);

    /// <summary>
    ///     Connects asynchronously and, for a server-first hop, reads the server's opening greeting
    ///     before starting the receive pump — publishing any capability marker it carries on
    ///     <see cref="ServerCapability" />.
    /// </summary>
    /// <remarks>
    ///     The greeting is read inline rather than through the pump so that a STARTTLS upgrade has a
    ///     seam where nothing else owns the stream and no byte of the peer's has been buffered past
    ///     the frame. Buffering pre-handshake plaintext and acting on it afterwards is the entire
    ///     STARTTLS injection class, so the upgrade point is placed where that cannot happen.
    /// </remarks>
    /// <param name="host">The server hostname or IP address.</param>
    /// <param name="port">The server port.</param>
    /// <param name="expectGreeting">
    ///     True for the lobby, which is the only server-first hop; login and world are client-first
    ///     and have no greeting to wait for.
    /// </param>
    /// <param name="tlsHost">
    ///     The name this connection authenticates as — validated against the certificate, pinned
    ///     under, and sent as SNI. Usually <paramref name="host" />; the two differ on a redirect,
    ///     which carries an IP address and no name, where dialling the address preserves the server's
    ///     routing choice while the inherited name is what can actually be validated — an IP literal
    ///     matches no ordinary certificate.
    /// </param>
    /// <remarks>
    ///     <paramref name="tlsHost" /> is deliberately required rather than defaulted. Omitting it is
    ///     the failure this signature exists to prevent, and a default would make that omission
    ///     compile and behave correctly on every hop reached by name — every hop except the redirect,
    ///     which is the one that matters and the one no test covers end to end.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    public async Task ConnectAsync(
        string host,
        int port,
        bool expectGreeting,
        string tlsHost,
        CancellationToken ct = default)
    {
        if (Disposed)
            throw new ObjectDisposedException(nameof(GameClient));

        Disconnect();

        ServerCapability = null;
        Negotiated = null;
        ExtensionChannel = null;
        PendingGreeting = null;
        ExtensionQueue.Clear();

        Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        await Socket.ConnectAsync(host, port, ct);
        Transport = new NetworkStream(Socket, false);

        try
        {
            var carryOver = expectGreeting ? await ReadGreetingAsync(ct) : ReadOnlyMemory<byte>.Empty;

            //the lobby's marker is what licenses every later hop to upgrade, so record it before using it.
            if (expectGreeting)
                UpgradeToTls = ServerCapability is not null;

            if (UpgradeToTls && carryOver.IsEmpty)
                await UpgradeToTlsAsync(tlsHost, ct);

            StartPumps(carryOver.Span);
        } catch
        {
            //nothing is alive yet, so Disconnect declines to clean up and the socket would leak. That is
            //routine now rather than exceptional: a first-contact certificate is refused by design, and
            //the user is expected to accept and reconnect.
            AbandonTransport();

            throw;
        }
    }

    /// <summary>
    ///     Releases the transport and socket for a connection that never started its pumps.
    /// </summary>
    private void AbandonTransport()
    {
        try
        {
            Transport?.Dispose();
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

        Transport = null;
        Socket = null;
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

        //stop accepting sends before the transport goes away, so a Send racing this call is dropped
        //rather than writing to a disposed stream.
        SendQueue?.Writer.TryComplete();

        try
        {
            Socket?.Shutdown(SocketShutdown.Both);
        } catch
        {
            /* ignored */
        }

        try
        {
            Transport?.Dispose();
        } catch
        {
            /* ignored */
        }

        Transport = null;

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
            SendTask?.GetAwaiter()
                    .GetResult();
        } catch
        {
            /* ignored */
        }

        SendTask = null;
        SendQueue = null;

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
        var writer = SendQueue?.Writer;

        if (!Connected || writer is null)
        {
            //logged rather than dropped in silence: a packet discarded here leaves the client waiting on a
            //reply to something it never sent, which reads on the wire as a server that stopped talking.
            NoticeDebugLog.Write($"!!! dropped outbound opcode=0x{packet.Opcode:X2}: no live send pump");

            return;
        }

        using (SendLock.EnterScope())
        {
            //EncodeClient advances Crypto.ClientOrdinal for encrypted opcodes; hold the lock across encode+enqueue so
            //ordinals stay monotonic and the queue's FIFO order is the ordinal order the pump writes in.
            var wire = Codec.EncodeClient(packet, Crypto);

            NoticeDebugLog.Write($"outbound opcode=0x{packet.Opcode:X2} len={wire.Length} hex={HexPreview(wire.Span)}");

            writer.TryWrite(wire.ToArray());
        }
    }

    /// <summary>
    ///     Sends an extension packet on the negotiated dialect. Silently declined when no dialect is
    ///     engaged — callers gate on <see cref="DialectEngaged" /> rather than relying on this.
    /// </summary>
    public void SendExtension(IExtensionClientPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var writer = SendQueue?.Writer;

        if (ExtensionChannel is not { } channel || !Connected || writer is null)
        {
            NoticeDebugLog.Write(
                $"!!! dropped extension opcode=0x{packet.Opcode:X4}: {(ExtensionChannel is null ? "no dialect engaged" : "no live send pump")}");

            return;
        }

        //shares the retail send lock: it orders the queue as well as guarding crypto ordinals, and an
        //extension frame interleaved into the middle of a retail frame's enqueue would corrupt the stream.
        using var scope = SendLock.EnterScope();

        var wire = channel.EncodeClient(packet);

        //logged like its retail counterpart, with the wider opcode field spelled out: an extension
        //opcode is a u16, so 0x0100 and retail's 0x01 are different packets that would otherwise print
        //alike in a log where both framings appear interleaved.
        NoticeDebugLog.Write(
            $"outbound extension opcode=0x{packet.Opcode:X4} dialect=0x{(byte)channel.Dialect:X2} len={wire.Length} hex={HexPreview(wire)}");

        writer.TryWrite(wire);
    }

    /// <summary>
    ///     Moves any received extension packets into <paramref name="buffer" />, clearing it first.
    /// </summary>
    public void DrainExtensionPackets(List<IExtensionServerPacket> buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        buffer.Clear();

        while (ExtensionQueue.TryDequeue(out var packet))
            buffer.Add(packet);
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

    /// <summary>
    ///     Reads exactly one retail frame — the server's greeting — and publishes any capability
    ///     marker on <see cref="ServerCapability" />. Returns whatever arrived past that frame, for
    ///     the receive pump to consume.
    /// </summary>
    /// <remarks>
    ///     A marker is published only when the greeting is the last thing in the buffer. Hybrasyl is
    ///     silent after its greeting until the client speaks, so bytes past it mean this is not a
    ///     server whose upgrade point we can identify — and rather than fail a connection that works
    ///     today, we decline the upgrade and stay retail. Third-party servers Brigid already talks to
    ///     have not been verified to go quiet here, which is exactly why this degrades instead of
    ///     throwing.
    /// </remarks>
    /// <exception cref="TimeoutException">
    ///     No greeting arrived within <see cref="GreetingTimeout" />. This is deliberately fail-closed
    ///     rather than "assume retail and carry on": inferring plaintext from silence would hand an
    ///     attacker who cannot strip the marker the same result by merely delaying it. The timeout
    ///     declares the <em>connection</em> dead and never decides capability — and a connection with
    ///     no greeting could not have progressed anyway, since the lobby's first client packet is a
    ///     reply to it.
    /// </exception>
    private async Task<ReadOnlyMemory<byte>> ReadGreetingAsync(CancellationToken ct)
    {
        var buffer = new byte[GREETING_READ_BUFFER_SIZE];
        var filled = 0;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(GreetingTimeout);

        while (true)
        {
            if (filled >= WireFrameHeaderLength)
            {
                //a non-retail first byte is not something this client can resynchronize from.
                if (buffer[0] != WireFrameMarker)
                    throw new InvalidDataException(
                        $"Server greeting began 0x{buffer[0]:X2}, expected the retail frame marker 0x{WireFrameMarker:X2}.");

                var frameLength = WireFrameHeaderLength + ((buffer[1] << 8) | buffer[2]);

                if (frameLength > buffer.Length)
                    throw new InvalidDataException(
                        $"Server greeting claims {frameLength} bytes, beyond the {buffer.Length}-byte greeting buffer.");

                if (frameLength <= filled)
                {
                    PublishGreetingCapability(buffer.AsMemory(0, frameLength), isClean: filled == frameLength);

                    return buffer.AsMemory(frameLength, filled - frameLength);
                }
            }

            int read;

            try
            {
                read = await Transport!.ReadAsync(buffer.AsMemory(filled), deadline.Token);
            } catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                //phrased so it cannot be read as "extensions are unavailable" — a greeting that arrives
                //without a marker is the ordinary retail outcome and is silent, never an error.
                throw new TimeoutException(
                    $"The server accepted the connection but sent no greeting within {GreetingTimeout.TotalSeconds:0}s. "
                    + "Brigid needs the lobby's 0x7E greeting before it can begin the handshake.");
            }

            if (read == 0)
                throw new EndOfStreamException("Connection closed before the server greeting arrived.");

            filled += read;
        }
    }

    /// <summary>
    ///     Performs the STARTTLS upgrade: wraps the connection in TLS 1.3 and runs the dialect
    ///     negotiation inside it, leaving <see cref="Negotiated" /> set and <see cref="Transport" />
    ///     pointing at the encrypted stream.
    /// </summary>
    /// <remarks>
    ///     Called only with an empty receive buffer and before any pump exists, so no plaintext byte
    ///     of the peer's can survive into the encrypted session and nothing else holds the stream
    ///     during the handshake. Both properties are the caller's to preserve.
    /// </remarks>
    private async Task UpgradeToTlsAsync(string identity, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(UpgradeTimeout);

        SslStream tls;

        try
        {
            //the shared upgrade, so the TLS 1.3 postcondition is enforced rather than merely requested:
            //options cannot express it on a platform whose stack negotiates something lower.
            tls = await TlsChannel.UpgradeAsClientAsync(
                Transport!,
                TlsOptions?.Invoke(identity) ?? TlsConfig.ClientOptions(identity),
                cancellationToken: deadline.Token);
        } catch (Exception ex)
        {
            //logged here rather than left to the caller: an upgrade failure otherwise surfaces only as a
            //disconnect, with the reason — very often a certificate the system roots do not trust —
            //invisible in the log.
            NoticeDebugLog.Write(
                $"!!! TLS handshake with {identity} failed: {ex.GetType().Name}: {ex.Message}"
                + (ex.InnerException is { } inner ? $" <- {inner.GetType().Name}: {inner.Message}" : string.Empty));

            throw;
        }

        //published before negotiating so the negotiation itself travels encrypted.
        Transport = tls;

        NoticeDebugLog.Write($"TLS handshake with {identity} complete ({tls.SslProtocol}); negotiating dialect");

        try
        {
            var result = await DialectNegotiator.NegotiateAsClientAsync(tls, DialectPolicy, ClientVersion, deadline.Token);

            Negotiated = result.Resolution;

            //bound only for the engaged mode; a below-floor connection speaks retail framing inside TLS
            //and must not acquire a codec that would let an extension frame be sent on it.
            ExtensionChannel = result.Resolution.Mode == ConnectionMode.DialectOverTls
                ? Extensions.ForConnection(result.Resolution)
                : null;

            NoticeDebugLog.Write(
                $"TLS established with {identity}; mode={result.Resolution.Mode} dialect={result.Resolution.Dialect} "
                + $"offer=0x{result.Offer.MinDialect:X2}..0x{result.Offer.MaxDialect:X2}");
        } catch (Exception ex)
        {
            //a negotiation failure after a good handshake usually means the two ends disagree on the
            //negotiation wire format, which is not itself versioned by the dialect mechanism.
            NoticeDebugLog.Write(
                $"!!! dialect negotiation with {identity} failed: {ex.GetType().Name}: {ex.Message}");

            throw;
        }
    }

    private void PublishGreetingCapability(ReadOnlyMemory<byte> frame, bool isClean)
    {
        //the greeting still travels the normal path, so ConnectionManager's handler drives the client's
        //first send exactly as before — but it is held rather than queued here. Queuing it now publishes
        //it to the game loop, which drains on its own thread and would dispatch it mid-upgrade, when the
        //send pump does not yet exist and the handler's reply is silently dropped. StartPumps releases it.
        if (Codec.TryGetServerPacket(frame, Crypto, out var packet, out _) && (packet is not null))
            PendingGreeting = packet;

        if (!CapabilityMarker.TryRead(frame.Span[WireFrameHeaderLength..], out var marker))
            return;

        if (!isClean)
        {
            NoticeDebugLog.Write("capability marker present but bytes followed the greeting; declining the upgrade");

            return;
        }

        ServerCapability = marker;
        NoticeDebugLog.Write($"capability marker v{marker.Version} flags=0x{(byte)marker.Flags:X2}");
    }

    private void StartPumps(ReadOnlySpan<byte> carryOver)
    {
        ReceiveMemoryOwner = MemoryPool<byte>.Shared.Rent(RECEIVE_BUFFER_SIZE);
        carryOver.CopyTo(ReceiveMemoryOwner.Memory.Span);
        ReceiveCount = carryOver.Length;
        IsAlive = true;

        //single reader: the send pump is the only writer to the transport, which is what serializes
        //writes now that SslStream (which forbids overlapping writes) is a possible transport.
        SendQueue = Channel.CreateUnbounded<byte[]>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        var generation = Interlocked.Increment(ref ConnectionGeneration);
        ReceiveCts = new CancellationTokenSource();

        var token = ReceiveCts.Token;
        var reader = SendQueue.Reader;

        //release the greeting now that a reply can actually be sent: IsAlive and the send queue are both
        //up, so the handler this wakes reaches the pump rather than being dropped. Ahead of the carry-over
        //drain, so the greeting still arrives before anything that rode in behind it.
        if (PendingGreeting is { } greeting)
        {
            PendingGreeting = null;
            InboundQueue.Enqueue(greeting);
        }

        //drain anything that arrived alongside the greeting; the pump only processes after a read, so
        //a complete frame already in the buffer would otherwise wait on unrelated traffic. Safe to do
        //here because no pump is running yet.
        if (ReceiveCount > 0)
            ProcessReceivedData();

        ReceiveTask = Task.Run(() => ReceiveLoopAsync(token, generation), token);
        SendTask = Task.Run(() => SendLoopAsync(reader, token), token);
    }

    private async Task SendLoopAsync(ChannelReader<byte[]> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var wire in reader.ReadAllAsync(ct))
            {
                //read the transport at dispatch rather than capturing it: a STARTTLS upgrade replaces it,
                //and a frame queued before the swap must go out on whichever stream is current when it is
                //actually written.
                var transport = Transport;

                if (transport is null)
                    break;

                await transport.WriteAsync(wire, ct);
                await transport.FlushAsync(ct);
            }
        } catch (OperationCanceledException)
        {
            //disconnect cancelled the pump; nothing to report.
        } catch (Exception ex)
        {
            //the receive loop owns disconnect detection, so a dead socket here is logged, not escalated.
            NoticeDebugLog.Write($"!!! send pump stopped: {ex.GetType().Name}: {ex.Message}");
        }
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
                    bytesRead = await Transport!.ReadAsync(memory, ct);
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

        while (ReceiveCount - offset > 0)
        {
            var remaining = ReceiveMemoryOwner!.Memory.Slice(offset, ReceiveCount - offset);
            var span = remaining.Span;

            //route by byte 0, per the coexistence rule: retail and extension frames genuinely share one
            //stream, because packets not yet migrated keep retail framing inside TLS. Routing on content
            //rather than on connection state is what keeps that correct without tracking position in a
            //sequence. A length prefix can never begin with either marker under the frame-size cap.
            if (span[0] != WireFrameMarker)
            {
                if (!TryConsumeExtensionFrame(remaining, ref offset))
                    break;

                continue;
            }

            if (ReceiveCount - offset < WireFrameHeaderLength)
                break; //incomplete retail header; wait for more bytes

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

    /// <summary>
    ///     Consumes one extension frame off the front of <paramref name="remaining" />, advancing
    ///     <paramref name="offset" />. Returns false when the drain loop should stop — an incomplete
    ///     frame, or an unrecoverable one whose remainder has been discarded.
    /// </summary>
    private bool TryConsumeExtensionFrame(ReadOnlyMemory<byte> remaining, ref int offset)
    {
        if (ExtensionChannel is not { } channel)
        {
            //no dialect engaged means no extension frame belongs here, so this is a desync rather than a
            //packet: the only safe recovery is to drop the remainder and resynchronize on the next read.
            NoticeDebugLog.Write(
                $"!!! frame marker mismatch 0x{remaining.Span[0]:X2} with no dialect engaged; dropping {remaining.Length} buffered byte(s)");

            offset = ReceiveCount;

            return false;
        }

        try
        {
            if (!channel.TryDecodeServer(remaining, out var packet, out var consumed))
                return false; //incomplete frame; wait for more bytes

            offset += consumed;

            if (packet is not null)
                ExtensionQueue.Enqueue(packet);

            return true;
        } catch (Exception ex)
        {
            //unlike a retail frame, a malformed extension frame gives no length to skip past, so the
            //remainder cannot be resynchronized and is dropped whole.
            NoticeDebugLog.Write(
                $"!!! extension decode failed: {ex.GetType().Name}: {ex.Message}; dropping {remaining.Length} buffered byte(s)");

            offset = ReceiveCount;

            return false;
        }
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
