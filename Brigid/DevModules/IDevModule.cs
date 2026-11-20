namespace Brigid.DevModules;

/// <summary>
///     A developer tool that the client loads from <see cref="Data.AppPaths.DevModulesDir" /> when <c>BRIGID_DEV_MODULES</c>
///     is set (BRIG-29). A dev module is its own assembly, and it never ships to players. Make the class public, with a
///     public constructor with no parameters. Reference the client with <c>Private="false"</c>, so that <c>Brigid.dll</c>
///     is not copied beside the module. The other client assemblies (<c>Brigid.Data</c> and the others) can still be in
///     the module's output; the loader skips a file with the name of a client or runtime assembly. The loader reads the file into memory, so <c>Assembly.Location</c> is empty in a module.
///     <para>
///         The contract. A module can read the state of the client and show debug UI. It can also give input that a
///         player could give: keys, text and mouse events through SDL, which go through <see cref="InputBuffer" /> and the
///         <see cref="InputDispatcher" /> as a player's input does (BRIG-184). A module sends no packet, with one
///         exception: board read requests, whose replies the module can take before the board UI does (BRIG-80). A module
///         never sends a privileged command; the admin command surface stays out of the client (HS-1578).
///     </para>
///     <para>
///         This rule is a convention, not a sandbox. The client does not stop a module that breaks it, and a fork can
///         remove the <c>BRIGID_DEV_MODULES</c> gate.
///     </para>
/// </summary>
public interface IDevModule
{
    /// <summary>The name in the dev menu and in the log.</summary>
    string Name { get; }

    /// <summary>One line about the module.</summary>
    string Description { get; }

    /// <summary>The actions that the dev menu shows for the module. Read after <see cref="Initialize" />.</summary>
    IReadOnlyList<DevAction> Actions { get; }

    /// <summary>
    ///     Called one time, after the assets load and before the first lobby screen. Subscribe to the events of
    ///     <paramref name="host" /> here.
    /// </summary>
    void Initialize(IDevHost host);

    /// <summary>
    ///     Called one time when the client closes. Remove every subscription to a long-lived object (the host,
    ///     <c>WorldState</c>, <c>ConnectionManager</c>), or the module stays in memory.
    /// </summary>
    void Shutdown();
}
