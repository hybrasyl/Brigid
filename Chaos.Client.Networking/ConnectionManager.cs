#region
using System.Buffers;
using System.Net;
using System.Text;
using Chaos.Cryptography;
using Chaos.DarkAges.Definitions;
using Chaos.Geometry;
using Chaos.Geometry.Abstractions.Definitions;
using Chaos.IO.Memory;
using Chaos.Networking.Abstractions.Definitions;
using Chaos.Networking.Entities.Client;
using Chaos.Networking.Entities.Server;
using Chaos.Packets;
using Chaos.Packets.Abstractions;
#endregion

namespace Chaos.Client.Networking;

/// <summary>
///     Manages the connection lifecycle: Lobby handshake, login redirect, and world entry. Drives state transitions and
///     orchestrates the GameClient through each phase.
/// </summary>
public sealed class ConnectionManager : IDisposable
{
    private readonly Action<ServerPacket>?[] PacketHandlers = new Action<ServerPacket>?[byte.MaxValue + 1];
    private WorldEntryState EntryState;
    private RedirectInfo? PendingRedirect;

    /// <summary>
    ///     The local player's unique entity ID, assigned by the server upon world entry.
    /// </summary>
    public uint AislingId { get; private set; }

    /// <summary>
    ///     The name of the logged-in character, populated when <see cref="Login" /> is called.
    /// </summary>
    public string AislingName { get; private set; } = string.Empty;

    /// <summary>
    ///     The player's latest merged attribute snapshot. Partial server updates are merged so this always reflects the full state.
    /// </summary>
    public AttributesArgs? Attributes { get; private set; }

    /// <summary>
    ///     The current map's metadata (map ID, dimensions, flags), updated on each map change.
    /// </summary>
    public MapInfoArgs? MapInfo { get; private set; }

    /// <summary>
    ///     The player's current X tile coordinate, updated on walk confirmations and location packets.
    /// </summary>
    public int PlayerX { get; private set; }

    /// <summary>
    ///     The player's current Y tile coordinate, updated on walk confirmations and location packets.
    /// </summary>
    public int PlayerY { get; private set; }

    /// <summary>
    ///     The name of the server the client is currently connected to.
    /// </summary>
    public string ServerName { get; set; } = string.Empty;

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

    private void SendIfWorld<T>(T args) where T : IPacketSerializable
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(args);
    }

    /// <inheritdoc />
    public void Dispose() => Client.Dispose();

    /// <summary>
    ///     Sends a password change request to the login server.
    /// </summary>
    /// <param name="name">The account name.</param>
    /// <param name="currentPassword">The current password.</param>
    /// <param name="newPassword">The new password.</param>
    public void ChangePassword(string name, string currentPassword, string newPassword)
    {
        if (State != ConnectionState.Login)
            return;

        Client.Send(
            new PasswordChangeArgs
            {
                Name = name,
                CurrentPassword = currentPassword,
                NewPassword = newPassword
            });
    }

    /// <summary>
    ///     Sends a click on an entity (NPC, creature, aisling, ground item).
    /// </summary>
    public void ClickEntity(uint targetId)
        => SendIfWorld(
            new ClickArgs
            {
                ClickType = ClickType.TargetId,
                TargetId = targetId
            });

    /// <summary>
    ///     Sends a click on a map tile.
    /// </summary>
    public void ClickTile(int x, int y)
        => SendIfWorld(
            new ClickArgs
            {
                ClickType = ClickType.TargetPoint,
                TargetPoint = new Point(x, y)
            });

    /// <summary>
    ///     Sends a world map node click.
    /// </summary>
    public void ClickWorldMapNode(
        ushort mapId,
        int x,
        int y,
        ushort checkSum)
        => SendIfWorld(
            new WorldMapClickArgs
            {
                MapId = mapId,
                Point = new Point(x, y),
                CheckSum = checkSum
            });

    /// <summary>
    ///     Connects to the lobby server, performs the Version/ConnectionInfo handshake.
    /// </summary>
    /// <param name="host">The lobby server hostname or IP address.</param>
    /// <param name="port">The lobby server port.</param>
    /// <param name="clientVersion">The client version to send during handshake.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ConnectToLobbyAsync(
        string host,
        int port,
        ushort clientVersion,
        CancellationToken ct = default)
    {
        State = ConnectionState.Connecting;
        Client.Crypto = new Crypto();
        Client.SetSequence(0);

        //set all acceptconnection handler state before connectasync starts the receive loop.
        //the receive loop starts inside connectasync and acceptconnection can arrive immediately,
        //so the handler must have everything it needs before the socket connects.
        LobbyClientVersion = clientVersion;
        PendingLobbyVersion = true;
        PendingTargetState = ConnectionState.Lobby;

        try
        {
            await Client.ConnectAsync(host, port, ct);
        } catch (Exception ex)
        {
            PendingLobbyVersion = false;
            State = ConnectionState.Disconnected;
            OnError?.Invoke($"Failed to connect to lobby: {ex.Message}");
        }
    }

    /// <summary>
    ///     Sends the finalized character creation request (appearance choices) to the login server.
    /// </summary>
    /// <param name="hairStyle">The selected hair style index.</param>
    /// <param name="gender">The selected gender.</param>
    /// <param name="hairColor">The selected hair color.</param>
    public void CreateCharFinalize(byte hairStyle, Gender gender, DisplayColor hairColor)
    {
        if (State != ConnectionState.Login)
            return;

        Client.Send(
            new CreateCharFinalizeArgs
            {
                HairStyle = hairStyle,
                Gender = gender,
                HairColor = hairColor
            });
    }

    /// <summary>
    ///     Sends the initial character creation request (name and password) to the login server.
    /// </summary>
    /// <param name="name">The character name.</param>
    /// <param name="password">The account password.</param>
    public void CreateCharInitial(string name, string password)
    {
        if (State != ConnectionState.Login)
            return;

        Client.Send(
            new CreateCharInitialArgs
            {
                Name = name,
                Password = password
            });
    }

    /// <summary>
    ///     Sends a gold drop request onto the ground.
    /// </summary>
    /// <param name="amount">The amount of gold to drop.</param>
    /// <param name="x">The destination tile X coordinate.</param>
    /// <param name="y">The destination tile Y coordinate.</param>
    public void DropGold(int amount, int x, int y)
        => SendIfWorld(
            new GoldDropArgs
            {
                Amount = amount,
                DestinationPoint = new Point(x, y)
            });

    /// <summary>
    ///     Sends a gold give request to a creature/NPC.
    /// </summary>
    /// <param name="amount">The amount of gold to give.</param>
    /// <param name="targetId">The target entity ID.</param>
    public void DropGoldOnCreature(int amount, uint targetId)
        => SendIfWorld(
            new GoldDroppedOnCreatureArgs
            {
                Amount = amount,
                TargetId = targetId
            });

    /// <summary>
    ///     Sends an item drop request onto the ground.
    /// </summary>
    /// <param name="sourceSlot">The inventory slot of the item to drop.</param>
    /// <param name="x">The destination tile X coordinate.</param>
    /// <param name="y">The destination tile Y coordinate.</param>
    /// <param name="count">The number of items to drop (for stackable items).</param>
    public void DropItem(
        byte sourceSlot,
        int x,
        int y,
        int count = 1)
        => SendIfWorld(
            new ItemDropArgs
            {
                SourceSlot = sourceSlot,
                DestinationPoint = new Point(x, y),
                Count = count
            });

    /// <summary>
    ///     Sends an item give request to a creature/NPC.
    /// </summary>
    /// <param name="sourceSlot">The inventory slot of the item to give.</param>
    /// <param name="targetId">The target entity ID.</param>
    /// <param name="count">The number of items to give (for stackable items).</param>
    public void DropItemOnCreature(byte sourceSlot, uint targetId, byte count = 1)
        => SendIfWorld(
            new ItemDroppedOnCreatureArgs
            {
                SourceSlot = sourceSlot,
                TargetId = targetId,
                Count = count
            });

    private void FollowPendingRedirect()
    {
        if (PendingRedirect is not { } redirect)
            return;

        PendingRedirect = null;

        State = ConnectionState.Connecting;

        //lobby→login uses empty keysaltseed (falls back to "default"), login→world uses character name
        var keySaltSeed = redirect.TargetState == ConnectionState.Login ? string.Empty : redirect.Name;
        Client.Crypto = new Crypto(redirect.Seed, redirect.Key, keySaltSeed);
        Client.SetSequence(0);
        EntryState = WorldEntryState.None;
        PendingTargetState = redirect.TargetState;

        try
        {
            Client.Connect(redirect.EndPoint.Address.ToString(), redirect.EndPoint.Port);
        } catch (Exception ex)
        {
            State = ConnectionState.Disconnected;
            OnError?.Invoke($"Failed to connect after redirect: {ex.Message}");

            return;
        }

        //send clientredirected immediately after connecting.
        //not all servers send acceptconnection before expecting this packet.
        State = PendingTargetState;

        Client.Send(
            new ClientRedirectedArgs
            {
                Id = redirect.Id,
                Seed = redirect.Seed,
                Key = redirect.Key,
                Name = redirect.Name
            });

        if (State == ConnectionState.Login)
            RequestHomepage();
    }

    /// <summary>
    ///     Sends login credentials to the login server.
    /// </summary>
    /// <param name="name">The character name.</param>
    /// <param name="password">The account password.</param>
    /// <param name="clientId1">First client identification value.</param>
    /// <param name="clientId2">Second client identification value.</param>
    public void Login(
        string name,
        string password,
        uint clientId1 = 1,
        uint clientId2 = 1)
    {
        if (State != ConnectionState.Login)
            return;

        AislingName = name;

        Client.Send(
            new LoginArgs
            {
                Name = name,
                Password = password,
                ClientId1 = clientId1,
                ClientId2 = clientId2,
                IsValid = true
            });
    }

    //--- inventory ---

    /// <summary>
    ///     Fired when an item is added to the inventory pane.
    /// </summary>
    public event AddItemToPaneHandler? OnAddItemToPane;

    //--- skills / spells ---

    /// <summary>
    ///     Fired when a skill is added to the skill pane.
    /// </summary>
    public event AddSkillToPaneHandler? OnAddSkillToPane;

    /// <summary>
    ///     Fired when a spell is added to the spell pane.
    /// </summary>
    public event AddSpellToPaneHandler? OnAddSpellToPane;

    /// <summary>
    ///     Fired when a spell/effect animation should play.
    /// </summary>
    public event AnimationHandler? OnAnimation;

    /// <summary>
    ///     Fired when player attributes are updated.
    /// </summary>
    public event AttributesHandler? OnAttributes;

    /// <summary>
    ///     Fired when a body animation is triggered on an entity.
    /// </summary>
    public event BodyAnimationHandler? OnBodyAnimation;

    /// <summary>
    ///     Fired when a casting animation should be cancelled.
    /// </summary>
    public event CancelCastingHandler? OnCancelCasting;

    /// <summary>
    ///     Fired when the server confirms the player's own walk. Args: (direction, oldX, oldY).
    /// </summary>
    public event ClientWalkResponseHandler? OnClientWalkResponse;

    /// <summary>
    ///     Fired when a skill or spell cooldown starts.
    /// </summary>
    public event CooldownHandler? OnCooldown;

    /// <summary>
    ///     Fired when another entity changes facing direction. Args: (sourceId, direction).
    /// </summary>
    public event CreatureTurnHandler? OnCreatureTurn;

    /// <summary>
    ///     Fired when another entity walks. Args: (sourceId, oldX, oldY, direction).
    /// </summary>
    public event CreatureWalkHandler? OnCreatureWalk;

    /// <summary>
    ///     Fired when an aisling display is received.
    /// </summary>
    public event DisplayAislingHandler? OnDisplayAisling;

    /// <summary>
    ///     Fired when a bulletin board should be displayed.
    /// </summary>
    public event DisplayBoardHandler? OnDisplayBoard;

    /// <summary>
    ///     Fired when an NPC dialog should be displayed.
    /// </summary>
    public event DisplayDialogHandler? OnDisplayDialog;

    /// <summary>
    ///     Fired when an editable notepad should be displayed.
    /// </summary>
    public event DisplayEditableNotepadHandler? OnDisplayEditableNotepad;

    /// <summary>
    ///     Fired when an exchange/trade window should be displayed.
    /// </summary>
    public event DisplayExchangeHandler? OnDisplayExchange;

    /// <summary>
    ///     Fired when a group invite is received.
    /// </summary>
    public event DisplayGroupInviteHandler? OnDisplayGroupInvite;

    //--- npc interaction ---

    /// <summary>
    ///     Fired when an NPC menu should be displayed.
    /// </summary>
    public event DisplayMenuHandler? OnDisplayMenu;

    /// <summary>
    ///     Fired when a public chat message is displayed.
    /// </summary>
    public event DisplayPublicMessageHandler? OnDisplayPublicMessage;

    /// <summary>
    ///     Fired when a read-only notepad should be displayed.
    /// </summary>
    public event DisplayReadonlyNotepadHandler? OnDisplayReadonlyNotepad;

    /// <summary>
    ///     Fired when an equipment slot is cleared.
    /// </summary>
    public event DisplayUnequipHandler? OnDisplayUnequip;

    /// <summary>
    ///     Fired when a visible entity (non-aisling) is received.
    /// </summary>
    public event DisplayVisibleEntitiesHandler? OnDisplayVisibleEntities;

    /// <summary>
    ///     Fired when door states are updated.
    /// </summary>
    public event DoorHandler? OnDoor;

    /// <summary>
    ///     Fired when a status effect is applied or removed.
    /// </summary>
    public event EffectHandler? OnEffect;

    //--- equipment ---

    /// <summary>
    ///     Fired when an equipment slot is updated.
    /// </summary>
    public event EquipmentHandler? OnEquipment;

    /// <summary>
    ///     Fired when an error occurs during connection or handshake.
    /// </summary>
    public event ConnectionErrorHandler? OnError;

    /// <summary>
    ///     Fired when a logout response is received.
    /// </summary>
    public event ExitResponseHandler? OnExitResponse;

    /// <summary>
    ///     Fired after the server forced the client to echo a packet. The packet has already been sent; this event is
    ///     informational.
    /// </summary>
    public event ForceClientPacketHandler? OnForceClientPacket;

    //--- visual / audio ---

    /// <summary>
    ///     Fired when an entity's health bar should be displayed.
    /// </summary>
    public event HealthBarHandler? OnHealthBar;

    //--- world state ---

    /// <summary>
    ///     Fired when the ambient light level changes (time of day).
    /// </summary>
    public event LightLevelHandler? OnLightLevel;

    /// <summary>
    ///     Fired when the player's location changes.
    /// </summary>
    public event LocationChangedHandler? OnLocationChanged;

    /// <summary>
    ///     Fired when a login control is received (e.g. homepage URL).
    /// </summary>
    public event LoginControlHandler? OnLoginControl;

    /// <summary>
    ///     Fired when a login message is received (success, failure, or informational).
    /// </summary>
    public event LoginMessageHandler? OnLoginMessage;

    /// <summary>
    ///     Fired when a login notice (EULA) is received.
    /// </summary>
    public event LoginNoticeHandler? OnLoginNotice;

    /// <summary>
    ///     Fired when a map change is about to begin.
    /// </summary>
    public event MapChangePendingHandler? OnMapChangePending;

    /// <summary>
    ///     Fired for each row of map data received.
    /// </summary>
    public event MapDataHandler? OnMapData;

    /// <summary>
    ///     Fired when map info is received, before map data arrives.
    /// </summary>
    public event MapInfoHandler? OnMapInfo;

    /// <summary>
    ///     Fired when the server signals that map loading is complete.
    /// </summary>
    public event MapLoadCompleteHandler? OnMapLoadComplete;

    /// <summary>
    ///     Fired when metadata is received.
    /// </summary>
    public event MetaDataHandler? OnMetaData;

    /// <summary>
    ///     Fired when another player's profile is received.
    /// </summary>
    public event OtherProfileHandler? OnOtherProfile;

    /// <summary>
    ///     Fired when a redirect is received and the client needs to connect to a new server.
    /// </summary>
    public event RedirectReceivedHandler? OnRedirectReceived;

    /// <summary>
    ///     Fired when a viewport refresh response is received.
    /// </summary>
    public event RefreshResponseHandler? OnRefreshResponse;

    /// <summary>
    ///     Fired when an entity is removed from the viewport.
    /// </summary>
    public event RemoveEntityHandler? OnRemoveEntity;

    /// <summary>
    ///     Fired when an item is removed from the inventory pane.
    /// </summary>
    public event RemoveItemFromPaneHandler? OnRemoveItemFromPane;

    /// <summary>
    ///     Fired when a skill is removed from the skill pane.
    /// </summary>
    public event RemoveSkillFromPaneHandler? OnRemoveSkillFromPane;

    /// <summary>
    ///     Fired when a spell is removed from the spell pane.
    /// </summary>
    public event RemoveSpellFromPaneHandler? OnRemoveSpellFromPane;

    /// <summary>
    ///     Fired when the player's own profile is received.
    /// </summary>
    public event SelfProfileHandler? OnSelfProfile;

    //--- chat / messages ---

    /// <summary>
    ///     Fired when a system message is received (yellow text, overhead, etc.).
    /// </summary>
    public event ServerMessageHandler? OnServerMessage;

    /// <summary>
    ///     Fired when the lobby handshake completes and the server table is received.
    /// </summary>
    public event ServerTableReceivedHandler? OnServerTableReceived;

    /// <summary>
    ///     Fired when a sound or music track should play.
    /// </summary>
    public event SoundHandler? OnSound;

    /// <summary>
    ///     Fired when the server assigns the local player's entity ID during world entry.
    /// </summary>
    public event UserIdHandler? OnUserId;

    /// <summary>
    ///     Fired when world entry is complete and all essential data (user ID, map, location, attributes) has been received.
    /// </summary>
    public event WorldEntryCompleteHandler? OnWorldEntryComplete;

    /// <summary>
    ///     Fired when a world list (online players) is received.
    /// </summary>
    public event WorldListHandler? OnWorldList;

    /// <summary>
    ///     Fired when the world map should be displayed.
    /// </summary>
    public event WorldMapHandler? OnWorldMap;

    /// <summary>
    ///     Sends a pickup request from a tile.
    /// </summary>
    /// <param name="x">The source tile X coordinate.</param>
    /// <param name="y">The source tile Y coordinate.</param>
    /// <param name="destinationSlot">The inventory slot to place the item in.</param>
    public void PickupItem(int x, int y, byte destinationSlot)
        => SendIfWorld(
            new PickupArgs
            {
                SourcePoint = new Point(x, y),
                DestinationSlot = destinationSlot
            });

    /// <summary>
    ///     Processes queued inbound packets, driving state transitions. Call this from the game loop's Update method.
    /// </summary>
    /// <param name="buffer">A reusable list that receives drained packets; cleared by the caller between frames.</param>
    public void ProcessPackets(List<ServerPacket> buffer)
    {
        Client.DrainPackets(buffer);

        foreach (var pkt in buffer)
        {
            try
            {
                HandlePacket(pkt);
            } catch (Exception ex)
            {
                //log every downstream failure (deserializer mismatches, NREs in event handlers, etc.) so
                //protocol divergence between target servers is visible instead of silently dropped.
                var hex = Convert.ToHexString(pkt.Data, 0, Math.Min(pkt.Length, 128));
                NoticeDebugLog.Write($"!!! handler threw opcode=0x{pkt.OpCode:X2} len={pkt.Length} {ex.GetType().Name}: {ex.Message}");
                NoticeDebugLog.Write($"  hex(0..128)={hex}");
                NoticeDebugLog.Write($"  stack: {ex.StackTrace}");
            } finally
            {
                ArrayPool<byte>.Shared.Return(pkt.Data);
            }
        }

        //follow pending redirect once the old connection is fully torn down
        if (PendingRedirect is not null && !Client.Connected)
            FollowPendingRedirect();
    }

    /// <summary>
    ///     Sends a raise stat request.
    /// </summary>
    /// <param name="stat">The stat to raise.</param>
    public void RaiseStat(Stat stat)
        => SendIfWorld(
            new RaiseStatArgs
            {
                Stat = stat
            });

    /// <summary>
    ///     Sends an exit/logout request.
    /// </summary>
    /// <param name="isRequest"><see langword="true" /> to request the logout dialog; <see langword="false" /> to confirm logout.</param>
    public void RequestExit(bool isRequest = true)
        => SendIfWorld(
            new ExitRequestArgs
            {
                IsRequest = isRequest
            });

    /// <summary>
    ///     Requests the homepage URL from the login server.
    /// </summary>
    public void RequestHomepage()
    {
        if (State != ConnectionState.Login)
            return;

        Client.Send(new HomepageRequestArgs());
    }

    /// <summary>
    ///     Requests tile data for the current map from the server.
    /// </summary>
    public void RequestMapData() => SendIfWorld(new MapDataRequestArgs());

    /// <summary>
    ///     Requests the full login notice (EULA) from the login server.
    /// </summary>
    public void RequestNotice()
    {
        if (State != ConnectionState.Login)
            return;

        Client.Send(new NoticeRequestArgs());
    }

    /// <summary>
    ///     Sends a refresh request (F5).
    /// </summary>
    public void RequestRefresh() => SendIfWorld(new RefreshRequestArgs());

    /// <summary>
    ///     Sends a self profile request.
    /// </summary>
    public void RequestSelfProfile() => SendIfWorld(new SelfProfileRequestArgs());

    /// <summary>
    ///     Requests the server table from the lobby.
    /// </summary>
    public void RequestServerTable()
    {
        if (State != ConnectionState.Lobby)
            return;

        Client.Send(
            new ServerTableRequestArgs
            {
                ServerTableRequestType = ServerTableRequestType.RequestTable
            });
    }

    /// <summary>
    ///     Sends a world list request.
    /// </summary>
    public void RequestWorldList() => SendIfWorld(new WorldListRequestArgs());

    /// <summary>
    ///     Selects a server from the server table by ID, triggering a redirect.
    /// </summary>
    /// <param name="serverId">The server ID from the server table.</param>
    public void SelectServer(byte serverId)
    {
        if (State != ConnectionState.Lobby)
            return;

        Client.Send(
            new ServerTableRequestArgs
            {
                ServerTableRequestType = ServerTableRequestType.ServerId,
                ServerId = serverId
            });
    }

    /// <summary>
    ///     Adds a player to the ignore list.
    /// </summary>
    /// <param name="targetName">The name of the player to ignore.</param>
    public void SendAddIgnore(string targetName)
        => SendIfWorld(
            new IgnoreArgs
            {
                IgnoreType = IgnoreType.AddUser,
                TargetName = targetName
            });

    /// <summary>
    ///     Sends a begin chant packet to start spell casting.
    /// </summary>
    /// <param name="castLineCount">The number of chant lines for this spell.</param>
    public void SendBeginChant(byte castLineCount)
        => SendIfWorld(
            new BeginChantArgs
            {
                CastLineCount = castLineCount
            });

    /// <summary>
    ///     Sends a board/mail interaction (view board, read post, send mail, delete, etc.).
    /// </summary>
    /// <param name="requestType">The type of board interaction.</param>
    /// <param name="boardId">The board ID.</param>
    /// <param name="postId">The post ID for read/delete operations.</param>
    /// <param name="startPostId">The starting post ID for paginated listing.</param>
    /// <param name="controls">Board control flags (previous/next page availability).</param>
    /// <param name="to">The recipient name for mail.</param>
    /// <param name="subject">The post or mail subject.</param>
    /// <param name="message">The post or mail body text.</param>
    public void SendBoardInteraction(
        BoardRequestType requestType,
        ushort boardId = 0,
        short postId = 0,
        short startPostId = 0,
        BoardControls? controls = null,
        string? to = null,
        string? subject = null,
        string? message = null)
        => SendIfWorld(
            new BoardInteractionArgs
            {
                BoardRequestType = requestType,
                BoardId = boardId,
                PostId = postId,
                StartPostId = startPostId,
                Controls = controls,
                To = to,
                Subject = subject,
                Message = message
            });

    /// <summary>
    ///     Sends a chant line message.
    /// </summary>
    /// <param name="message">The chant text to display.</param>
    public void SendChant(string message)
        => SendIfWorld(
            new ChantArgs
            {
                ChantMessage = message
            });

    /// <summary>
    ///     Sends a CreateGroupbox request with recruitment configuration.
    /// </summary>
    /// <param name="playerName">The owner's (sender's) own character name. The protocol's
    /// TargetName field on CreateGroupbox is the sender's own name, not the groupbox title.</param>
    /// <param name="name">The group box name.</param>
    /// <param name="note">The recruitment note.</param>
    /// <param name="minLevel">Minimum level requirement.</param>
    /// <param name="maxLevel">Maximum level requirement.</param>
    /// <param name="maxWarriors">Maximum number of warriors.</param>
    /// <param name="maxWizards">Maximum number of wizards.</param>
    /// <param name="maxRogues">Maximum number of rogues.</param>
    /// <param name="maxPriests">Maximum number of priests.</param>
    /// <param name="maxMonks">Maximum number of monks.</param>
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
            new GroupInviteArgs
            {
                ClientGroupSwitch = ClientGroupSwitch.CreateGroupbox,
                //TargetName in CreateGroupbox is the owner's (sender's) own name, not
                //the groupbox title. The title lives in GroupBoxInfo.Name.
                TargetName = playerName,
                GroupBoxInfo = new CreateGroupBoxInfo
                {
                    Name = name,
                    Note = note,
                    MinLevel = minLevel,
                    MaxLevel = maxLevel,
                    MaxWarriors = maxWarriors,
                    MaxWizards = maxWizards,
                    MaxRogues = maxRogues,
                    MaxPriests = maxPriests,
                    MaxMonks = maxMonks
                }
            });

    /// <summary>
    ///     Sends a dialog interaction response (Next, Close, option select, text input).
    /// </summary>
    /// <param name="entityType">The type of entity that owns the dialog.</param>
    /// <param name="entityId">The entity ID of the dialog owner.</param>
    /// <param name="pursuitId">The pursuit ID of the current dialog chain.</param>
    /// <param name="dialogId">The dialog ID being responded to.</param>
    /// <param name="argsType">The type of response arguments.</param>
    /// <param name="option">The selected option index, if applicable.</param>
    /// <param name="args">Additional string arguments (e.g. text input values).</param>
    public void SendDialogResponse(
        EntityType entityType,
        uint entityId,
        ushort pursuitId,
        ushort dialogId,
        DialogArgsType argsType = DialogArgsType.None,
        byte? option = null,
        List<string>? args = null)
        => SendIfWorld(
            new DialogInteractionArgs
            {
                EntityType = entityType,
                EntityId = entityId,
                PursuitId = pursuitId,
                DialogId = dialogId,
                DialogArgsType = argsType,
                Option = option,
                Args = args
            });

    /// <summary>
    ///     Sends the player's portrait and profile text to the server.
    /// </summary>
    /// <param name="portraitData">The raw portrait image bytes.</param>
    /// <param name="profileMessage">The profile text.</param>
    public void SendEditableProfile(byte[] portraitData, string profileMessage)
        => SendIfWorld(
            new EditableProfileArgs
            {
                PortraitData = portraitData,
                ProfileMessage = profileMessage
            });

    /// <summary>
    ///     Sends an emote request (body animation 9-44).
    /// </summary>
    /// <param name="bodyAnimation">The emote body animation to play.</param>
    public void SendEmote(BodyAnimation bodyAnimation)
        => SendIfWorld(
            new EmoteArgs
            {
                BodyAnimation = bodyAnimation
            });

    /// <summary>
    ///     Sends an exchange interaction.
    /// </summary>
    /// <param name="type">The exchange action type.</param>
    /// <param name="otherId">The other player's entity ID.</param>
    /// <param name="sourceSlot">The inventory slot of the item being exchanged.</param>
    /// <param name="itemCount">The number of items to exchange (for stackable items).</param>
    /// <param name="goldAmount">The amount of gold to exchange.</param>
    public void SendExchangeInteraction(
        ExchangeRequestType type,
        uint otherId = 0,
        byte? sourceSlot = null,
        byte? itemCount = null,
        int? goldAmount = null)
        => SendIfWorld(
            new ExchangeInteractionArgs
            {
                ExchangeRequestType = type,
                OtherPlayerId = otherId,
                SourceSlot = sourceSlot,
                ItemCount = itemCount,
                GoldAmount = goldAmount
            });

    /// <summary>
    ///     Sends a group invite or group management action.
    /// </summary>
    /// <param name="action">The group action to perform.</param>
    /// <param name="targetName">The target player name, if applicable.</param>
    public void SendGroupInvite(ClientGroupSwitch action, string? targetName = null)
        => SendIfWorld(
            new GroupInviteArgs
            {
                ClientGroupSwitch = action,
                TargetName = targetName ?? string.Empty
            });

    /// <summary>
    ///     Requests the current ignore list from the server.
    /// </summary>
    public void SendIgnoreRequest()
        => SendIfWorld(
            new IgnoreArgs
            {
                IgnoreType = IgnoreType.Request
            });

    /// <summary>
    ///     Sends a menu interaction response (pursuit selection).
    /// </summary>
    /// <param name="entityType">The type of entity that owns the menu.</param>
    /// <param name="entityId">The entity ID of the menu owner.</param>
    /// <param name="pursuitId">The selected pursuit ID.</param>
    /// <param name="slot">The selected slot index, if applicable.</param>
    /// <param name="args">Additional string arguments.</param>
    public void SendMenuResponse(
        EntityType entityType,
        uint entityId,
        ushort pursuitId,
        byte? slot = null,
        string[]? args = null)
        => SendIfWorld(
            new MenuInteractionArgs
            {
                EntityType = entityType,
                EntityId = entityId,
                PursuitId = pursuitId,
                Slot = slot,
                Args = args
            });

    /// <summary>
    ///     Sends a metadata request to the server (checksums or specific file data).
    /// </summary>
    /// <param name="requestType">The type of metadata request.</param>
    /// <param name="name">The metadata file name, for specific file requests.</param>
    public void SendMetaDataRequest(MetaDataRequestType requestType, string? name = null)
        => Client.Send(
            new MetaDataRequestArgs
            {
                MetaDataRequestType = requestType,
                Name = name
            });

    /// <summary>
    ///     Toggles a user option (e.g. group allow, exchange allow, whisper settings).
    /// </summary>
    /// <param name="option">The user option to toggle.</param>
    public void SendOptionToggle(UserOption option)
        => SendIfWorld(
            new OptionToggleArgs
            {
                UserOption = option
            });

    /// <summary>
    ///     Sends a public (normal) chat message visible to nearby players.
    /// </summary>
    /// <param name="message">The chat message text.</param>
    public void SendPublicMessage(string message)
        => SendIfWorld(
            new PublicMessageArgs
            {
                Message = message,
                PublicMessageType = PublicMessageType.Normal
            });

    /// <summary>
    ///     Removes a player from the ignore list.
    /// </summary>
    /// <param name="targetName">The name of the player to un-ignore.</param>
    public void SendRemoveIgnore(string targetName)
        => SendIfWorld(
            new IgnoreArgs
            {
                IgnoreType = IgnoreType.RemoveUser,
                TargetName = targetName
            });

    /// <summary>
    ///     Sends notepad text for an editable notepad slot.
    /// </summary>
    /// <param name="slot">The notepad slot index.</param>
    /// <param name="message">The notepad text content.</param>
    public void SendSetNotepad(byte slot, string message)
        => SendIfWorld(
            new SetNotepadArgs
            {
                Slot = slot,
                Message = message
            });

    /// <summary>
    ///     Sends a shout message (! prefix) visible to all players on the map.
    /// </summary>
    /// <param name="message">The shout message text.</param>
    public void SendShout(string message)
        => SendIfWorld(
            new PublicMessageArgs
            {
                Message = message,
                PublicMessageType = PublicMessageType.Shout
            });

    /// <summary>
    ///     Sends a social status change to the server.
    /// </summary>
    /// <param name="status">The new social status.</param>
    public void SendSocialStatus(SocialStatus status)
        => SendIfWorld(
            new SocialStatusArgs
            {
                SocialStatus = status
            });

    /// <summary>
    ///     Sends a whisper to a specific player.
    /// </summary>
    /// <param name="targetName">The recipient player name.</param>
    /// <param name="message">The whisper message text.</param>
    public void SendWhisper(string targetName, string message)
        => SendIfWorld(
            new WhisperArgs
            {
                TargetName = targetName,
                Message = message
            });

    /// <summary>
    ///     Sends a spacebar (assail) request.
    /// </summary>
    public void Spacebar() => SendIfWorld(new SpacebarArgs());

    /// <summary>
    ///     Fired when the connection state changes. Args: (oldState, newState).
    /// </summary>
    public event ConnectionStateChangedHandler? StateChanged;

    /// <summary>
    ///     Sends a swap slot request between two panel positions.
    /// </summary>
    /// <param name="panelType">The panel type (inventory, skill, or spell).</param>
    /// <param name="slot1">The first slot position.</param>
    /// <param name="slot2">The second slot position.</param>
    public void SwapSlot(PanelType panelType, byte slot1, byte slot2)
        => SendIfWorld(
            new SwapSlotArgs
            {
                PanelType = panelType,
                Slot1 = slot1,
                Slot2 = slot2
            });

    /// <summary>
    ///     Toggles group membership (join/leave the current group).
    /// </summary>
    public void ToggleGroup() => SendIfWorld(new ToggleGroupArgs());

    /// <summary>
    ///     Sends a turn request to face the specified direction.
    /// </summary>
    /// <param name="direction">The direction to face.</param>
    public void Turn(Direction direction)
        => SendIfWorld(
            new TurnArgs
            {
                Direction = direction
            });

    /// <summary>
    ///     Sends an unequip request for the specified equipment slot.
    /// </summary>
    /// <param name="slot">The equipment slot to unequip.</param>
    public void Unequip(EquipmentSlot slot)
        => SendIfWorld(
            new UnequipArgs
            {
                EquipmentSlot = slot
            });

    /// <summary>
    ///     Sends an item use request (equip, consume).
    /// </summary>
    /// <param name="slot">The inventory slot of the item to use.</param>
    public void UseItem(byte slot)
        => SendIfWorld(
            new ItemUseArgs
            {
                SourceSlot = slot
            });

    /// <summary>
    ///     Sends a skill use request.
    /// </summary>
    /// <param name="slot">The skill book slot to use.</param>
    public void UseSkill(byte slot)
        => SendIfWorld(
            new SkillUseArgs
            {
                SourceSlot = slot
            });

    /// <summary>
    ///     Sends a spell use request.
    /// </summary>
    /// <param name="slot">The spell book slot to use.</param>
    /// <param name="argsData">Optional targeting data for targeted spells.</param>
    public void UseSpell(byte slot, byte[]? argsData = null)
        => SendIfWorld(
            new SpellUseArgs
            {
                SourceSlot = slot,
                ArgsData = argsData ?? []
            });

    /// <summary>
    ///     Sends a targeted spell cast at a specific entity and position.
    /// </summary>
    /// <param name="slot">The spell book slot to use.</param>
    /// <param name="targetId">The target entity ID.</param>
    /// <param name="targetX">The target tile X coordinate.</param>
    /// <param name="targetY">The target tile Y coordinate.</param>
    public void UseSpellOnTarget(
        byte slot,
        uint targetId,
        int targetX,
        int targetY)
    {
        var argsData = new byte[8];
        argsData[0] = (byte)(targetId >> 24);
        argsData[1] = (byte)(targetId >> 16);
        argsData[2] = (byte)(targetId >> 8);
        argsData[3] = (byte)targetId;
        argsData[4] = (byte)(targetX >> 8);
        argsData[5] = (byte)targetX;
        argsData[6] = (byte)(targetY >> 8);
        argsData[7] = (byte)targetY;

        UseSpell(slot, argsData);
    }

    /// <summary>
    ///     Sends a walk request in the specified direction.
    /// </summary>
    /// <param name="direction">The direction to walk.</param>
    public void Walk(Direction direction)
    {
        if (State != ConnectionState.World)
            return;

        Client.Send(
            new ClientWalkArgs
            {
                Direction = direction,
                StepCount = WalkStepCount++
            });
    }

    #region Private
    private ushort LobbyClientVersion;
    private bool PendingLobbyVersion;
    private ConnectionState PendingTargetState;
    private byte WalkStepCount;

    private void IndexHandlers()
    {
        //lobby
        PacketHandlers[(byte)ServerOpCode.AcceptConnection] = HandleAcceptConnection;
        PacketHandlers[(byte)ServerOpCode.ConnectionInfo] = HandleConnectionInfo;
        PacketHandlers[(byte)ServerOpCode.ServerTableResponse] = HandleServerTableResponse;
        PacketHandlers[(byte)ServerOpCode.Redirect] = HandleRedirect;

        //login
        PacketHandlers[(byte)ServerOpCode.LoginMessage] = HandleLoginMessage;
        PacketHandlers[(byte)ServerOpCode.LoginNotice] = HandleLoginNotice;
        PacketHandlers[(byte)ServerOpCode.LoginControl] = HandleLoginControl;

        //world entry
        PacketHandlers[(byte)ServerOpCode.UserId] = HandleUserId;
        PacketHandlers[(byte)ServerOpCode.MapInfo] = HandleMapInfo;
        PacketHandlers[(byte)ServerOpCode.MapData] = HandleMapData;
        PacketHandlers[(byte)ServerOpCode.MapLoadComplete] = HandleMapLoadComplete;
        PacketHandlers[(byte)ServerOpCode.MapChangeComplete] = HandleMapChangeComplete;
        PacketHandlers[(byte)ServerOpCode.Location] = HandleLocation;
        PacketHandlers[(byte)ServerOpCode.Attributes] = HandleAttributes;
        PacketHandlers[(byte)ServerOpCode.DisplayVisibleEntities] = HandleDisplayVisibleEntities;
        PacketHandlers[(byte)ServerOpCode.DisplayAisling] = HandleDisplayAisling;

        //world entities
        PacketHandlers[(byte)ServerOpCode.RemoveEntity] = HandleRemoveEntity;
        PacketHandlers[(byte)ServerOpCode.CreatureWalk] = HandleCreatureWalk;
        PacketHandlers[(byte)ServerOpCode.ClientWalkResponse] = HandleClientWalkResponse;
        PacketHandlers[(byte)ServerOpCode.CreatureTurn] = HandleCreatureTurn;

        //chat / messages
        PacketHandlers[(byte)ServerOpCode.ServerMessage] = HandleServerMessage;
        PacketHandlers[(byte)ServerOpCode.DisplayPublicMessage] = HandleDisplayPublicMessage;

        //inventory
        PacketHandlers[(byte)ServerOpCode.AddItemToPane] = HandleAddItemToPane;
        PacketHandlers[(byte)ServerOpCode.RemoveItemFromPane] = HandleRemoveItemFromPane;

        //skills / spells
        PacketHandlers[(byte)ServerOpCode.AddSkillToPane] = HandleAddSkillToPane;
        PacketHandlers[(byte)ServerOpCode.RemoveSkillFromPane] = HandleRemoveSkillFromPane;
        PacketHandlers[(byte)ServerOpCode.AddSpellToPane] = HandleAddSpellToPane;
        PacketHandlers[(byte)ServerOpCode.RemoveSpellFromPane] = HandleRemoveSpellFromPane;

        //equipment
        PacketHandlers[(byte)ServerOpCode.Equipment] = HandleEquipment;
        PacketHandlers[(byte)ServerOpCode.DisplayUnequip] = HandleDisplayUnequip;

        //visual / audio
        PacketHandlers[(byte)ServerOpCode.HealthBar] = HandleHealthBar;
        PacketHandlers[(byte)ServerOpCode.Sound] = HandleSound;
        PacketHandlers[(byte)ServerOpCode.BodyAnimation] = HandleBodyAnimation;
        PacketHandlers[(byte)ServerOpCode.Animation] = HandleAnimation;
        PacketHandlers[(byte)ServerOpCode.Cooldown] = HandleCooldown;
        PacketHandlers[(byte)ServerOpCode.Effect] = HandleEffect;

        //world state
        PacketHandlers[(byte)ServerOpCode.LightLevel] = HandleLightLevel;
        PacketHandlers[(byte)ServerOpCode.Door] = HandleDoor;
        PacketHandlers[(byte)ServerOpCode.RefreshResponse] = HandleRefreshResponse;
        PacketHandlers[(byte)ServerOpCode.MapChangePending] = HandleMapChangePending;

        //npc interaction
        PacketHandlers[(byte)ServerOpCode.DisplayMenu] = HandleDisplayMenu;
        PacketHandlers[(byte)ServerOpCode.DisplayDialog] = HandleDisplayDialog;
        PacketHandlers[(byte)ServerOpCode.DisplayBoard] = HandleDisplayBoard;
        PacketHandlers[(byte)ServerOpCode.DisplayExchange] = HandleDisplayExchange;
        PacketHandlers[(byte)ServerOpCode.DisplayGroupInvite] = HandleDisplayGroupInvite;

        //profiles / lists
        PacketHandlers[(byte)ServerOpCode.EditableProfileRequest] = HandleEditableProfileRequest;
        PacketHandlers[(byte)ServerOpCode.SelfProfile] = HandleSelfProfile;
        PacketHandlers[(byte)ServerOpCode.OtherProfile] = HandleOtherProfile;
        PacketHandlers[(byte)ServerOpCode.WorldList] = HandleWorldList;
        PacketHandlers[(byte)ServerOpCode.WorldMap] = HandleWorldMap;

        //notepads
        PacketHandlers[(byte)ServerOpCode.DisplayEditableNotepad] = HandleDisplayEditableNotepad;
        PacketHandlers[(byte)ServerOpCode.DisplayReadonlyNotepad] = HandleDisplayReadonlyNotepad;

        //misc
        PacketHandlers[(byte)ServerOpCode.ExitResponse] = HandleExitResponse;
        PacketHandlers[(byte)ServerOpCode.ForceClientPacket] = HandleForceClientPacket;
        PacketHandlers[(byte)ServerOpCode.CancelCasting] = HandleCancelCasting;
        PacketHandlers[(byte)ServerOpCode.MetaData] = HandleMetaData;
    }

    private void HandlePacket(ServerPacket pkt)
    {
        var handler = PacketHandlers[pkt.OpCode];
        NoticeDebugLog.Write($"inbound opcode=0x{pkt.OpCode:X2} len={pkt.Length} handled={handler is not null} state={State}");
        handler?.Invoke(pkt);
    }

    private void HandleAcceptConnection(ServerPacket _)
    {
        if (PendingLobbyVersion)
        {
            //lobby handshake — send version packet
            PendingLobbyVersion = false;
            State = PendingTargetState;

            Client.Send(
                new VersionArgs
                {
                    Version = LobbyClientVersion
                });
        }

        //redirected connections send clientredirected in followredirectasync
        //immediately after connecting, without waiting for acceptconnection.
    }

    private void HandleConnectionInfo(ServerPacket pkt)
    {
        NoticeDebugLog.Write($"HandleConnectionInfo enter state={State}");
        try
        {
            var args = Client.Deserialize<ConnectionInfoArgs>(in pkt);
            NoticeDebugLog.Write($"  deserialized Seed={args.Seed} Key.Length={args.Key?.Length}");

            //lobby always uses empty keysaltseed (crypto falls back to "default")
            //must explicitly pass keysaltseed to avoid binding to the 2-arg constructor
            //crypto(byte seed, string keysaltseed) which generates a random key
            Client.Crypto = new Crypto(args.Seed, args.Key, null);
            NoticeDebugLog.Write("  crypto set");

            //crypto is now configured — safe to request the server table
            if (State == ConnectionState.Lobby)
            {
                NoticeDebugLog.Write("  calling RequestServerTable");
                RequestServerTable();
            }
        }
        catch (Exception ex)
        {
            NoticeDebugLog.Write($"  !!! HandleConnectionInfo threw {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    private void HandleServerTableResponse(ServerPacket pkt)
    {
        NoticeDebugLog.Write("HandleServerTableResponse enter");
        try
        {
            var rawHex = Convert.ToHexString(pkt.Data, 0, Math.Min(pkt.Length, 64));
            NoticeDebugLog.Write($"  full packet hex (first 64): {rawHex}");
            var args = Client.Deserialize<ServerTableResponseArgs>(in pkt);
            NoticeDebugLog.Write($"  raw ServerTable bytes={args.ServerTable?.Length}");
            if (args.ServerTable is { Length: > 0 })
                NoticeDebugLog.Write($"  ServerTable hex (first 32): {Convert.ToHexString(args.ServerTable, 0, Math.Min(args.ServerTable.Length, 32))}");
            var serverTableData = ServerTableData.Parse(args.ServerTable);
            NoticeDebugLog.Write($"  parsed {serverTableData.Servers?.Count ?? 0} servers, ShowServerList={serverTableData.ShowServerList}");
            OnServerTableReceived?.Invoke(serverTableData);
        }
        catch (Exception ex)
        {
            NoticeDebugLog.Write($"  !!! HandleServerTableResponse threw {ex.GetType().Name}: {ex.Message}");
            NoticeDebugLog.Write($"  stack: {ex.StackTrace}");
            throw;
        }
    }

    private void HandleRedirect(ServerPacket pkt)
    {
        var args = Client.Deserialize<RedirectArgs>(in pkt);

        //determine target state based on current state
        var targetState = State switch
        {
            ConnectionState.Lobby => ConnectionState.Login,
            ConnectionState.Login => ConnectionState.World,
            _                     => ConnectionState.Login
        };

        PendingRedirect = new RedirectInfo(
            args.EndPoint,
            args.Seed,
            args.Key,
            args.Name,
            args.Id,
            targetState);

        //begin teardown immediately — the redirect will be followed from the game loop
        //once the old connection is fully dead.
        Client.Disconnect();

        OnRedirectReceived?.Invoke(PendingRedirect.Value);
    }

    private void HandleLoginMessage(ServerPacket pkt)
    {
        var args = Client.Deserialize<LoginMessageArgs>(in pkt);
        OnLoginMessage?.Invoke(args);
    }

    private void HandleLoginNotice(ServerPacket pkt)
    {
        NoticeDebugLog.Write($"HandleLoginNotice raw Length={pkt.Length} DataLen={pkt.Data?.Length}");
        var args = Client.Deserialize<LoginNoticeArgs>(in pkt);
        NoticeDebugLog.Write($"  parsed IsFullResponse={args.IsFullResponse} CheckSum={args.CheckSum:X8} Data?.Length={args.Data?.Length}");
        OnLoginNotice?.Invoke(args);
    }

    private void HandleLoginControl(ServerPacket pkt)
    {
        var args = Client.Deserialize<LoginControlArgs>(in pkt);
        OnLoginControl?.Invoke(args);
    }

    private void HandleUserId(ServerPacket pkt)
    {
        var args = Client.Deserialize<UserIdArgs>(in pkt);
        AislingId = args.Id;
        OnUserId?.Invoke(args.Id);
        EntryState |= WorldEntryState.UserId;
        CheckWorldEntryComplete();
    }

    private void HandleMapInfo(ServerPacket pkt)
    {
        // Hybrasyl does not send MapChangePending, MapLoadComplete, or MapChangeComplete.
        // Synthesize the full lifecycle around the single MapInfo packet: fire pending first so
        // UI (world map, town map, pathfinding) is torn down, then the normal MapInfo event, then
        // the implicit load/change completion so OnWorldEntryComplete can fire.
        SynthesizeMapChangePending();

        var args = Client.Deserialize<MapInfoArgs>(in pkt);
        MapInfo = args;
        EntryState |= WorldEntryState.MapInfo;
        OnMapInfo?.Invoke(args);

        EntryState |= WorldEntryState.MapLoaded | WorldEntryState.MapChangeComplete;
        OnMapLoadComplete?.Invoke();
        CheckWorldEntryComplete();
    }

    private void HandleMapData(ServerPacket pkt)
    {
        var args = Client.Deserialize<MapDataArgs>(in pkt);
        OnMapData?.Invoke(args);
    }

    private void HandleMapLoadComplete(ServerPacket _)
    {
        EntryState |= WorldEntryState.MapLoaded;
        OnMapLoadComplete?.Invoke();
        CheckWorldEntryComplete();
    }

    private void HandleMapChangeComplete(ServerPacket _)
    {
        EntryState |= WorldEntryState.MapChangeComplete;
        CheckWorldEntryComplete();
    }

    private void HandleLocation(ServerPacket pkt)
    {
        var args = Client.Deserialize<LocationArgs>(in pkt);
        PlayerX = args.X;
        PlayerY = args.Y;
        EntryState |= WorldEntryState.Location;
        OnLocationChanged?.Invoke(args.X, args.Y);
        CheckWorldEntryComplete();
    }

    private void HandleAttributes(ServerPacket pkt)
    {
        var args = Client.Deserialize<AttributesArgs>(in pkt);

        //merge partial updates with previously stored attributes so consumers always get a complete picture
        if (Attributes is not null)
            args = MergeAttributes(Attributes, args);

        Attributes = args;
        EntryState |= WorldEntryState.Attributes;
        OnAttributes?.Invoke(args);
        CheckWorldEntryComplete();
    }

    private static AttributesArgs MergeAttributes(AttributesArgs previous, AttributesArgs incoming)
    {
        var flags = incoming.StatUpdateType;

        //start from previous complete state, then overlay the incoming partial fields
        var merged = previous with
        {
            StatUpdateType = flags
        };

        if (flags.HasFlag(StatUpdateType.Primary))
            merged = merged with
            {
                Level = incoming.Level,
                Ability = incoming.Ability,
                MaximumHp = incoming.MaximumHp,
                MaximumMp = incoming.MaximumMp,
                Str = incoming.Str,
                Int = incoming.Int,
                Wis = incoming.Wis,
                Con = incoming.Con,
                Dex = incoming.Dex,
                UnspentPoints = incoming.UnspentPoints,
                MaxWeight = incoming.MaxWeight,
                CurrentWeight = incoming.CurrentWeight
            };

        if (flags.HasFlag(StatUpdateType.Vitality))
            merged = merged with
            {
                CurrentHp = incoming.CurrentHp,
                CurrentMp = incoming.CurrentMp
            };

        if (flags.HasFlag(StatUpdateType.ExpGold))
            merged = merged with
            {
                TotalExp = incoming.TotalExp,
                ToNextLevel = incoming.ToNextLevel,
                TotalAbility = incoming.TotalAbility,
                ToNextAbility = incoming.ToNextAbility,
                GamePoints = incoming.GamePoints,
                Gold = incoming.Gold
            };

        if (flags.HasFlag(StatUpdateType.Secondary))
            merged = merged with
            {
                Blind = incoming.Blind,
                HasUnreadMail = incoming.HasUnreadMail,
                OffenseElement = incoming.OffenseElement,
                DefenseElement = incoming.DefenseElement,
                MagicResistance = incoming.MagicResistance,
                Ac = incoming.Ac,
                Dmg = incoming.Dmg,
                Hit = incoming.Hit
            };

        if (flags.HasFlag(StatUpdateType.GameMasterA))
            merged = merged with
            {
                IsAdmin = incoming.IsAdmin
            };

        if (flags.HasFlag(StatUpdateType.GameMasterB))
            merged = merged with
            {
                IsSwimming = incoming.IsSwimming
            };

        return merged;
    }

    private void HandleDisplayVisibleEntities(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayVisibleEntitiesArgs>(in pkt);
        OnDisplayVisibleEntities?.Invoke(args);
    }

    private void HandleDisplayAisling(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayAislingArgs>(in pkt);
        OnDisplayAisling?.Invoke(args);
    }

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

    private void HandleRemoveEntity(ServerPacket pkt)
    {
        var args = Client.Deserialize<RemoveEntityArgs>(in pkt);
        OnRemoveEntity?.Invoke(args.SourceId);
    }

    private void HandleCreatureWalk(ServerPacket pkt)
    {
        var args = Client.Deserialize<CreatureWalkArgs>(in pkt);

        OnCreatureWalk?.Invoke(
            args.SourceId,
            args.OldPoint.X,
            args.OldPoint.Y,
            args.Direction);
    }

    private void HandleClientWalkResponse(ServerPacket pkt)
    {
        var args = Client.Deserialize<ClientWalkResponseArgs>(in pkt);
        OnClientWalkResponse?.Invoke(args.Direction, args.OldPoint.X, args.OldPoint.Y);
    }

    private void HandleCreatureTurn(ServerPacket pkt)
    {
        var args = Client.Deserialize<CreatureTurnArgs>(in pkt);
        OnCreatureTurn?.Invoke(args.SourceId, args.Direction);
    }

    //--- chat / messages ---

    private void HandleServerMessage(ServerPacket pkt)
    {
        var args = Client.Deserialize<ServerMessageArgs>(in pkt);
        OnServerMessage?.Invoke(args);
    }

    private void HandleDisplayPublicMessage(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayPublicMessageArgs>(in pkt);
        OnDisplayPublicMessage?.Invoke(args);
    }

    //--- inventory ---

    private void HandleAddItemToPane(ServerPacket pkt)
    {
        var args = Client.Deserialize<AddItemToPaneArgs>(in pkt);
        OnAddItemToPane?.Invoke(args);
    }

    private void HandleRemoveItemFromPane(ServerPacket pkt)
    {
        var args = Client.Deserialize<RemoveItemFromPaneArgs>(in pkt);
        OnRemoveItemFromPane?.Invoke(args);
    }

    //--- skills / spells ---

    private void HandleAddSkillToPane(ServerPacket pkt)
    {
        var args = Client.Deserialize<AddSkillToPaneArgs>(in pkt);
        OnAddSkillToPane?.Invoke(args);
    }

    private void HandleRemoveSkillFromPane(ServerPacket pkt)
    {
        var args = Client.Deserialize<RemoveSkillFromPaneArgs>(in pkt);
        OnRemoveSkillFromPane?.Invoke(args);
    }

    private void HandleAddSpellToPane(ServerPacket pkt)
    {
        var args = Client.Deserialize<AddSpellToPaneArgs>(in pkt);
        OnAddSpellToPane?.Invoke(args);
    }

    private void HandleRemoveSpellFromPane(ServerPacket pkt)
    {
        var args = Client.Deserialize<RemoveSpellFromPaneArgs>(in pkt);
        OnRemoveSpellFromPane?.Invoke(args);
    }

    //--- equipment ---

    private void HandleEquipment(ServerPacket pkt)
    {
        var args = Client.Deserialize<EquipmentArgs>(in pkt);
        OnEquipment?.Invoke(args);
    }

    private void HandleDisplayUnequip(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayUnequipArgs>(in pkt);
        OnDisplayUnequip?.Invoke(args);
    }

    //--- visual / audio ---

    private void HandleHealthBar(ServerPacket pkt)
    {
        var args = Client.Deserialize<HealthBarArgs>(in pkt);
        OnHealthBar?.Invoke(args);
    }

    private void HandleSound(ServerPacket pkt)
    {
        var args = Client.Deserialize<SoundArgs>(in pkt);
        OnSound?.Invoke(args);
    }

    private void HandleBodyAnimation(ServerPacket pkt)
    {
        var args = Client.Deserialize<BodyAnimationArgs>(in pkt);
        OnBodyAnimation?.Invoke(args);
    }

    private void HandleAnimation(ServerPacket pkt)
    {
        var args = Client.Deserialize<AnimationArgs>(in pkt);
        OnAnimation?.Invoke(args);
    }

    private void HandleCooldown(ServerPacket pkt)
    {
        var args = Client.Deserialize<CooldownArgs>(in pkt);
        OnCooldown?.Invoke(args);
    }

    private void HandleEffect(ServerPacket pkt)
    {
        var args = Client.Deserialize<EffectArgs>(in pkt);
        OnEffect?.Invoke(args);
    }

    //--- world state ---

    private void HandleLightLevel(ServerPacket pkt)
    {
        var args = Client.Deserialize<LightLevelArgs>(in pkt);
        OnLightLevel?.Invoke(args);
    }

    private void HandleDoor(ServerPacket pkt)
    {
        var args = Client.Deserialize<DoorArgs>(in pkt);
        OnDoor?.Invoke(args);
    }

    private void HandleRefreshResponse(ServerPacket _) => OnRefreshResponse?.Invoke();

    private void HandleMapChangePending(ServerPacket _) => OnMapChangePending?.Invoke();

    // Synthesize a MapChangePending signal for Hybrasyl: fire it before HandleMapInfo's
    // implicit completion so UI cleanup (world-map hide, pathfinding clear) runs first.
    private void SynthesizeMapChangePending() => OnMapChangePending?.Invoke();

    //--- npc interaction ---

    private void HandleDisplayMenu(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayMenuArgs>(in pkt);
        OnDisplayMenu?.Invoke(args);
    }

    private void HandleDisplayDialog(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayDialogArgs>(in pkt);
        OnDisplayDialog?.Invoke(args);
    }

    private void HandleDisplayBoard(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayBoardArgs>(in pkt);
        OnDisplayBoard?.Invoke(args);
    }

    private void HandleDisplayExchange(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayExchangeArgs>(in pkt);
        OnDisplayExchange?.Invoke(args);
    }

    private void HandleDisplayGroupInvite(ServerPacket pkt)
    {
        //bypass chaos.networking's DisplayGroupInviteConverter — its Deserialize/Serialize branches are
        //functionally swapped against the actual Hybrasyl/retail wire shape (Invite=1 reads box info that
        //isn't there; ShowGroupBox=4 ignores box info that is). See chaos-networking-removal-direction.md
        //"Tactical workarounds (pre-removal)". Wire shape per server/hybrasyl Subsystems/Players/Grouping/UserGroup.cs.
        var span = pkt.Data.AsSpan(0, pkt.Length);
        var packet = new Packet(ref span, pkt.IsEncrypted);
        var reader = new SpanReader(Encoding.GetEncoding(949), in packet.Buffer);

        var subtype = reader.ReadByte();
        DisplayGroupInviteArgs? args;

        switch (subtype)
        {
            case 1: //Hybrasyl `Ask` — wire: [string8 source][0][0]. Map to Chaos enum Invite.
            {
                var source = reader.ReadString8();

                args = new DisplayGroupInviteArgs
                {
                    ServerGroupSwitch = ServerGroupSwitch.Invite,
                    SourceName = source
                };

                break;
            }
            case 2: //Hybrasyl `Member` — never emitted; retail wire shape unknown without capture.
                NoticeDebugLog.Write($"0x63 subtype 0x02 (Member) unhandled — no retail capture yet. len={pkt.Length}");

                return;
            case 4: //Hybrasyl `RecruitInfo` — full WriteInfo block. Map to Chaos enum ShowGroupBox.
            {
                var recruiter = reader.ReadString8();
                var name = reader.ReadString8();
                var note = reader.ReadString8();
                var minLevel = reader.ReadByte();
                var maxLevel = reader.ReadByte();

                //class-slot order on the wire (Hybrasyl WriteInfo): Warrior, Wizard, Rogue, Priest, Monk
                var maxWarriors = reader.ReadByte();
                var currWarriors = reader.ReadByte();
                var maxWizards = reader.ReadByte();
                var currWizards = reader.ReadByte();
                var maxRogues = reader.ReadByte();
                var currRogues = reader.ReadByte();
                var maxPriests = reader.ReadByte();
                var currPriests = reader.ReadByte();
                var maxMonks = reader.ReadByte();
                var currMonks = reader.ReadByte();

                args = new DisplayGroupInviteArgs
                {
                    ServerGroupSwitch = ServerGroupSwitch.ShowGroupBox,
                    SourceName = recruiter,
                    GroupBoxInfo = new DisplayGroupBoxInfo
                    {
                        Name = name,
                        Note = note,
                        MinLevel = minLevel,
                        MaxLevel = maxLevel,
                        MaxWarriors = maxWarriors,
                        CurrentWarriors = currWarriors,
                        MaxWizards = maxWizards,
                        CurrentWizards = currWizards,
                        MaxRogues = maxRogues,
                        CurrentRogues = currRogues,
                        MaxPriests = maxPriests,
                        CurrentPriests = currPriests,
                        MaxMonks = maxMonks,
                        CurrentMonks = currMonks
                    }
                };

                break;
            }
            case 5: //Hybrasyl `RecruitAsk` — never emitted; retail wire shape unknown without capture.
                NoticeDebugLog.Write($"0x63 subtype 0x05 (RecruitAsk) unhandled — no retail capture yet. len={pkt.Length}");

                return;
            default:
                NoticeDebugLog.Write($"0x63 unknown subtype 0x{subtype:X2} len={pkt.Length}");

                return;
        }

        OnDisplayGroupInvite?.Invoke(args);
    }

    //--- profiles / lists ---

    private void HandleEditableProfileRequest(ServerPacket _) => OnEditableProfileRequest?.Invoke();

    /// <summary>
    ///     Fired when the server requests the player's portrait and profile text.
    /// </summary>
    public event EditableProfileRequestHandler? OnEditableProfileRequest;

    private void HandleSelfProfile(ServerPacket pkt)
    {
        var args = Client.Deserialize<SelfProfileArgs>(in pkt);
        OnSelfProfile?.Invoke(args);
    }

    private void HandleOtherProfile(ServerPacket pkt)
    {
        var args = Client.Deserialize<OtherProfileArgs>(in pkt);
        OnOtherProfile?.Invoke(args);
    }

    private void HandleWorldList(ServerPacket pkt)
    {
        var args = Client.Deserialize<WorldListArgs>(in pkt);
        OnWorldList?.Invoke(args);
    }

    private void HandleWorldMap(ServerPacket pkt)
    {
        var args = Client.Deserialize<WorldMapArgs>(in pkt);
        OnWorldMap?.Invoke(args);
    }

    //--- notepads ---

    private void HandleDisplayEditableNotepad(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayEditableNotepadArgs>(in pkt);
        OnDisplayEditableNotepad?.Invoke(args);
    }

    private void HandleDisplayReadonlyNotepad(ServerPacket pkt)
    {
        var args = Client.Deserialize<DisplayReadonlyNotepadArgs>(in pkt);
        OnDisplayReadonlyNotepad?.Invoke(args);
    }

    //--- misc ---

    private void HandleExitResponse(ServerPacket pkt)
    {
        var args = Client.Deserialize<ExitResponseArgs>(in pkt);
        OnExitResponse?.Invoke(args);
    }

    private void HandleForceClientPacket(ServerPacket pkt)
    {
        var args = Client.Deserialize<ForceClientPacketArgs>(in pkt);

        var owner = MemoryPool<byte>.Shared.Rent(args.Data.Length);
        args.Data.CopyTo(owner.Memory.Span);

        var packet = new Packet((byte)args.ClientOpCode, owner, args.Data.Length);
        Client.Send(ref packet);

        OnForceClientPacket?.Invoke(args);
    }

    private void HandleCancelCasting(ServerPacket _) => OnCancelCasting?.Invoke();

    private void HandleMetaData(ServerPacket pkt)
    {
        var args = Client.Deserialize<MetaDataArgs>(in pkt);
        OnMetaData?.Invoke(args);
    }

    private void HandleDisconnected()
    {
        if (State != ConnectionState.Connecting)
            State = ConnectionState.Disconnected;
    }
    #endregion
}

/// <summary>
///     Contains redirect information received from the server.
/// </summary>
public readonly record struct RedirectInfo(
    IPEndPoint EndPoint,
    byte Seed,
    string Key,
    string Name,
    uint Id,
    ConnectionState TargetState);