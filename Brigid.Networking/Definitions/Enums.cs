namespace Brigid.Networking.Definitions;

/// <summary>
///     Represents the current phase of the client's connection to the server.
/// </summary>
public enum ConnectionState
{
    /// <summary>
    ///     Not connected to any server.
    /// </summary>
    Disconnected,

    /// <summary>
    ///     TCP connection is being established.
    /// </summary>
    Connecting,

    /// <summary>
    ///     Connected to the lobby server, performing handshake (Version, ConnectionInfo, ServerTable).
    /// </summary>
    Lobby,

    /// <summary>
    ///     Connected to the login server, awaiting authentication.
    /// </summary>
    Login,

    /// <summary>
    ///     Connected to the world server, game is active.
    /// </summary>
    World
}

[Flags]
public enum WorldEntryState : byte
{
    None = 0,
    UserId = 1 << 0,
    MapInfo = 1 << 1,
    MapLoaded = 1 << 2,
    MapChangeComplete = 1 << 3,
    Location = 1 << 4,
    Attributes = 1 << 5,

    AllRequired = UserId | MapInfo | MapLoaded | MapChangeComplete | Location | Attributes
}