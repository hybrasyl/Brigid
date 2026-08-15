#region
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Chaos.DarkAges.Definitions;
using Chaos.Geometry.Abstractions.Definitions;
using DALib.Networking.Wire;
using Hybrasyl.Protocol.Packets;
using Hybrasyl.Protocol.Wire;
using Cli = DALib.Networking.Packets.Client;
using Server = DALib.Networking.Packets.Server;
#endregion

namespace Brigid.Networking;

/// <summary>
///     Manages the connection lifecycle: Lobby handshake, login redirect, and world entry. Drives state transitions and
///     orchestrates the GameClient through each phase. Inbound packets arrive already deserialized as DALib
///     <see cref="IServerPacket" /> values; handlers cast and fire events that carry the DALib packet directly. Outbound
///     sends translate the public method arguments into typed DALib <see cref="IClientPacket" /> values.
/// </summary>
public sealed class ConnectionManager : IDisposable
{
    private readonly Action<IServerPacket>?[] PacketHandlers = new Action<IServerPacket>?[byte.MaxValue + 1];
    private WorldEntryState EntryState;
    private RedirectInfo? PendingRedirect;

    //the redirect currently being followed, held until its connection succeeds so a hop refused over a
    //certificate can be retried once the user has ruled on it.
    private RedirectInfo? LastRedirect;

    /// <summary>
    ///     The host the lobby was reached by. This is the session's server identity: the later hops are
    ///     redirected to by address, and inherit this name when the address belongs to it.
    /// </summary>
    private string LobbyHost = string.Empty;

    /// <summary>
    ///     How long a redirect may spend resolving the lobby host before giving up and authenticating
    ///     the hop as its bare address. Short on purpose — it sits directly in the connect path, and
    ///     failing it costs a certificate prompt rather than the connection.
    /// </summary>
    private static readonly TimeSpan RedirectResolveTimeout = TimeSpan.FromSeconds(3);

    //set when the client confirms a logout (ClientExit endSignal=0). A redirect that arrives while in World
    //means "go back to the login screen" only if we asked to quit; an unsolicited World-state redirect is a
    //server-initiated world transfer (e.g. Temuair↔Medenia) that must reconnect straight back into World.
    private bool ExitConfirmed;

    /// <summary>
    ///     The local player's unique entity ID, assigned by the server upon world entry.
    /// </summary>
    public uint AislingId { get; private set; }

    /// <summary>
    ///     The name of the logged-in character, populated when <see cref="Login" /> is called.
    /// </summary>
    public string AislingName { get; private set; } = string.Empty;

    /// <summary>
    ///     The current map's metadata (map ID, dimensions, flags), updated on each map change.
    /// </summary>
    public Server.MapInfoPacket? MapInfo { get; private set; }

    /// <summary>
    ///     The player's current X tile coordinate, updated on walk confirmations and location packets.
    /// </summary>
    public int PlayerX { get; private set; }

    /// <summary>
    ///     The player's current Y tile coordinate, updated on walk confirmations and location packets.
    /// </summary>
    public int PlayerY { get; private set; }

    /// <summary>
    ///     The friendly name of the server the client is currently connected to (e.g. "Dark Ages", "Hybrasyl"). Raises
    ///     <see cref="ServerNameChanged" /> on change so derived surfaces (the window title) refresh from the write
    ///     itself rather than each write site having to remember to poke them — any future writer stays correct.
    /// </summary>
    public string ServerName
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            ServerNameChanged?.Invoke();
        }
    } = string.Empty;

    /// <summary>
    ///     Raised whenever <see cref="ServerName" /> changes. Fired on the thread that performs the write (today the
    ///     lobby/server-select flow on the game-loop thread); subscribers touching UI must not block.
    /// </summary>
    public event Action? ServerNameChanged;

    /// <summary>
    ///     Raised once per hop, after its connection is established, with the transport it ended up on.
    ///     The client layer records TLS history from this and decides whether a plaintext hop against a
    ///     server known to speak TLS warrants a warning.
    /// </summary>
    public event Action<TransportEstablished>? OnTransportEstablished;

    /// <summary>
    ///     The current phase of the connection lifecycle. Fires <see cref="StateChanged" /> on transitions.
    /// </summary>
    public ConnectionState State
    {
        get;

        private set
        {
            if (field == value)
                return;

            var old = field;
            field = value;
            NoticeDebugLog.Write($"ConnectionState {old} -> {value}");
            StateChanged?.Invoke(old, value);
        }
    } = ConnectionState.Disconnected;

    /// <summary>
    ///     The low-level TCP client used for packet I/O, encryption, and socket management.
    /// </summary>
    public GameClient Client { get; }

    public ConnectionManager()
    {
        Client = new GameClient();
        Client.OnDisconnected += HandleDisconnected;
        IndexHandlers();
    }

    private void SendIfWorld<T>(T packet) where T : IClientPacket
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(packet);
    }

    /// <inheritdoc />
    public void Dispose() => Client.Dispose();

    /// <summary>
    ///     Sends a password change request to the login server.
    /// </summary>
    public void ChangePassword(string name, string currentPassword, string newPassword)
    {
        if (State != ConnectionState.Login)
            return;

        Client.Send(
            new Cli.ChangePasswordPacket
            {
                Name = name,
                CurrentPassword = currentPassword,
                NewPassword = newPassword
            });
    }

    /// <summary>
    ///     Sends a click on an entity (NPC, creature, aisling, ground item).
    /// </summary>
    public void ClickEntity(uint targetId) => SendIfWorld(Cli.ClickPacket.Entity(targetId));

    /// <summary>
    ///     Sends a click on a map tile (floor-aligned: signpost, ground item, stair base).
    /// </summary>
    public void ClickTile(int x, int y) => SendIfWorld(Cli.ClickPacket.Point((ushort)x, (ushort)y, 1));

    /// <summary>
    ///     Sends a click on a door tile. Door panels are above-tile sprites, so the anchor flag is 0
    ///     (per the retail 0x43 point-click wire model; see comhaigne 0x43-point-click).
    /// </summary>
    public void ClickDoor(int x, int y) => SendIfWorld(Cli.ClickPacket.Point((ushort)x, (ushort)y, 0));

    /// <summary>
    ///     Sends a coord click for a floor-aligned tile target (signpost, ground item, door frame, stair base).
    /// </summary>
    public void ClickFloorTile(int x, int y) => SendIfWorld(Cli.ClickPacket.Point((ushort)x, (ushort)y, 1));

    /// <summary>
    ///     Sends a world map node click.
    /// </summary>
    public void ClickWorldMapNode(
        ushort mapId,
        int x,
        int y,
        ushort checkSum)
        => SendIfWorld(
            new Cli.MapPointClickPacket
            {
                CheckSum = checkSum,
                MapId = mapId,
                X = (ushort)x,
                Y = (ushort)y
            });

    /// <summary>
    ///     Connects to the lobby server, performs the Version/ConnectionInfo handshake.
    /// </summary>
    public async Task ConnectToLobbyAsync(
        string host,
        int port,
        ushort clientVersion,
        CancellationToken ct = default)
    {
        State = ConnectionState.Connecting;
        Client.ResetCrypto();
        Client.SetSequence(0);

        //set all acceptconnection handler state before connectasync starts the receive loop.
        LobbyClientVersion = clientVersion;
        PendingLobbyVersion = true;
        PendingTargetState = ConnectionState.Lobby;
        LobbyHost = host;

        //a session starting over has no hop to retry. Left set, a redirect abandoned earlier would be
        //re-followed by the next certificate the user accepts at the lobby.
        LastRedirect = null;

        try
        {
            //the lobby is the only server-first hop: its 0x7E greeting carries the capability marker,
            //and is read before the pumps start so an upgrade has a clean seam.
            await Client.ConnectAsync(host, port, true, host, ct);
            ReportTransport(host, ConnectionState.Lobby);
        } catch (Exception ex)
        {
            PendingLobbyVersion = false;
            State = ConnectionState.Disconnected;
            NoticeDebugLog.Write($"!!! lobby connect failed: {ex.GetType().Name}: {ex.Message}");
            OnError?.Invoke($"Failed to connect to lobby: {ex.Message}");
        }
    }

    /// <summary>
    ///     Sends the finalized character creation request (appearance choices) to the login server.
    /// </summary>
    public void CreateCharFinalize(byte hairStyle, Gender gender, DisplayColor hairColor)
    {
        if (State != ConnectionState.Login)
            return;

        Client.Send(
            new Cli.CreateCharFinalizePacket
            {
                HairStyle = hairStyle,
                Gender = ToDalibGender(gender),
                HairColor = (byte)hairColor
            });
    }

    /// <summary>
    ///     Sends the initial character creation request (name and password) to the login server.
    /// </summary>
    public void CreateCharInitial(string name, string password)
    {
        if (State != ConnectionState.Login)
            return;

        Client.Send(
            new Cli.CreateCharRequestPacket
            {
                Name = name,
                Password = password,
                Email = string.Empty
            });
    }

    /// <summary>
    ///     Sends a gold drop request onto the ground.
    /// </summary>
    public void DropGold(int amount, int x, int y)
        => SendIfWorld(
            new Cli.DropGoldPacket
            {
                Amount = (uint)amount,
                X = (ushort)x,
                Y = (ushort)y
            });

    /// <summary>
    ///     Sends a gold give request to a creature/NPC.
    /// </summary>
    public void DropGoldOnCreature(int amount, uint targetId)
        => SendIfWorld(
            new Cli.DropGoldOnCreaturePacket
            {
                Amount = (uint)amount,
                TargetId = targetId
            });

    /// <summary>
    ///     Sends an item drop request onto the ground.
    /// </summary>
    public void DropItem(
        byte sourceSlot,
        int x,
        int y,
        int count = 1)
        => SendIfWorld(
            new Cli.DropItemPacket
            {
                Slot = sourceSlot,
                X = (ushort)x,
                Y = (ushort)y,
                Count = (uint)count
            });

    /// <summary>
    ///     Sends an item give request to a creature/NPC.
    /// </summary>
    public void DropItemOnCreature(byte sourceSlot, uint targetId, byte count = 1)
        => SendIfWorld(
            new Cli.DropItemOnCreaturePacket
            {
                Slot = sourceSlot,
                TargetId = targetId,
                Count = count
            });

    /// <summary>
    ///     Follows a pending redirect onto the next hop. Async because login and world are
    ///     client-first: when the lobby advertised TLS, the upgrade must complete before the
    ///     <c>ClientJoin</c> below goes out, or the credentials it carries travel in plaintext.
    ///     Clearing <see cref="PendingRedirect" /> up front is what keeps the game loop from
    ///     starting a second one while this is in flight.
    /// </summary>
    private async Task FollowPendingRedirectAsync()
    {
        if (PendingRedirect is not { } redirect)
            return;

        //cleared before the await, not after: ProcessPackets re-checks this every frame and would
        //otherwise launch a second hop while this one is still handshaking.
        PendingRedirect = null;
        LastRedirect = redirect;

        State = ConnectionState.Connecting;

        //lobby→login uses empty keysaltseed (falls back to "default"), login→world uses character name
        var keySaltSeed = redirect.TargetState == ConnectionState.Login ? string.Empty : redirect.Name;
        Client.ApplyCryptoKey(redirect.Seed, redirect.Key, keySaltSeed);
        Client.SetSequence(0);
        EntryState = WorldEntryState.None;
        PendingTargetState = redirect.TargetState;

        var identity = await ResolveRedirectIdentityAsync(redirect.EndPoint.Address);

        try
        {
            await Client.ConnectAsync(
                redirect.EndPoint.Address.ToString(),
                redirect.EndPoint.Port,
                false,
                identity);
        } catch (Exception ex)
        {
            State = ConnectionState.Disconnected;
            NoticeDebugLog.Write($"!!! redirect connect failed: {ex.GetType().Name}: {ex.Message}");
            OnError?.Invoke($"Failed to connect after redirect: {ex.Message}");

            return;
        }

        //kept until here so a certificate refused on this hop can be trusted and the same hop retried;
        //restarting from the lobby would cost the user their credentials a second time.
        LastRedirect = null;
        ReportTransport(identity, redirect.TargetState);

        //send clientjoin immediately after connecting.
        //not all servers send acceptconnection before expecting this packet.
        State = PendingTargetState;

        Client.Send(
            new Cli.ClientJoinPacket
            {
                EncryptionSeed = redirect.Seed,
                EncryptionKey = redirect.Key,
                Name = redirect.Name,
                RedirectId = redirect.Id
            });

        if (State == ConnectionState.Login)
            RequestHomepage();
    }

    /// <summary>
    ///     The name a redirect hop authenticates as: the lobby's host when the redirect address is one
    ///     of that host's own addresses, otherwise the address itself.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The retail <c>0x03</c> redirect carries an IP address and no name, and an IP literal
    ///         matches no ordinary certificate — so without this every hop after the lobby falls to
    ///         trust-on-first-use and re-prompts for a certificate the user already approved.
    ///     </para>
    ///     <para>
    ///         DNS is not an authentication input here. It decides only <em>which name</em> is put to
    ///         the certificate check; a resolver that lies produces a name the presented certificate
    ///         does not match, or a fingerprint that does not match the pin, and the hop is refused on
    ///         that evidence rather than on this lookup.
    ///     </para>
    /// </remarks>
    internal async Task<string> ResolveRedirectIdentityAsync(IPAddress target)
    {
        if (string.IsNullOrEmpty(LobbyHost))
            return target.ToString();

        //a lobby reached by bare address needs no resolver; it is already the comparison.
        if (IPAddress.TryParse(LobbyHost, out var literal))
            return ChooseRedirectIdentity(LobbyHost, [literal], target);

        IPAddress[] addresses;

        try
        {
            using var deadline = new CancellationTokenSource(RedirectResolveTimeout);
            addresses = await Dns.GetHostAddressesAsync(LobbyHost, deadline.Token);
        } catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            NoticeDebugLog.Write(
                $"redirect identity: could not resolve {LobbyHost} ({ex.GetType().Name}); {target} authenticates as itself");

            return target.ToString();
        }

        var identity = ChooseRedirectIdentity(LobbyHost, addresses, target);

        if (identity != LobbyHost)
            NoticeDebugLog.Write($"redirect to {target} is not an address of {LobbyHost}; it authenticates as itself");

        return identity;
    }

    /// <summary>
    ///     The decision inside <see cref="ResolveRedirectIdentityAsync" />, separated from the lookup
    ///     that feeds it: the lobby's host when <paramref name="target" /> is one of
    ///     <paramref name="lobbyAddresses" />, otherwise the address as its own identity.
    /// </summary>
    internal static string ChooseRedirectIdentity(
        string lobbyHost,
        IReadOnlyList<IPAddress> lobbyAddresses,
        IPAddress target)
    {
        if (string.IsNullOrEmpty(lobbyHost))
            return target.ToString();

        for (var i = 0; i < lobbyAddresses.Count; i++)
            if (lobbyAddresses[i].Equals(target))
                return lobbyHost;

        return target.ToString();
    }

    /// <summary>
    ///     Announces what transport a completed hop ended up on, so the client layer can record TLS
    ///     history and notice a server that has stopped upgrading.
    /// </summary>
    private void ReportTransport(string identity, ConnectionState target)
        => OnTransportEstablished?.Invoke(
            new TransportEstablished(identity, Client.Negotiated is not null, target));

    /// <summary>
    ///     Drops the current connection. For a caller that has decided not to proceed — a refused
    ///     downgrade warning — rather than for error paths, which tear down through the client already.
    /// </summary>
    public void Disconnect() => Client.Disconnect();

    /// <summary>
    ///     Sends an echo probe and reports the round-trip time when its reply arrives. Returns false
    ///     when no dialect is engaged, which is the ordinary state on a retail connection.
    /// </summary>
    /// <remarks>
    ///     Application-layer, and therefore a different measurement from
    ///     <c>LatencyMonitor</c>'s kernel RTT: this one crosses the TLS record layer, both frame
    ///     codecs, and the server's dispatch, so it reports what a packet actually costs rather than
    ///     what the socket did. The token is a local timestamp echoed verbatim, so the reply carries
    ///     its own start time and nothing has to be held between the halves.
    /// </remarks>
    public bool SendEchoProbe()
    {
        if (!Client.DialectEngaged)
            return false;

        Client.SendExtension(new ClientEcho((ulong)Stopwatch.GetTimestamp()));

        return true;
    }

    /// <summary>
    ///     Raised on the game loop with the round-trip time of an echo reply.
    /// </summary>
    public event Action<TimeSpan>? OnEchoReply;

    private readonly List<IExtensionServerPacket> ExtensionBuffer = [];

    /// <summary>
    ///     Drains extension packets. Separate from <see cref="ProcessPackets" />'s retail buffer
    ///     because the two do not share a packet interface, but driven from the same call so both
    ///     arrive on the game loop.
    /// </summary>
    private void ProcessExtensionPackets()
    {
        Client.DrainExtensionPackets(ExtensionBuffer);

        foreach (var packet in ExtensionBuffer)
        {
            //mirrors the retail dispatch line so both framings read alike in one log, with the u16
            //opcode written wide enough that extension 0x0100 cannot be mistaken for retail 0x01.
            NoticeDebugLog.Write(
                $"inbound extension opcode=0x{packet.Opcode:X4} handled={packet is ClientEcho} state={State}");

            if (packet is not ClientEcho echo)
                continue;

            //the token is the timestamp this client stamped; the server returns it verbatim.
            var roundTrip = Stopwatch.GetElapsedTime((long)echo.Token);

            //a token we never stamped cannot have started before now, so a negative elapsed time
            //identifies a reply that is not ours rather than a very fast one.
            if (roundTrip < TimeSpan.Zero)
                NoticeDebugLog.Write($"!!! echo reply carried a token this client never sent: 0x{echo.Token:X16}");
            else
                OnEchoReply?.Invoke(roundTrip);
        }
    }

    /// <summary>
    ///     Re-arms the redirect whose connection attempt failed, so the game loop follows it again.
    ///     Returns false when there is nothing to retry.
    /// </summary>
    public bool RetryLastRedirect()
    {
        if (LastRedirect is not { } redirect)
            return false;

        PendingRedirect = redirect;

        return true;
    }

    /// <summary>
    ///     Sends login credentials to the login server.
    /// </summary>
    public void Login(
        string name,
        string password,
        uint clientId1 = 1,
        uint clientId2 = 1)
    {
        if (State != ConnectionState.Login)
            return;

        //ClientId1/2 have no DALib LoginPacket field (the integrity trailer is codec-internal); retained on the
        //signature for caller compatibility but unused.
        _ = clientId1;
        _ = clientId2;

        AislingName = name;

        Client.Send(
            new Cli.LoginPacket
            {
                Name = name,
                Password = password
            });
    }

    /// <summary>Fired when an item is added to the inventory pane.</summary>
    public event Action<Server.AddItemPacket>? OnAddItemToPane;

    /// <summary>Fired when a skill is added to the skill pane.</summary>
    public event Action<Server.AddSkillPacket>? OnAddSkillToPane;

    /// <summary>Fired when a spell is added to the spell pane.</summary>
    public event Action<Server.AddSpellPacket>? OnAddSpellToPane;

    /// <summary>Fired when a spell/effect animation should play.</summary>
    public event Action<Server.SpellAnimationPacket>? OnAnimation;

    /// <summary>Fired when player attributes are updated. Carries the raw partial DALib packet; merge lives in PlayerAttributes.</summary>
    public event Action<Server.AttributesPacket>? OnAttributes;

    /// <summary>Fired when a body animation is triggered on an entity.</summary>
    public event Action<Server.PlayerAnimationPacket>? OnBodyAnimation;

    /// <summary>Fired when a casting animation should be cancelled.</summary>
    public event CancelCastingHandler? OnCancelCasting;

    /// <summary>Fired when the server confirms the player's own walk. Args: (direction, oldX, oldY).</summary>
    public event ClientWalkResponseHandler? OnClientWalkResponse;

    /// <summary>Fired when a skill or spell cooldown starts.</summary>
    public event Action<Server.CooldownPacket>? OnCooldown;

    /// <summary>Fired when another entity changes facing direction. Args: (sourceId, direction).</summary>
    public event CreatureTurnHandler? OnCreatureTurn;

    /// <summary>Fired when another entity walks. Args: (sourceId, oldX, oldY, direction).</summary>
    public event CreatureWalkHandler? OnCreatureWalk;

    /// <summary>Fired when an aisling display is received.</summary>
    public event Action<Server.DisplayUserPacket>? OnDisplayAisling;

    /// <summary>Fired when a bulletin board should be displayed.</summary>
    public event Action<Server.BoardResponsePacket>? OnDisplayBoard;

    /// <summary>Fired when an NPC dialog should be displayed.</summary>
    public event Action<Server.NpcDialogPacket>? OnDisplayDialog;

    /// <summary>Fired when an editable notepad should be displayed.</summary>
    public event Action<Server.EditablePaperPacket>? OnDisplayEditableNotepad;

    /// <summary>Fired when an exchange/trade window should be displayed.</summary>
    public event Action<Server.ExchangeResponsePacket>? OnDisplayExchange;

    /// <summary>Fired when a group invite/recruit packet is received.</summary>
    public event Action<Server.GroupResponsePacket>? OnDisplayGroupInvite;

    /// <summary>Fired when an NPC menu should be displayed.</summary>
    public event Action<Server.NpcMenuPacket>? OnDisplayMenu;

    /// <summary>Fired when a public chat message is displayed.</summary>
    public event Action<Server.PublicMessagePacket>? OnDisplayPublicMessage;

    /// <summary>Fired when a read-only notepad should be displayed.</summary>
    public event Action<Server.ReadonlyPaperPacket>? OnDisplayReadonlyNotepad;

    /// <summary>Fired when an equipment slot is cleared.</summary>
    public event Action<Server.RemoveEquipmentPacket>? OnDisplayUnequip;

    /// <summary>Fired when a visible entity (non-aisling) batch is received.</summary>
    public event Action<Server.DrawObjectsPacket>? OnDisplayVisibleEntities;

    /// <summary>Fired when door states are updated.</summary>
    public event Action<Server.DoorPacket>? OnDoor;

    /// <summary>Fired when a status effect icon is applied or removed.</summary>
    public event Action<Server.StatusBarPacket>? OnEffect;

    /// <summary>Fired when an equipment slot is updated.</summary>
    public event Action<Server.AddEquipmentPacket>? OnEquipment;

    /// <summary>Fired when an error occurs during connection or handshake.</summary>
    public event ConnectionErrorHandler? OnError;

    /// <summary>Fired when a logout response is received.</summary>
    public event Action<Server.ConfirmExitPacket>? OnExitResponse;

    /// <summary>Fired when an entity's health bar should be displayed.</summary>
    public event Action<Server.HealthBarPacket>? OnHealthBar;

    /// <summary>Fired when the ambient light level changes (time of day).</summary>
    public event Action<Server.LightLevelPacket>? OnLightLevel;

    /// <summary>Fired when the player's location changes.</summary>
    public event LocationChangedHandler? OnLocationChanged;

    /// <summary>Fired when a login control (homepage URL) is received.</summary>
    public event Action<Server.UrlPacket>? OnLoginControl;

    /// <summary>Fired when a login message is received (success, failure, or informational).</summary>
    public event Action<Server.LoginMessagePacket>? OnLoginMessage;

    /// <summary>Fired when a login notice (EULA) is received.</summary>
    public event Action<Server.LoginNotificationPacket>? OnLoginNotice;

    /// <summary>Fired when a map change is about to begin.</summary>
    public event MapChangePendingHandler? OnMapChangePending;

    /// <summary>Fired for each row of map data received.</summary>
    public event Action<Server.MapDataPacket>? OnMapData;

    /// <summary>Fired when map info is received, before map data arrives.</summary>
    public event Action<Server.MapInfoPacket>? OnMapInfo;

    /// <summary>Fired when the server signals that map loading is complete.</summary>
    public event MapLoadCompleteHandler? OnMapLoadComplete;

    /// <summary>Fired when metadata is received.</summary>
    public event Action<Server.MetafilePacket>? OnMetaData;

    /// <summary>Fired when another player's profile is received.</summary>
    public event Action<Server.ProfilePacket>? OnOtherProfile;

    /// <summary>Fired when a redirect is received and the client needs to connect to a new server.</summary>
    public event RedirectReceivedHandler? OnRedirectReceived;

    /// <summary>Fired when a viewport refresh response is received.</summary>
    public event RefreshResponseHandler? OnRefreshResponse;

    /// <summary>Fired when an entity is removed from the viewport.</summary>
    public event RemoveEntityHandler? OnRemoveEntity;

    /// <summary>Fired when an item is removed from the inventory pane.</summary>
    public event Action<Server.RemoveItemPacket>? OnRemoveItemFromPane;

    /// <summary>Fired when a skill is removed from the skill pane.</summary>
    public event Action<Server.RemoveSkillPacket>? OnRemoveSkillFromPane;

    /// <summary>Fired when a spell is removed from the spell pane.</summary>
    public event Action<Server.RemoveSpellPacket>? OnRemoveSpellFromPane;

    /// <summary>Fired when the player's own profile is received.</summary>
    public event Action<Server.SelfProfilePacket>? OnSelfProfile;

    /// <summary>Fired when a system message is received (yellow text, overhead, etc.).</summary>
    public event Action<Server.SystemMessagePacket>? OnServerMessage;

    /// <summary>Fired when the lobby handshake completes and the server table is received.</summary>
    public event ServerTableReceivedHandler? OnServerTableReceived;

    /// <summary>Fired when a sound or music track should play.</summary>
    public event Action<Server.PlaySoundPacket>? OnSound;

    /// <summary>Fired when the server assigns the local player's entity ID during world entry.</summary>
    public event UserIdHandler? OnUserId;

    /// <summary>Fired when world entry is complete and all essential data has been received.</summary>
    public event WorldEntryCompleteHandler? OnWorldEntryComplete;

    /// <summary>Fired when a world list (online players) is received.</summary>
    public event Action<Server.UserListPacket>? OnWorldList;

    /// <summary>Fired when the world map should be displayed.</summary>
    public event Action<Server.WorldMapPacket>? OnWorldMap;

    /// <summary>Fired when the server requests the player's portrait and profile text.</summary>
    public event EditableProfileRequestHandler? OnEditableProfileRequest;

    /// <summary>Fired when the connection state changes. Args: (oldState, newState).</summary>
    public event ConnectionStateChangedHandler? StateChanged;

    /// <summary>
    ///     Sends a pickup request from a tile.
    /// </summary>
    public void PickupItem(int x, int y, byte destinationSlot)
        => SendIfWorld(
            new Cli.PickupItemPacket
            {
                Slot = destinationSlot,
                X = (ushort)x,
                Y = (ushort)y
            });

    /// <summary>
    ///     Processes queued inbound packets, driving state transitions. Call this from the game loop's Update method.
    /// </summary>
    public void ProcessPackets(List<IServerPacket> buffer)
    {
        ProcessExtensionPackets();
        Client.DrainPackets(buffer);

        foreach (var pkt in buffer)
            try
            {
                HandlePacket(pkt);
            } catch (Exception ex)
            {
                //log every downstream failure (cast mismatches, NREs in event handlers, etc.) so protocol divergence
                //between target servers is visible instead of silently dropped.
                NoticeDebugLog.Write($"!!! handler threw opcode=0x{pkt.Opcode:X2} {ex.GetType().Name}: {ex.Message}");
                NoticeDebugLog.Write($"  stack: {ex.StackTrace}");
            }

        //follow pending redirect once the old connection is fully torn down. Not awaited: this runs on
        //the game loop, and the hop now includes a TLS handshake. Failures route to OnError inside.
        if (PendingRedirect is not null && !Client.Connected)
            _ = FollowPendingRedirectAsync();
    }

    /// <summary>
    ///     Sends a raise stat request.
    /// </summary>
    public void RaiseStat(Stat stat) => SendIfWorld(new Cli.StatPointPacket { Stat = StatSelector(stat) });

    /// <summary>
    ///     Sends an exit/logout request.
    /// </summary>
    public void RequestExit(bool isRequest = true)
    {
        //the confirm (endSignal=0) is what makes the server redirect us back to login; mark it so the
        //incoming World-state redirect routes to Login instead of being treated as a world transfer.
        if (!isRequest)
            ExitConfirmed = true;

        SendIfWorld(
            new Cli.ClientExitPacket
            {
                Signal = isRequest ? Cli.ExitSignal.Request : Cli.ExitSignal.Confirm
            });
    }

    /// <summary>
    ///     Requests the homepage URL from the login server.
    /// </summary>
    public void RequestHomepage()
    {
        if (State != ConnectionState.Login)
            return;

        Client.Send(new Cli.RequestHomepagePacket());
    }

    /// <summary>
    ///     Requests tile data for the current map from the server.
    /// </summary>
    public void RequestMapData()
        => SendIfWorld(
            new Cli.RequestMapPacket
            {
                X = MapInfo?.Width ?? 0,
                Y = MapInfo?.Height ?? 0
            });

    /// <summary>
    ///     Requests the full login notice (EULA) from the login server.
    /// </summary>
    public void RequestNotice()
    {
        if (State != ConnectionState.Login)
            return;

        Client.Send(new Cli.RequestNotificationPacket());
    }

    /// <summary>
    ///     Sends a refresh request (F5).
    /// </summary>
    public void RequestRefresh() => SendIfWorld(new Cli.RefreshPacket());

    /// <summary>
    ///     Sends a self profile request.
    /// </summary>
    public void RequestSelfProfile() => SendIfWorld(new Cli.RequestProfilePacket());

    /// <summary>
    ///     Requests the server table from the lobby.
    /// </summary>
    public void RequestServerTable()
    {
        if (State != ConnectionState.Lobby)
            return;

        Client.Send(new Cli.ServerTableRequestPacket());
    }

    /// <summary>
    ///     Sends a world list request.
    /// </summary>
    public void RequestWorldList() => SendIfWorld(new Cli.RequestWorldListPacket());

    /// <summary>
    ///     Selects a server from the server table by ID, triggering a redirect.
    /// </summary>
    public void SelectServer(byte serverId)
    {
        if (State != ConnectionState.Lobby)
            return;

        Client.Send(new Cli.ServerTableSelectPacket { ServerId = serverId });
    }

    /// <summary>
    ///     Adds a player to the ignore list.
    /// </summary>
    public void SendAddIgnore(string targetName) => SendIfWorld(Cli.IgnorePacket.AddUser(targetName));

    /// <summary>
    ///     Sends a begin chant packet to start spell casting.
    /// </summary>
    public void SendBeginChant(byte castLineCount) => SendIfWorld(new Cli.BeginCastingPacket { Lines = castLineCount });

    /// <summary>
    ///     Sends a board/mail interaction (view board, read post, send mail, delete, etc.).
    /// </summary>
    public void SendBoardInteraction(
        BoardRequestType requestType,
        ushort boardId = 0,
        short postId = 0,
        short startPostId = 0,
        BoardControls? controls = null,
        string? to = null,
        string? subject = null,
        string? message = null)
    {
        Cli.BoardRequestPacket? packet = requestType switch
        {
            BoardRequestType.BoardList => new Cli.BoardListPacket(),
            BoardRequestType.ViewBoard => new Cli.ViewBoardPacket
            {
                BoardId = boardId,
                StartPostId = startPostId,
                Offset = (sbyte)(-BoardProtocol.PageSize)
            },
            //offset drives server-side prev/next navigation: 0 = exact post, 1 = newer, -1 = older
            BoardRequestType.ViewPost => new Cli.ViewPostPacket
            {
                BoardId = boardId,
                PostId = postId,
                Offset = (sbyte)(controls ?? BoardControls.RequestPost)
            },
            BoardRequestType.NewPost => new Cli.NewPostPacket
            {
                BoardId = boardId,
                Subject = subject ?? string.Empty,
                Body = message ?? string.Empty
            },
            BoardRequestType.Delete => new Cli.DeletePostPacket
            {
                BoardId = boardId,
                PostId = postId
            },
            BoardRequestType.SendMail => new Cli.SendMailPacket
            {
                BoardId = boardId,
                Recipient = to ?? string.Empty,
                Subject = subject ?? string.Empty,
                Body = message ?? string.Empty
            },
            BoardRequestType.Highlight => new Cli.HighlightPostPacket
            {
                BoardId = boardId,
                PostId = postId
            },
            _ => null
        };

        if (packet is null)
        {
            NoticeDebugLog.Write($"SendBoardInteraction: unmapped BoardRequestType {requestType} -- not sent");

            return;
        }

        SendIfWorld(packet);
    }

    /// <summary>
    ///     Sends a chant line message.
    /// </summary>
    public void SendChant(string message) => SendIfWorld(new Cli.CastLinePacket { Line = message });

    /// <summary>
    ///     Sends a CreateGroupbox request with recruitment configuration.
    /// </summary>
    public void SendCreateGroupBox(
        string playerName,
        string name,
        string note,
        byte minLevel,
        byte maxLevel,
        byte maxWarriors,
        byte maxWizards,
        byte maxRogues,
        byte maxPriests,
        byte maxMonks)
        => SendIfWorld(
            Cli.GroupRequestPacket.Groupbox(
                playerName,
                name,
                note,
                minLevel,
                maxLevel,
                maxWarriors,
                maxWizards,
                maxRogues,
                maxPriests,
                maxMonks));

    /// <summary>
    ///     Sends a dialog interaction response (Next/Prev/Close, option select, text input).
    /// </summary>
    public void SendDialogResponse(
        byte objectType,
        uint entityId,
        ushort pursuitId,
        ushort dialogId,
        DialogArgsType argsType = DialogArgsType.None,
        byte? option = null,
        List<string>? args = null)
    {
        Cli.DialogUsePacket packet = argsType switch
        {
            DialogArgsType.MenuResponse => new Cli.DialogOptionResponsePacket
            {
                ObjectType = objectType,
                ObjectId = entityId,
                PursuitId = pursuitId,
                PursuitIndex = dialogId,
                Option = option ?? 0
            },
            DialogArgsType.TextResponse => new Cli.DialogTextResponsePacket
            {
                ObjectType = objectType,
                ObjectId = entityId,
                PursuitId = pursuitId,
                PursuitIndex = dialogId,
                Text = args is { Count: > 0 } ? string.Join("\t", args) : string.Empty
            },
            _ => new Cli.DialogNavigationPacket
            {
                ObjectType = objectType,
                ObjectId = entityId,
                PursuitId = pursuitId,
                PursuitIndex = dialogId
            }
        };

        SendIfWorld(packet);
    }

    /// <summary>
    ///     Sends the player's portrait and profile text to the server.
    /// </summary>
    public void SendEditableProfile(byte[] portraitData, string profileMessage)
        => SendIfWorld(
            new Cli.SetProfilePacket
            {
                PortraitData = portraitData,
                ProfileText = profileMessage
            });

    /// <summary>
    ///     Sends an emote request (body animation 9-44). The wire carries the zero-based emote index (animation - 9).
    /// </summary>
    public void SendEmote(BodyAnimation bodyAnimation)
    {
        var anim = (byte)bodyAnimation;
        var emoteIndex = anim >= 9 ? (byte)(anim - 9) : anim;

        SendIfWorld(new Cli.EmotePacket { EmoteIndex = emoteIndex });
    }

    /// <summary>
    ///     Sends an exchange interaction.
    /// </summary>
    public void SendExchangeInteraction(
        ExchangeRequestType type,
        uint otherId = 0,
        byte? sourceSlot = null,
        byte? itemCount = null,
        int? goldAmount = null)
    {
        Cli.ExchangePacket? packet = type switch
        {
            ExchangeRequestType.StartExchange => new Cli.StartExchangePacket { OtherUserId = otherId },
            ExchangeRequestType.AddItem => new Cli.AddExchangeItemPacket
            {
                OtherUserId = otherId,
                SourceSlot = sourceSlot ?? 0
            },
            ExchangeRequestType.AddStackableItem => new Cli.AddExchangeStackableItemPacket
            {
                OtherUserId = otherId,
                SourceSlot = sourceSlot ?? 0,
                ItemCount = itemCount ?? 0
            },
            ExchangeRequestType.SetGold => new Cli.SetExchangeGoldPacket
            {
                OtherUserId = otherId,
                GoldAmount = (uint)(goldAmount ?? 0)
            },
            ExchangeRequestType.Cancel => new Cli.CancelExchangePacket { OtherUserId = otherId },
            ExchangeRequestType.Accept => new Cli.AcceptExchangePacket { OtherUserId = otherId },
            _ => null
        };

        if (packet is null)
        {
            NoticeDebugLog.Write($"SendExchangeInteraction: unmapped ExchangeRequestType {type} -- not sent");

            return;
        }

        SendIfWorld(packet);
    }

    /// <summary>
    ///     Sends a group invite or group management action.
    /// </summary>
    public void SendGroupInvite(ClientGroupSwitch action, string? targetName = null)
    {
        var name = targetName ?? string.Empty;

        IClientPacket? packet = action switch
        {
            ClientGroupSwitch.TryInvite => Cli.GroupRequestPacket.TryInvite(name),
            ClientGroupSwitch.AcceptInvite => Cli.GroupRequestPacket.AcceptInvite(name),
            ClientGroupSwitch.RemoveGroupBox => Cli.GroupRequestPacket.RemoveGroupBox(name),
            ClientGroupSwitch.RequestToJoin => Cli.GroupRequestPacket.RecruitJoin(name),
            ClientGroupSwitch.ViewGroupBox => Cli.GroupRequestPacket.ViewRecruitInfo(name),
            _ => null
        };

        if (packet is null)
        {
            NoticeDebugLog.Write($"SendGroupInvite: unmapped ClientGroupSwitch {action} -- not sent");

            return;
        }

        SendIfWorld(packet);
    }

    /// <summary>
    ///     Requests the current ignore list from the server.
    /// </summary>
    public void SendIgnoreRequest() => SendIfWorld(Cli.IgnorePacket.Request());

    /// <summary>
    ///     Sends a menu interaction response (pursuit selection / text / option).
    /// </summary>
    public void SendMenuResponse(
        byte objectType,
        uint entityId,
        ushort pursuitId,
        byte? slot = null,
        string[]? args = null)
    {
        Cli.NpcMainMenuPacket packet;

        if (slot is { } slotValue)
            packet = new Cli.NpcOptionResponsePacket
            {
                ObjectType = objectType,
                ObjectId = entityId,
                PursuitId = pursuitId,
                Option = slotValue
            };
        else if (args is { Length: >= 2 })
            packet = new Cli.NpcTextPairResponsePacket
            {
                ObjectType = objectType,
                ObjectId = entityId,
                PursuitId = pursuitId,
                Name = args[0],
                Quantity = args[1]
            };
        else if (args is { Length: 1 })
            packet = new Cli.NpcTextResponsePacket
            {
                ObjectType = objectType,
                ObjectId = entityId,
                PursuitId = pursuitId,
                Text = args[0]
            };
        else
            packet = new Cli.NpcMainMenuSelectPacket
            {
                ObjectType = objectType,
                ObjectId = entityId,
                PursuitId = pursuitId
            };

        SendIfWorld(packet);
    }

    /// <summary>
    ///     Sends a metadata request to the server (checksums or specific file data).
    /// </summary>
    public void SendMetaDataRequest(MetaDataRequestType requestType, string? name = null)
        => Client.Send(
            requestType == MetaDataRequestType.AllCheckSums
                ? Cli.RequestMetafilePacket.AllCheckSums()
                : Cli.RequestMetafilePacket.ForName(name ?? string.Empty));

    /// <summary>
    ///     Toggles a user option (e.g. group allow, exchange allow, whisper settings).
    /// </summary>
    public void SendOptionToggle(UserOption option) => SendIfWorld(new Cli.SettingsPacket { SettingNumber = (byte)option });

    /// <summary>
    ///     Sends a public (normal) chat message visible to nearby players.
    /// </summary>
    public void SendPublicMessage(string message)
        => SendIfWorld(
            new Cli.TalkPacket
            {
                ChatType = Cli.ChatType.Say,
                Message = message
            });

    /// <summary>
    ///     Removes a player from the ignore list.
    /// </summary>
    public void SendRemoveIgnore(string targetName) => SendIfWorld(Cli.IgnorePacket.RemoveUser(targetName));

    /// <summary>
    ///     Sends notepad text for an editable notepad slot.
    /// </summary>
    public void SendSetNotepad(byte slot, string message)
        => SendIfWorld(
            new Cli.SetNotepadPacket
            {
                Slot = slot,
                Message = message
            });

    /// <summary>
    ///     Sends a shout message (! prefix) visible to all players on the map.
    /// </summary>
    public void SendShout(string message)
        => SendIfWorld(
            new Cli.TalkPacket
            {
                ChatType = Cli.ChatType.Shout,
                Message = message
            });

    /// <summary>
    ///     Sends a social status change to the server.
    /// </summary>
    public void SendSocialStatus(SocialStatus status) => SendIfWorld(new Cli.StatusPacket { Status = (byte)status });

    /// <summary>
    ///     Sends a whisper to a specific player.
    /// </summary>
    public void SendWhisper(string targetName, string message)
        => SendIfWorld(
            new Cli.WhisperPacket
            {
                Target = targetName,
                Message = message
            });

    /// <summary>
    ///     Sends a spacebar (assail) request.
    /// </summary>
    public void Spacebar() => SendIfWorld(new Cli.AttackPacket());

    /// <summary>
    ///     Sends a swap slot request between two panel positions.
    /// </summary>
    public void SwapSlot(PanelType panelType, byte slot1, byte slot2)
        => SendIfWorld(
            new Cli.SwapSlotPacket
            {
                Window = (byte)panelType,
                Slot1 = slot1,
                Slot2 = slot2
            });

    /// <summary>
    ///     Toggles group membership (join/leave the current group).
    /// </summary>
    public void ToggleGroup() => SendIfWorld(new Cli.GroupTogglePacket());

    /// <summary>
    ///     Sends a turn request to face the specified direction.
    /// </summary>
    public void Turn(Direction direction) => SendIfWorld(new Cli.TurnPacket { Direction = ToDalibDirection(direction) });

    /// <summary>
    ///     Sends an unequip request for the specified equipment slot.
    /// </summary>
    public void Unequip(EquipmentSlot slot) => SendIfWorld(new Cli.UnequipPacket { Slot = (byte)slot });

    /// <summary>
    ///     Sends an item use request (equip, consume).
    /// </summary>
    public void UseItem(byte slot) => SendIfWorld(new Cli.UseItemPacket { Slot = slot });

    /// <summary>
    ///     Sends a skill use request.
    /// </summary>
    public void UseSkill(byte slot) => SendIfWorld(new Cli.UseSkillPacket { Slot = slot });

    /// <summary>
    ///     Sends a spell use request, optionally carrying raw targeting bytes.
    /// </summary>
    public void UseSpell(byte slot, byte[]? argsData = null)
        => SendIfWorld(
            new Cli.UseSpellPacket
            {
                Slot = slot,
                Args = argsData ?? []
            });

    /// <summary>
    ///     Sends a targeted spell cast at a specific entity and position.
    /// </summary>
    public void UseSpellOnTarget(
        byte slot,
        uint targetId,
        int targetX,
        int targetY)
        => SendIfWorld(Cli.UseSpellPacket.Targeted(slot, targetId, (ushort)targetX, (ushort)targetY));

    /// <summary>
    ///     Sends a walk request in the specified direction.
    /// </summary>
    public void Walk(Direction direction)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new Cli.WalkPacket
            {
                Direction = ToDalibDirection(direction),
                Sequence = WalkStepCount++
            });
    }

    #region Private
    private ushort LobbyClientVersion;
    private bool PendingLobbyVersion;
    private ConnectionState PendingTargetState;
    private byte WalkStepCount;

    private static DALib.Enums.Direction ToDalibDirection(Direction direction)
        => direction switch
        {
            Direction.Up    => DALib.Enums.Direction.North,
            Direction.Right => DALib.Enums.Direction.East,
            Direction.Down  => DALib.Enums.Direction.South,
            Direction.Left  => DALib.Enums.Direction.West,
            _               => DALib.Enums.Direction.North
        };

    private static Direction ToChaosDirection(DALib.Enums.Direction direction)
        => direction switch
        {
            DALib.Enums.Direction.North => Direction.Up,
            DALib.Enums.Direction.East  => Direction.Right,
            DALib.Enums.Direction.South => Direction.Down,
            DALib.Enums.Direction.West  => Direction.Left,
            _                           => Direction.Up
        };

    private static DALib.Enums.Gender ToDalibGender(Gender gender)
        => gender switch
        {
            Gender.Male   => DALib.Enums.Gender.Male,
            Gender.Female => DALib.Enums.Gender.Female,
            _             => DALib.Enums.Gender.Neutral
        };

    //retail 0x47 single-bit stat selector: Str=0x01, Dex=0x02, Int=0x04, Wis=0x08, Con=0x10.
    private static byte StatSelector(Stat stat)
        => stat switch
        {
            Stat.STR => 0x01,
            Stat.DEX => 0x02,
            Stat.INT => 0x04,
            Stat.WIS => 0x08,
            Stat.CON => 0x10,
            _        => 0x00
        };

    private void IndexHandlers()
    {
        //0x1F (old MapChangeComplete) is deliberately unregistered: HandleMapInfo synthesizes that
        //entry flag unconditionally, and Hybrasyl never emits 0x1F (DALib names it ChangeWeather).

        //lobby
        PacketHandlers[(byte)ServerOpcode.AcceptConnection] = HandleAcceptConnection;
        PacketHandlers[(byte)ServerOpcode.CryptoKey] = HandleConnectionInfo;
        PacketHandlers[(byte)ServerOpcode.ServerTableData] = HandleServerTableResponse;
        PacketHandlers[(byte)ServerOpcode.Redirect] = HandleRedirect;

        //login
        PacketHandlers[(byte)ServerOpcode.LoginMessage] = HandleLoginMessage;
        PacketHandlers[(byte)ServerOpcode.LoginNotification] = HandleLoginNotice;
        PacketHandlers[(byte)ServerOpcode.Url] = HandleLoginControl;

        //world entry
        PacketHandlers[(byte)ServerOpcode.UserAppearance] = HandleUserId;
        PacketHandlers[(byte)ServerOpcode.MapInfo] = HandleMapInfo;
        PacketHandlers[(byte)ServerOpcode.MapData] = HandleMapData;
        PacketHandlers[(byte)ServerOpcode.MapLoadComplete] = HandleMapLoadComplete;
        PacketHandlers[(byte)ServerOpcode.Location] = HandleLocation;
        PacketHandlers[(byte)ServerOpcode.Attributes] = HandleAttributes;
        PacketHandlers[(byte)ServerOpcode.DrawObjects] = HandleDisplayVisibleEntities;
        PacketHandlers[(byte)ServerOpcode.DisplayUser] = HandleDisplayAisling;

        //world entities
        PacketHandlers[(byte)ServerOpcode.RemoveObject] = HandleRemoveEntity;
        PacketHandlers[(byte)ServerOpcode.CreatureWalk] = HandleCreatureWalk;
        PacketHandlers[(byte)ServerOpcode.ConfirmWalk] = HandleClientWalkResponse;
        PacketHandlers[(byte)ServerOpcode.CreatureTurn] = HandleCreatureTurn;

        //chat / messages
        PacketHandlers[(byte)ServerOpcode.SystemMessage] = HandleServerMessage;
        PacketHandlers[(byte)ServerOpcode.PublicMessage] = HandleDisplayPublicMessage;

        //inventory
        PacketHandlers[(byte)ServerOpcode.AddItem] = HandleAddItemToPane;
        PacketHandlers[(byte)ServerOpcode.RemoveItem] = HandleRemoveItemFromPane;

        //skills / spells
        PacketHandlers[(byte)ServerOpcode.AddSkill] = HandleAddSkillToPane;
        PacketHandlers[(byte)ServerOpcode.RemoveSkill] = HandleRemoveSkillFromPane;
        PacketHandlers[(byte)ServerOpcode.AddSpell] = HandleAddSpellToPane;
        PacketHandlers[(byte)ServerOpcode.RemoveSpell] = HandleRemoveSpellFromPane;

        //equipment
        PacketHandlers[(byte)ServerOpcode.AddEquipment] = HandleEquipment;
        PacketHandlers[(byte)ServerOpcode.RemoveEquipment] = HandleDisplayUnequip;

        //visual / audio
        PacketHandlers[(byte)ServerOpcode.HealthBar] = HandleHealthBar;
        PacketHandlers[(byte)ServerOpcode.PlaySound] = HandleSound;
        PacketHandlers[(byte)ServerOpcode.PlayerAnimation] = HandleBodyAnimation;
        PacketHandlers[(byte)ServerOpcode.SpellAnimation] = HandleAnimation;
        PacketHandlers[(byte)ServerOpcode.Cooldown] = HandleCooldown;
        PacketHandlers[(byte)ServerOpcode.StatusBar] = HandleEffect;

        //world state
        PacketHandlers[(byte)ServerOpcode.LightLevel] = HandleLightLevel;
        PacketHandlers[(byte)ServerOpcode.Door] = HandleDoor;
        PacketHandlers[(byte)ServerOpcode.Refresh] = HandleRefreshResponse;
        PacketHandlers[(byte)ServerOpcode.MapChangePending] = HandleMapChangePending;

        //npc interaction
        PacketHandlers[(byte)ServerOpcode.NpcMenu] = HandleDisplayMenu;
        PacketHandlers[(byte)ServerOpcode.NpcDialog] = HandleDisplayDialog;
        PacketHandlers[(byte)ServerOpcode.Board] = HandleDisplayBoard;
        PacketHandlers[(byte)ServerOpcode.Exchange] = HandleDisplayExchange;
        PacketHandlers[(byte)ServerOpcode.Group] = HandleDisplayGroupInvite;

        //profiles / lists
        PacketHandlers[(byte)ServerOpcode.RequestPortrait] = HandleEditableProfileRequest;
        PacketHandlers[(byte)ServerOpcode.SelfProfile] = HandleSelfProfile;
        PacketHandlers[(byte)ServerOpcode.Profile] = HandleOtherProfile;
        PacketHandlers[(byte)ServerOpcode.UserList] = HandleWorldList;
        PacketHandlers[(byte)ServerOpcode.WorldMap] = HandleWorldMap;

        //notepads
        PacketHandlers[(byte)ServerOpcode.EditablePaper] = HandleDisplayEditableNotepad;
        PacketHandlers[(byte)ServerOpcode.ReadonlyPaper] = HandleDisplayReadonlyNotepad;

        //misc
        PacketHandlers[(byte)ServerOpcode.ConfirmExit] = HandleExitResponse;
        PacketHandlers[(byte)ServerOpcode.Bounce] = HandleForceClientPacket;
        PacketHandlers[(byte)ServerOpcode.CancelCast] = HandleCancelCasting;
        PacketHandlers[(byte)ServerOpcode.Metafile] = HandleMetaData;
    }

    private void HandlePacket(IServerPacket pkt)
    {
        var handler = PacketHandlers[pkt.Opcode];
        NoticeDebugLog.Write($"inbound opcode=0x{pkt.Opcode:X2} handled={handler is not null} state={State}");
        handler?.Invoke(pkt);
    }

    private void HandleAcceptConnection(IServerPacket _)
    {
        if (PendingLobbyVersion)
        {
            //lobby handshake — send version packet
            PendingLobbyVersion = false;
            State = PendingTargetState;

            Client.Send(new Cli.VersionPacket { Version = LobbyClientVersion });
        }

        //redirected connections send clientjoin in FollowPendingRedirect immediately after connecting,
        //without waiting for acceptconnection.
    }

    private void HandleConnectionInfo(IServerPacket p)
    {
        NoticeDebugLog.Write($"HandleConnectionInfo enter state={State}");
        var pkt = (Server.CryptoKeyPacket)p;
        NoticeDebugLog.Write($"  deserialized Seed={pkt.Seed} Key.Length={pkt.Key?.Length}");

        //lobby always uses empty keysaltseed (crypto falls back to "default")
        Client.ApplyCryptoKey(pkt.Seed, pkt.Key!, null);
        NoticeDebugLog.Write("  crypto set");

        //crypto is now configured — safe to request the server table
        if (State == ConnectionState.Lobby)
        {
            NoticeDebugLog.Write("  calling RequestServerTable");
            RequestServerTable();
        }
    }

    private void HandleServerTableResponse(IServerPacket p)
    {
        NoticeDebugLog.Write("HandleServerTableResponse enter");
        var pkt = (Server.ServerTableDataPacket)p;
        NoticeDebugLog.Write($"  parsed {pkt.Servers.Count} servers");
        OnServerTableReceived?.Invoke(pkt.Servers);
    }

    private void HandleRedirect(IServerPacket p)
    {
        var pkt = (Server.RedirectPacket)p;

        //determine target state based on current state. A World-state redirect is ambiguous: it is either a
        //logout (we sent ClientExit confirm → back to Login) or a server-initiated world transfer such as
        //Temuair↔Medenia (→ straight back into World, using the character-name key salt). Distinguish by whether
        //we asked to quit.
        var targetState = State switch
        {
            ConnectionState.Lobby => ConnectionState.Login,
            ConnectionState.Login => ConnectionState.World,
            ConnectionState.World => ExitConfirmed ? ConnectionState.Login : ConnectionState.World,
            _                     => ConnectionState.Login
        };

        //consumed: any redirect resolves the pending quit. Reaching World again always passes through a
        //Login→World redirect, so a stale flag can never leak into a later world transfer.
        ExitConfirmed = false;

        PendingRedirect = new RedirectInfo(
            new IPEndPoint(pkt.IpAddress, pkt.Port),
            pkt.EncryptionSeed,
            pkt.EncryptionKey,
            pkt.Name,
            pkt.RedirectId,
            targetState);

        //order matters: fire the redirect event BEFORE Disconnect. Disconnect synchronously fires
        //OnDisconnected → State setter → StateChanged, and the world-screen handler suppresses its
        //"Connection lost" popup based on a flag set by OnRedirectReceived. Inverting this order
        //races the popup against the redirect flag.
        OnRedirectReceived?.Invoke(PendingRedirect.Value);

        //begin teardown — the redirect will be followed from the game loop once the old connection is fully dead.
        Client.Disconnect();
    }

    private void HandleLoginMessage(IServerPacket p) => OnLoginMessage?.Invoke((Server.LoginMessagePacket)p);

    private void HandleLoginNotice(IServerPacket p) => OnLoginNotice?.Invoke((Server.LoginNotificationPacket)p);

    private void HandleLoginControl(IServerPacket p) => OnLoginControl?.Invoke((Server.UrlPacket)p);

    private void HandleUserId(IServerPacket p)
    {
        var pkt = (Server.UserAppearancePacket)p;
        AislingId = pkt.Id;
        OnUserId?.Invoke(pkt.Id);
        EntryState |= WorldEntryState.UserId;
        CheckWorldEntryComplete();
    }

    private void HandleMapInfo(IServerPacket p)
    {
        // Hybrasyl does not send MapChangePending, MapLoadComplete, or MapChangeComplete.
        // Synthesize the full lifecycle around the single MapInfo packet: fire pending first so
        // UI (world map, town map, pathfinding) is torn down, then the normal MapInfo event, then
        // the implicit load/change completion so OnWorldEntryComplete can fire.
        SynthesizeMapChangePending();

        var pkt = (Server.MapInfoPacket)p;
        MapInfo = pkt;
        EntryState |= WorldEntryState.MapInfo;
        OnMapInfo?.Invoke(pkt);

        EntryState |= WorldEntryState.MapLoaded | WorldEntryState.MapChangeComplete;
        OnMapLoadComplete?.Invoke();
        CheckWorldEntryComplete();
    }

    private void HandleMapData(IServerPacket p) => OnMapData?.Invoke((Server.MapDataPacket)p);

    private void HandleMapLoadComplete(IServerPacket _)
    {
        EntryState |= WorldEntryState.MapLoaded;
        OnMapLoadComplete?.Invoke();
        CheckWorldEntryComplete();
    }

    private void HandleLocation(IServerPacket p)
    {
        var pkt = (Server.LocationPacket)p;
        PlayerX = pkt.X;
        PlayerY = pkt.Y;
        EntryState |= WorldEntryState.Location;
        OnLocationChanged?.Invoke(pkt.X, pkt.Y);
        CheckWorldEntryComplete();
    }

    private void HandleAttributes(IServerPacket p)
    {
        var pkt = (Server.AttributesPacket)p;

        //fire the raw partial packet; the accumulate-partial-updates merge lives in PlayerAttributes now.
        OnAttributes?.Invoke(pkt);
        EntryState |= WorldEntryState.Attributes;
        CheckWorldEntryComplete();
    }

    private void HandleDisplayVisibleEntities(IServerPacket p) => OnDisplayVisibleEntities?.Invoke((Server.DrawObjectsPacket)p);

    private void HandleDisplayAisling(IServerPacket p) => OnDisplayAisling?.Invoke((Server.DisplayUserPacket)p);

    private void CheckWorldEntryComplete()
    {
        if (State != ConnectionState.World)
            return;

        if (!EntryState.HasFlag(WorldEntryState.AllRequired))
            return;

        //clear the flag so we don't fire again until the next world entry
        EntryState = WorldEntryState.None;
        OnWorldEntryComplete?.Invoke();
    }

    private void HandleRemoveEntity(IServerPacket p)
    {
        var pkt = (Server.RemoveObjectPacket)p;
        OnRemoveEntity?.Invoke(pkt.SourceId);
    }

    private void HandleCreatureWalk(IServerPacket p)
    {
        var pkt = (Server.CreatureWalkPacket)p;
        OnCreatureWalk?.Invoke(pkt.SourceId, pkt.OldX, pkt.OldY, ToChaosDirection(pkt.Direction));
    }

    private void HandleClientWalkResponse(IServerPacket p)
    {
        var pkt = (Server.ConfirmWalkPacket)p;
        OnClientWalkResponse?.Invoke(ToChaosDirection(pkt.Direction), pkt.OldX, pkt.OldY);
    }

    private void HandleCreatureTurn(IServerPacket p)
    {
        var pkt = (Server.CreatureTurnPacket)p;
        OnCreatureTurn?.Invoke(pkt.SourceId, ToChaosDirection(pkt.Direction));
    }

    //--- chat / messages ---

    private void HandleServerMessage(IServerPacket p) => OnServerMessage?.Invoke((Server.SystemMessagePacket)p);

    private void HandleDisplayPublicMessage(IServerPacket p) => OnDisplayPublicMessage?.Invoke((Server.PublicMessagePacket)p);

    //--- inventory ---

    private void HandleAddItemToPane(IServerPacket p) => OnAddItemToPane?.Invoke((Server.AddItemPacket)p);

    private void HandleRemoveItemFromPane(IServerPacket p) => OnRemoveItemFromPane?.Invoke((Server.RemoveItemPacket)p);

    //--- skills / spells ---

    private void HandleAddSkillToPane(IServerPacket p) => OnAddSkillToPane?.Invoke((Server.AddSkillPacket)p);

    private void HandleRemoveSkillFromPane(IServerPacket p) => OnRemoveSkillFromPane?.Invoke((Server.RemoveSkillPacket)p);

    private void HandleAddSpellToPane(IServerPacket p) => OnAddSpellToPane?.Invoke((Server.AddSpellPacket)p);

    private void HandleRemoveSpellFromPane(IServerPacket p) => OnRemoveSpellFromPane?.Invoke((Server.RemoveSpellPacket)p);

    //--- equipment ---

    private void HandleEquipment(IServerPacket p) => OnEquipment?.Invoke((Server.AddEquipmentPacket)p);

    private void HandleDisplayUnequip(IServerPacket p) => OnDisplayUnequip?.Invoke((Server.RemoveEquipmentPacket)p);

    //--- visual / audio ---

    private void HandleHealthBar(IServerPacket p) => OnHealthBar?.Invoke((Server.HealthBarPacket)p);

    private void HandleSound(IServerPacket p) => OnSound?.Invoke((Server.PlaySoundPacket)p);

    private void HandleBodyAnimation(IServerPacket p) => OnBodyAnimation?.Invoke((Server.PlayerAnimationPacket)p);

    private void HandleAnimation(IServerPacket p) => OnAnimation?.Invoke((Server.SpellAnimationPacket)p);

    private void HandleCooldown(IServerPacket p) => OnCooldown?.Invoke((Server.CooldownPacket)p);

    private void HandleEffect(IServerPacket p) => OnEffect?.Invoke((Server.StatusBarPacket)p);

    //--- world state ---

    private void HandleLightLevel(IServerPacket p) => OnLightLevel?.Invoke((Server.LightLevelPacket)p);

    private void HandleDoor(IServerPacket p) => OnDoor?.Invoke((Server.DoorPacket)p);

    private void HandleRefreshResponse(IServerPacket _) => OnRefreshResponse?.Invoke();

    private void HandleMapChangePending(IServerPacket _) => OnMapChangePending?.Invoke();

    // Synthesize a MapChangePending signal for Hybrasyl: fire it before HandleMapInfo's
    // implicit completion so UI cleanup (world-map hide, pathfinding clear) runs first.
    private void SynthesizeMapChangePending() => OnMapChangePending?.Invoke();

    //--- npc interaction ---

    private void HandleDisplayMenu(IServerPacket p) => OnDisplayMenu?.Invoke((Server.NpcMenuPacket)p);

    private void HandleDisplayDialog(IServerPacket p) => OnDisplayDialog?.Invoke((Server.NpcDialogPacket)p);

    private void HandleDisplayBoard(IServerPacket p) => OnDisplayBoard?.Invoke((Server.BoardResponsePacket)p);

    private void HandleDisplayExchange(IServerPacket p) => OnDisplayExchange?.Invoke((Server.ExchangeResponsePacket)p);

    private void HandleDisplayGroupInvite(IServerPacket p) => OnDisplayGroupInvite?.Invoke((Server.GroupResponsePacket)p);

    //--- profiles / lists ---

    private void HandleEditableProfileRequest(IServerPacket _) => OnEditableProfileRequest?.Invoke();

    private void HandleSelfProfile(IServerPacket p) => OnSelfProfile?.Invoke((Server.SelfProfilePacket)p);

    private void HandleOtherProfile(IServerPacket p) => OnOtherProfile?.Invoke((Server.ProfilePacket)p);

    private void HandleWorldList(IServerPacket p) => OnWorldList?.Invoke((Server.UserListPacket)p);

    private void HandleWorldMap(IServerPacket p) => OnWorldMap?.Invoke((Server.WorldMapPacket)p);

    //--- notepads ---

    private void HandleDisplayEditableNotepad(IServerPacket p) => OnDisplayEditableNotepad?.Invoke((Server.EditablePaperPacket)p);

    private void HandleDisplayReadonlyNotepad(IServerPacket p) => OnDisplayReadonlyNotepad?.Invoke((Server.ReadonlyPaperPacket)p);

    //--- misc ---

    private void HandleExitResponse(IServerPacket p) => OnExitResponse?.Invoke((Server.ConfirmExitPacket)p);

    private void HandleForceClientPacket(IServerPacket p)
    {
        //re-emit the inner forced packet exactly as the server instructed.
        var pkt = (Server.BouncePacket)p;
        Client.Send(new RawClientPacket((byte)pkt.ClientOpcode, pkt.Data));
    }

    private void HandleCancelCasting(IServerPacket _) => OnCancelCasting?.Invoke();

    private void HandleMetaData(IServerPacket p) => OnMetaData?.Invoke((Server.MetafilePacket)p);

    private void HandleDisconnected()
    {
        if (State != ConnectionState.Connecting)
            State = ConnectionState.Disconnected;
    }
    #endregion
}

/// <summary>
///     What transport one completed hop ended up on.
/// </summary>
/// <param name="Server">
///     The identity the hop authenticated as — the name a certificate was checked against and a pin
///     looked up under, which is the lobby's host for every hop of an ordinary session.
/// </param>
/// <param name="Encrypted">Whether the hop upgraded to TLS and negotiated a dialect.</param>
/// <param name="Target">The connection state this hop is entering.</param>
public readonly record struct TransportEstablished(string Server, bool Encrypted, ConnectionState Target);

/// <summary>
///     Contains redirect information received from the server.
/// </summary>
public readonly record struct RedirectInfo(
    IPEndPoint EndPoint,
    byte Seed,
    byte[] Key,
    string Name,
    uint Id,
    ConnectionState TargetState);

/// <summary>
///     A pass-through client packet carrying a raw, pre-built body for a given opcode. Used to re-emit the inner packet of
///     a server 0x4B Bounce (<see cref="DALib.Networking.Packets.Server.BouncePacket" />), whose body was authored by the
///     server.
/// </summary>
internal sealed record RawClientPacket(byte ForcedOpcode, byte[] Body) : ClientPacket
{
    public override byte Opcode => ForcedOpcode;

    public override void WriteBody(IPacketWriter writer) => writer.WriteBytes(Body);
}

