#region
using Brigid.Networking;
using Brigid.Screens;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.DevModules;

/// <summary>
///     The host that one module gets: it passes each member on to the client's host, and it records the listeners that
///     the module adds. <see cref="Detach" /> removes them, when the module fails to start and when it unloads, so that a
///     module that is gone never runs again and does not keep its load context in memory.
/// </summary>
internal sealed class ModuleHost(IDevHost host) : IDevHost
{
    private readonly List<Action<IScreen?>> ScreenListeners = [];
    private readonly List<Action<GameTime>> UpdateListeners = [];

    public ChaosGame Game => host.Game;

    public ScreenManager Screens => host.Screens;

    public InputDispatcher Dispatcher => host.Dispatcher;

    public ConnectionManager Connection => host.Connection;

    public string OutputDirectory => host.OutputDirectory;

    public void Log(string message) => host.Log(message);

    public event Action<IScreen?>? ScreenChanged
    {
        add
        {
            if (value is null)
                return;

            host.ScreenChanged += value;
            ScreenListeners.Add(value);
        }

        remove
        {
            if (value is null)
                return;

            host.ScreenChanged -= value;
            ScreenListeners.Remove(value);
        }
    }

    public event Action<GameTime>? UpdateStarted
    {
        add
        {
            if (value is null)
                return;

            host.UpdateStarted += value;
            UpdateListeners.Add(value);
        }

        remove
        {
            if (value is null)
                return;

            host.UpdateStarted -= value;
            UpdateListeners.Remove(value);
        }
    }

    /// <summary>Removes every listener that the module added and did not remove.</summary>
    internal void Detach()
    {
        foreach (var listener in ScreenListeners)
            host.ScreenChanged -= listener;

        foreach (var listener in UpdateListeners)
            host.UpdateStarted -= listener;

        ScreenListeners.Clear();
        UpdateListeners.Clear();
    }
}
