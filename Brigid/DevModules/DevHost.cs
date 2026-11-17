#region
using Brigid.Data;
using Brigid.Networking;
using Brigid.Screens;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.DevModules;

/// <summary>
///     The client's one <see cref="IDevHost" />. It raises <see cref="ScreenChanged" /> after each
///     <see cref="ScreenManager.ScreenChanged" />, and <c>ChaosGame.Update</c> calls <see cref="RaiseUpdateStarted" />.
///     Each module gets a <see cref="ModuleHost" /> in front of it.
/// </summary>
internal sealed class DevHost : IDevHost
{
    private readonly string OutputPath = Path.Combine(AppPaths.DevModulesDir, "output");
    private Action<IScreen?>? ScreenListeners;
    private Action<GameTime>? UpdateListeners;

    public DevHost(ChaosGame game)
    {
        Game = game;

        //a listener that calls Switch again gets the newest screen, not the one that this change showed
        game.Screens.ScreenChanged += _ => DevModuleLoader.RaiseEach(ref ScreenListeners, () => Game.Screens.ActiveScreen, Log, nameof(ScreenChanged));
    }

    public ChaosGame Game { get; }

    public ScreenManager Screens => Game.Screens;

    public InputDispatcher Dispatcher => Game.Dispatcher;

    public ConnectionManager Connection => Game.Connection;

    public string OutputDirectory
    {
        get
        {
            DataDiagnostics.Try(() => Directory.CreateDirectory(OutputPath), "create the dev module output folder");

            return OutputPath;
        }
    }

    public void Log(string message) => NoticeDebugLog.Write($"[dev-module] {message}");

    public event Action<IScreen?>? ScreenChanged
    {
        add => ScreenListeners += value;
        remove => ScreenListeners -= value;
    }

    public event Action<GameTime>? UpdateStarted
    {
        add => UpdateListeners += value;
        remove => UpdateListeners -= value;
    }

    /// <summary>Raises <see cref="UpdateStarted" />. Called by <c>ChaosGame.Update</c>, before the input of the update is read.</summary>
    internal void RaiseUpdateStarted(GameTime gameTime) => DevModuleLoader.RaiseEach(ref UpdateListeners, () => gameTime, Log, nameof(UpdateStarted));
}
