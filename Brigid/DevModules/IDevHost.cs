#region
using Brigid.Networking;
using Brigid.Screens;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.DevModules;

/// <summary>
///     The view of the client that a dev module gets (BRIG-29). Every member is used on the game thread. The host raises
///     each event to each listener in turn. An exception from a listener goes to the log, the host removes that listener,
///     and the other listeners and the client continue. The host removes a module's listeners when the module unloads.
/// </summary>
public interface IDevHost
{
    /// <summary>The game.</summary>
    ChaosGame Game { get; }

    /// <summary>The screen stack. <see cref="ScreenManager.ActiveScreen" /> is the screen that is shown.</summary>
    ScreenManager Screens { get; }

    /// <summary>The UI input dispatch.</summary>
    InputDispatcher Dispatcher { get; }

    /// <summary>The connection. See the contract on <see cref="IDevModule" /> for what a module can send.</summary>
    ConnectionManager Connection { get; }

    /// <summary>
    ///     The folder for the files that a module writes: <c>{AppPaths.DevModulesDir}/output</c>. The host makes it when a
    ///     module reads this property. A module writes to a subfolder with its own name.
    /// </summary>
    string OutputDirectory { get; }

    /// <summary>Writes a line to the client log, with the prefix <c>[dev-module]</c>.</summary>
    void Log(string message);

    /// <summary>
    ///     Raised after the active screen changes, with the new screen. A panel that a module added to the old root is
    ///     disposed with that root, so the module adds it again to the new root here.
    /// </summary>
    event Action<IScreen?>? ScreenChanged;

    /// <summary>
    ///     Raised at the start of each <c>ChaosGame.Update</c>, before the client reads the input of that update. A module
    ///     that gives input pushes it here, so that the input arrives in the same update. The game uses a fixed time step,
    ///     so more than one update can run before one draw.
    /// </summary>
    event Action<GameTime>? UpdateStarted;
}
