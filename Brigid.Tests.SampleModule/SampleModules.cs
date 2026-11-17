#region
using Brigid.DevModules;
#endregion

namespace Brigid.Tests.SampleModule;

/// <summary>A module that loads.</summary>
public sealed class SampleDevModule : IDevModule
{
    private IDevHost? Host;

    public string Name => "Sample";

    public string Description => "A module for the loader tests.";

    public IReadOnlyList<DevAction> Actions => [];

    public void Initialize(IDevHost host)
    {
        Host = host;
        host.ScreenChanged += OnScreenChanged;
        host.Log("sample: initialized");
    }

    private static void OnScreenChanged(Screens.IScreen? screen) { }

    public void Shutdown() => Host?.Log("sample: shut down");
}

/// <summary>A module that throws in Initialize. The loader skips it and shuts it down.</summary>
public sealed class ThrowingDevModule : IDevModule
{
    private IDevHost? Host;

    public string Name => "Throwing";

    public string Description => "A module that fails to start.";

    public IReadOnlyList<DevAction> Actions => [];

    public void Initialize(IDevHost host)
    {
        Host = host;

        //a module that fails part way has listeners already; the loader must remove them
        host.UpdateStarted += _ => host.Log("throwing: still running");

        throw new InvalidOperationException("boom");
    }

    public void Shutdown() => Host?.Log("throwing: shut down");
}

/// <summary>A module with no constructor without parameters. The loader does not make it.</summary>
public sealed class NoDefaultConstructorDevModule(string name) : IDevModule
{
    public string Name => name;

    public string Description => "";

    public IReadOnlyList<DevAction> Actions => [];

    public void Initialize(IDevHost host) => throw new InvalidOperationException("the loader must not make this module");

    public void Shutdown() { }
}

/// <summary>An abstract module with a public constructor, so that only the abstract check stops the loader.</summary>
public abstract class AbstractDevModule : IDevModule
{
    public AbstractDevModule() { }

    public abstract string Name { get; }

    public string Description => "";

    public IReadOnlyList<DevAction> Actions => [];

    public void Initialize(IDevHost host) => throw new InvalidOperationException("the loader must not make this module");

    public void Shutdown() { }
}
