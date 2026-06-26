#region
using Chaos.Geometry.Abstractions.Definitions;
using Chaos.Networking.Entities.Server;
#endregion

namespace Brigid.Networking.Definitions;

#region GameClient Delegates
/// <summary>
///     Fired when the client is disconnected from the server.
/// </summary>
public delegate void DisconnectedHandler();

/// <summary>
///     Fired when a packet is received that is not handled internally (heartbeat/sync).
/// </summary>
public delegate void PacketReceivedHandler(ServerPacket packet);
#endregion

#region ConnectionManager Delegates
/// <summary>
///     Fired when the connection state changes.
/// </summary>
public delegate void ConnectionStateChangedHandler(ConnectionState oldState, ConnectionState newState);

/// <summary>
///     Fired when an error occurs during connection or handshake.
/// </summary>
public delegate void ConnectionErrorHandler(string message);

/// <summary>
///     Fired when a casting animation should be cancelled.
/// </summary>
public delegate void CancelCastingHandler();

/// <summary>
///     Fired when the server confirms the player's own walk.
/// </summary>
public delegate void ClientWalkResponseHandler(Direction direction, int oldX, int oldY);

/// <summary>
///     Fired when another entity changes facing direction.
/// </summary>
public delegate void CreatureTurnHandler(uint sourceId, Direction direction);

/// <summary>
///     Fired when another entity walks.
/// </summary>
public delegate void CreatureWalkHandler(uint sourceId, int oldX, int oldY, Direction direction);

/// <summary>
///     Fired when the player's location changes.
/// </summary>
public delegate void LocationChangedHandler(int x, int y);

/// <summary>
///     Fired when a map change is about to begin.
/// </summary>
public delegate void MapChangePendingHandler();

/// <summary>
///     Fired when the server signals that map loading is complete.
/// </summary>
public delegate void MapLoadCompleteHandler();

/// <summary>
///     Fired when a redirect is received and the client needs to connect to a new server.
/// </summary>
public delegate void RedirectReceivedHandler(RedirectInfo info);

/// <summary>
///     Fired when a viewport refresh response is received.
/// </summary>
public delegate void RefreshResponseHandler();

/// <summary>
///     Fired when an entity is removed from the viewport.
/// </summary>
public delegate void RemoveEntityHandler(uint entityId);

/// <summary>
///     Fired when the lobby handshake completes and the server table is received.
/// </summary>
public delegate void ServerTableReceivedHandler(ServerTableData data);

/// <summary>
///     Fired when the server assigns the local player's entity ID during world entry.
/// </summary>
public delegate void UserIdHandler(uint userId);

/// <summary>
///     Fired when world entry is complete and all essential data has been received.
/// </summary>
public delegate void WorldEntryCompleteHandler();

/// <summary>
///     Fired when the server requests the player's portrait and profile text.
/// </summary>
public delegate void EditableProfileRequestHandler();
#endregion

#region ConnectionManager Args-Based Delegates
/// <summary>
///     Fired when an item is added to the inventory pane.
/// </summary>
public delegate void AddItemToPaneHandler(AddItemToPaneArgs args);

/// <summary>
///     Fired when a skill is added to the skill pane.
/// </summary>
public delegate void AddSkillToPaneHandler(AddSkillToPaneArgs args);

/// <summary>
///     Fired when a spell is added to the spell pane.
/// </summary>
public delegate void AddSpellToPaneHandler(AddSpellToPaneArgs args);

/// <summary>
///     Fired when a spell/effect animation should play.
/// </summary>
public delegate void AnimationHandler(AnimationArgs args);

/// <summary>
///     Fired when player attributes are updated.
/// </summary>
public delegate void AttributesHandler(AttributesArgs args);

/// <summary>
///     Fired when a body animation is triggered on an entity.
/// </summary>
public delegate void BodyAnimationHandler(BodyAnimationArgs args);

/// <summary>
///     Fired when a skill or spell cooldown starts.
/// </summary>
public delegate void CooldownHandler(CooldownArgs args);

/// <summary>
///     Fired when a bulletin board should be displayed.
/// </summary>
public delegate void DisplayBoardHandler(DisplayBoardArgs args);

/// <summary>
///     Fired when an NPC dialog should be displayed.
/// </summary>
public delegate void DisplayDialogHandler(DisplayDialogArgs args);

/// <summary>
///     Fired when an editable notepad should be displayed.
/// </summary>
public delegate void DisplayEditableNotepadHandler(DisplayEditableNotepadArgs args);

/// <summary>
///     Fired when an exchange/trade window should be displayed.
/// </summary>
public delegate void DisplayExchangeHandler(DisplayExchangeArgs args);

/// <summary>
///     Fired when a group invite is received.
/// </summary>
public delegate void DisplayGroupInviteHandler(DisplayGroupInviteArgs args);

/// <summary>
///     Fired when an NPC menu should be displayed.
/// </summary>
public delegate void DisplayMenuHandler(DisplayMenuArgs args);

/// <summary>
///     Fired when a public chat message is displayed.
/// </summary>
public delegate void DisplayPublicMessageHandler(DisplayPublicMessageArgs args);

/// <summary>
///     Fired when a read-only notepad should be displayed.
/// </summary>
public delegate void DisplayReadonlyNotepadHandler(DisplayReadonlyNotepadArgs args);

/// <summary>
///     Fired when an aisling display is received.
/// </summary>
public delegate void DisplayAislingHandler(DisplayAislingArgs args);

/// <summary>
///     Fired when an equipment slot is cleared.
/// </summary>
public delegate void DisplayUnequipHandler(DisplayUnequipArgs args);

/// <summary>
///     Fired when a visible entity (non-aisling) is received.
/// </summary>
public delegate void DisplayVisibleEntitiesHandler(DisplayVisibleEntitiesArgs args);

/// <summary>
///     Fired when door states are updated.
/// </summary>
public delegate void DoorHandler(DoorArgs args);

/// <summary>
///     Fired when a status effect is applied or removed.
/// </summary>
public delegate void EffectHandler(EffectArgs args);

/// <summary>
///     Fired when an equipment slot is updated.
/// </summary>
public delegate void EquipmentHandler(EquipmentArgs args);

/// <summary>
///     Fired when a logout response is received.
/// </summary>
public delegate void ExitResponseHandler(ExitResponseArgs args);

/// <summary>
///     Fired after the server forced the client to echo a packet.
/// </summary>
public delegate void ForceClientPacketHandler(ForceClientPacketArgs args);

/// <summary>
///     Fired when an entity's health bar should be displayed.
/// </summary>
public delegate void HealthBarHandler(HealthBarArgs args);

/// <summary>
///     Fired when the ambient light level changes (time of day).
/// </summary>
public delegate void LightLevelHandler(LightLevelArgs args);

/// <summary>
///     Fired when a login control is received (e.g. homepage URL).
/// </summary>
public delegate void LoginControlHandler(LoginControlArgs args);

/// <summary>
///     Fired when a login message is received (success, failure, or informational).
/// </summary>
public delegate void LoginMessageHandler(LoginMessageArgs args);

/// <summary>
///     Fired when a login notice (EULA) is received.
/// </summary>
public delegate void LoginNoticeHandler(LoginNoticeArgs args);

/// <summary>
///     Fired for each row of map data received.
/// </summary>
public delegate void MapDataHandler(MapDataArgs args);

/// <summary>
///     Fired when map info is received, before map data arrives.
/// </summary>
public delegate void MapInfoHandler(MapInfoArgs args);

/// <summary>
///     Fired when metadata is received.
/// </summary>
public delegate void MetaDataHandler(MetaDataArgs args);

/// <summary>
///     Fired when another player's profile is received.
/// </summary>
public delegate void OtherProfileHandler(OtherProfileArgs args);

/// <summary>
///     Fired when an item is removed from the inventory pane.
/// </summary>
public delegate void RemoveItemFromPaneHandler(RemoveItemFromPaneArgs args);

/// <summary>
///     Fired when a skill is removed from the skill pane.
/// </summary>
public delegate void RemoveSkillFromPaneHandler(RemoveSkillFromPaneArgs args);

/// <summary>
///     Fired when a spell is removed from the spell pane.
/// </summary>
public delegate void RemoveSpellFromPaneHandler(RemoveSpellFromPaneArgs args);

/// <summary>
///     Fired when the player's own profile is received.
/// </summary>
public delegate void SelfProfileHandler(SelfProfileArgs args);

/// <summary>
///     Fired when a system message is received (yellow text, overhead, etc.).
/// </summary>
public delegate void ServerMessageHandler(ServerMessageArgs args);

/// <summary>
///     Fired when a sound or music track should play.
/// </summary>
public delegate void SoundHandler(SoundArgs args);

/// <summary>
///     Fired when a world list (online players) is received.
/// </summary>
public delegate void WorldListHandler(WorldListArgs args);

/// <summary>
///     Fired when the world map should be displayed.
/// </summary>
public delegate void WorldMapHandler(WorldMapArgs args);
#endregion