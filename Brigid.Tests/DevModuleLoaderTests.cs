#region
using System.Reflection;
using Brigid.DevModules;
using Brigid.Networking;
using Brigid.Screens;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-29 loader, with the real module assembly of Brigid.Tests.SampleModule (built beside the tests into
///     <c>devmodule-sample</c>). One module loads, one that throws is skipped and shut down, and the client's own types
///     come from the default context.
/// </summary>
[Collection("DevModules")]
public sealed class DevModuleLoaderTests : IDisposable
{
    private readonly string Root = Directory.CreateTempSubdirectory("brigid-devmodules-").FullName;
    private readonly FakeDevHost Host = new();

    public DevModuleLoaderTests() => DevModuleLoader.ResetForTest();

    public void Dispose()
    {
        DevModuleLoader.ResetForTest();
        Directory.Delete(Root, true);
    }

    [Fact]
    public void AModuleLoadsAndAModuleThatThrowsIsSkipped()
    {
        CopySampleModule("sample.dll");

        var count = DevModuleLoader.LoadFrom(Root, Host);

        Assert.Equal(1, count);
        var module = Assert.Single(DevModuleLoader.Modules);
        Assert.Equal("Sample", module.Name);
        Assert.Contains("sample: initialized", Host.Lines);
        Assert.Contains(Host.Lines, line => line.Contains("Throwing: Initialize failed") && line.Contains("boom"));
        Assert.Contains("throwing: shut down", Host.Lines);
        Assert.Equal(0, Host.UpdateListenerCount);
        Assert.Equal(1, Host.ScreenListenerCount);
        Assert.DoesNotContain(Host.Lines, line => line.Contains("NoDefaultConstructorDevModule") || line.Contains("AbstractDevModule"));
    }

    [Fact]
    public void TheModuleSeesTheClientTypesOfTheDefaultContext()
    {
        CopySampleModule("sample.dll");

        //a module built without Private=false has a copy of the client beside it; the copy must not load
        File.Copy(typeof(IDevModule).Assembly.Location, Path.Combine(Root, "Brigid.dll"));

        DevModuleLoader.LoadFrom(Root, Host);

        var module = Assert.Single(DevModuleLoader.Modules);
        Assert.Contains("skipped Brigid.dll: it is an assembly of the client or the runtime", Host.Lines);
        Assert.Equal("Brigid.Tests.SampleModule", module.GetType().Assembly.GetName().Name);
        Assert.Same(typeof(IDevModule).Assembly, module.GetType().GetInterfaces().Single().Assembly);
    }

    [Fact]
    public void UnloadAllShutsDownEachModule()
    {
        CopySampleModule("sample.dll");
        DevModuleLoader.LoadFrom(Root, Host);

        DevModuleLoader.UnloadAll();

        Assert.Empty(DevModuleLoader.Modules);
        Assert.Contains("sample: shut down", Host.Lines);
        Assert.Equal(0, Host.ScreenListenerCount);
    }

    [Fact]
    public void AFileThatIsNotAnAssemblyIsSkipped()
    {
        File.WriteAllBytes(Path.Combine(Root, "junk.dll"), [1, 2, 3, 4]);

        Assert.Equal(0, DevModuleLoader.LoadFrom(Root, Host));
        Assert.Contains(Host.Lines, line => line.StartsWith("skipped junk.dll", StringComparison.Ordinal));
    }

    [Fact]
    public void AFolderThatDoesNotExistLoadsNothing()
    {
        Assert.Equal(0, DevModuleLoader.LoadFrom(Path.Combine(Root, "missing"), Host));
        Assert.Empty(DevModuleLoader.Modules);
    }

    [Fact]
    public void GuardLogsAnExceptionAndReturnsFalse()
    {
        var lines = new List<string>();

        Assert.False(DevModuleLoader.Guard(lines.Add, "the action", () => throw new InvalidOperationException("bad")));
        Assert.True(DevModuleLoader.Guard(lines.Add, "the action", () => { }));
        var line = Assert.Single(lines);
        Assert.StartsWith("the action failed: System.InvalidOperationException: bad", line);
    }

    [Fact]
    public void RaiseEachRunsTheNextListenerAndRemovesTheOneThatThrew()
    {
        var lines = new List<string>();
        var seen = new List<int>();
        var throws = 0;
        Action<int>? handlers = null;
        handlers += _ =>
        {
            throws++;

            throw new InvalidOperationException("first");
        };
        handlers += seen.Add;

        DevModuleLoader.RaiseEach(ref handlers, () => 7, lines.Add, "Test");
        DevModuleLoader.RaiseEach(ref handlers, () => 8, lines.Add, "Test");

        Assert.Equal([7, 8], seen);
        Assert.Equal(1, throws);
        Assert.Contains("removed the Test listener that failed", lines);
    }

    //a module type whose members name an assembly that is not there throws in the type check; the client must still start
    [Fact]
    public void ATypeWhoseCheckThrowsIsSkippedAndTheOthersLoad()
    {
        var host = new FakeDevHost();

        var types = DevModuleLoader.SelectModuleTypes([new MissingDependencyType(), typeof(ListedModule), typeof(string)], "module.dll", host);

        Assert.Equal([typeof(ListedModule)], types);
        Assert.Contains(host.Lines, line => line.Contains("the type check failed") && line.Contains(nameof(FileNotFoundException)));
    }

    private void CopySampleModule(string fileName)
        => File.Copy(Path.Combine(AppContext.BaseDirectory, "devmodule-sample", "Brigid.Tests.SampleModule.dll"), Path.Combine(Root, fileName));
}

/// <summary>A host that records the log, for the dev module tests.</summary>
public sealed class FakeDevHost : IDevHost
{
    public List<string> Lines { get; } = [];

    public ChaosGame Game => throw new NotSupportedException();

    public ScreenManager Screens => throw new NotSupportedException();

    public InputDispatcher Dispatcher => throw new NotSupportedException();

    public ConnectionManager Connection => throw new NotSupportedException();

    public string OutputDirectory => throw new NotSupportedException();

    public int ScreenListenerCount => ScreenListeners?.GetInvocationList().Length ?? 0;

    public int UpdateListenerCount => UpdateListeners?.GetInvocationList().Length ?? 0;

    private Action<IScreen?>? ScreenListeners;
    private Action<GameTime>? UpdateListeners;

    public void Log(string message) => Lines.Add(message);

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
}

/// <summary>A dev module type for the type check test.</summary>
public sealed class ListedModule : IDevModule
{
    public string Name => "listed";

    public string Description => string.Empty;

    public IReadOnlyList<DevAction> Actions => [];

    public void Initialize(IDevHost host) { }

    public void Shutdown() { }
}

/// <summary>A type whose constructor lookup throws, as a module type that names a missing assembly does.</summary>
internal sealed class MissingDependencyType() : TypeDelegator(typeof(ListedModule))
{
    protected override ConstructorInfo GetConstructorImpl(
        BindingFlags bindingAttr,
        Binder? binder,
        CallingConventions callConvention,
        Type[] types,
        ParameterModifier[]? modifiers)
        => throw new FileNotFoundException("Could not load file or assembly 'Missing.Dependency'");
}
