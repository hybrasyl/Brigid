#region
using System.Collections.Generic;
using Chaos.Geometry.Abstractions.Definitions;
using DALib.Networking.Packets.Server;
#endregion

namespace Brigid.Networking.Definitions;

#region GameClient Delegates
/// <summary>
///     Fired when the client is disconnected from the server.
/// </summary>
public delegate void DisconnectedHandler();
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
public delegate void ServerTableReceivedHandler(IList<ServerEntry> servers);

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
