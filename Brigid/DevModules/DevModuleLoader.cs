#region
using System.Reflection;
using Brigid.Data;
#endregion

namespace Brigid.DevModules;

/// <summary>
///     Loads the dev modules (BRIG-29). Each <c>*.dll</c> at the top of <see cref="AppPaths.DevModulesDir" /> gets its own
///     collectible <see cref="DevModuleLoadContext" />, and each public class in it that implements
///     <see cref="IDevModule" /> and has a public constructor with no parameters is made and initialized. A file that is a
///     copy of a client or runtime assembly is skipped. A file that does not load, a class that throws in its
///     constructor, and a module that throws in <see cref="IDevModule.Initialize" /> are each skipped and logged; the
///     client always starts. With <c>BRIGID_DEV_MODULES</c> not set, nothing loads and nothing changes.
/// </summary>
internal static class DevModuleLoader
{
    private static readonly List<IDevModule> LoadedModules = [];
    private static readonly List<ModuleHost> ModuleHosts = [];
    private static readonly List<DevModuleLoadContext> Contexts = [];
    private static Action<string> Log = static _ => { };

    /// <summary>The modules that loaded, in load order.</summary>
    internal static IReadOnlyList<IDevModule> Modules => LoadedModules;

    /// <summary>
    ///     Loads the modules when <see cref="GlobalSettings.DevModulesEnabled" /> is true, and returns their host. Returns
    ///     null, and does nothing, when it is false. Called one time, from <c>ChaosGame.FinishAssetInitialization</c>.
    /// </summary>
    internal static DevHost? Load(ChaosGame game) => Load(GlobalSettings.DevModulesEnabled, () => new DevHost(game), AppPaths.DevModulesDir);

    /// <summary>
    ///     Makes the host and loads the modules in <paramref name="directory" /> when <paramref name="enabled" /> is true.
    ///     Returns null, makes no host and reads no folder, when it is false. Internal for the tests.
    /// </summary>
    internal static THost? Load<THost>(bool enabled, Func<THost> makeHost, string directory) where THost : class, IDevHost
    {
        if (!enabled)
            return null;

        var host = makeHost();
        LoadFrom(directory, host);

        return host;
    }

    /// <summary>Loads the modules in <paramref name="directory" /> for <paramref name="host" />, and returns how many loaded.</summary>
    internal static int LoadFrom(string directory, IDevHost host)
    {
        Log = host.Log;

        if (!Directory.Exists(directory))
        {
            host.Log($"no folder {directory}, so no module loaded");

            return 0;
        }

        string[] files;

        try
        {
            files = Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            host.Log($"could not list {directory}: {ex.Message}");

            return 0;
        }

        var before = LoadedModules.Count;

        foreach (var path in files.Order(StringComparer.Ordinal))
        {
            //a module built without Private=false has copies of the client beside it; the client's own copy serves it
            if (DevModuleLoadContext.IsDefaultAssembly(Path.GetFileNameWithoutExtension(path)))
            {
                host.Log($"skipped {Path.GetFileName(path)}: it is an assembly of the client or the runtime");

                continue;
            }

            LoadAssembly(path, host);
        }

        var count = LoadedModules.Count - before;
        host.Log($"{count} module(s) loaded from {directory}");

        return count;
    }

    /// <summary>
    ///     Shuts down each module, in the opposite order of load, removes its listeners and unloads the contexts. Called
    ///     from <c>ChaosGame.UnloadContent</c>. An unload completes only when nothing holds the module, so it is best
    ///     effort.
    /// </summary>
    internal static void UnloadAll()
    {
        for (var i = LoadedModules.Count - 1; i >= 0; i--)
        {
            Guard(Log, "a module shut down", LoadedModules[i].Shutdown);
            ModuleHosts[i].Detach();
        }

        LoadedModules.Clear();
        ModuleHosts.Clear();

        foreach (var context in Contexts)
            context.Unload();

        Contexts.Clear();
        Log = static _ => { };
    }

    /// <summary>
    ///     Runs <paramref name="action" />, which is module code. An exception goes to <paramref name="log" /> in full, and
    ///     the method returns false. Module code is outside the client and must not stop it, so a catch of every exception
    ///     is correct here, as it is around the packet handlers in <c>ConnectionManager.ProcessPackets</c>.
    /// </summary>
    internal static bool Guard(Action<string> log, string what, Action action)
    {
        try
        {
            action();

            return true;
        } catch (Exception ex)
        {
            log($"{what} failed: {ex}");

            return false;
        }
    }

    /// <summary>
    ///     Raises <paramref name="handlers" /> one listener at a time, each through <see cref="Guard" />, with the value
    ///     that <paramref name="argument" /> gives at that time. A listener that throws is removed, so that it does not
    ///     fail at each update.
    /// </summary>
    internal static void RaiseEach<T>(ref Action<T>? handlers, Func<T> argument, Action<string> log, string eventName)
    {
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<Action<T>>())
            if (!Guard(log, $"a {eventName} listener", () => handler(argument())))
            {
                handlers -= handler;
                log($"removed the {eventName} listener that failed");
            }
    }

    private static void LoadAssembly(string path, IDevHost host)
    {
        var file = Path.GetFileName(path);
        var context = new DevModuleLoadContext(path);
        Assembly assembly;

        try
        {
            assembly = context.LoadModuleFile(path);
        } catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or IOException or UnauthorizedAccessException)
        {
            host.Log($"skipped {file}: {ex.Message}");
            context.Unload();

            return;
        }

        var loaded = 0;

        foreach (var type in FindModuleTypes(assembly, file, host))
            if (TryStart(type, file, host) is var (module, moduleHost))
            {
                LoadedModules.Add(module);
                ModuleHosts.Add(moduleHost);
                loaded++;
            }

        if (loaded > 0)
        {
            Contexts.Add(context);

            return;
        }

        host.Log($"{file} has no dev module that loads");
        context.Unload();
    }

    private static IEnumerable<Type> FindModuleTypes(Assembly assembly, string file, IDevHost host)
    {
        Type?[] types;

        try
        {
            types = assembly.GetTypes();
        } catch (ReflectionTypeLoadException ex)
        {
            host.Log($"some types of {file} did not load: {ex.LoaderExceptions.FirstOrDefault()?.Message}");
            types = ex.Types;
        }

        return SelectModuleTypes(types.OfType<Type>(), file, host);
    }

    /// <summary>
    ///     The public classes of <paramref name="types" /> that are dev modules and have a public constructor with no
    ///     parameters, in name order. The check of each type is module code: a type whose members name an assembly that is
    ///     not there throws while it is checked, so the check runs through <see cref="Guard" /> and skips that type.
    /// </summary>
    internal static IReadOnlyList<Type> SelectModuleTypes(IEnumerable<Type> types, string file, IDevHost host)
        => types.Where(type => IsModuleType(type, file, host))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToList();

    private static bool IsModuleType(Type type, string file, IDevHost host)
    {
        var isModule = false;

        Guard(
            host.Log,
            $"{type.FullName} in {file}: the type check",
            () => isModule = type is { IsClass: true, IsAbstract: false, IsVisible: true }
                             && typeof(IDevModule).IsAssignableFrom(type)
                             && (type.GetConstructor(Type.EmptyTypes) is not null));

        return isModule;
    }

    private static (IDevModule Module, ModuleHost Host)? TryStart(Type type, string file, IDevHost host)
    {
        IDevModule? module = null;

        if (!Guard(host.Log, $"{type.FullName} in {file}: the constructor", () => module = (IDevModule)Activator.CreateInstance(type)!))
            return null;

        var name = type.FullName;
        Guard(host.Log, $"{type.FullName} in {file}: Name", () => name = module!.Name);

        var moduleHost = new ModuleHost(host);

        if (!Guard(host.Log, $"{name}: Initialize", () => module!.Initialize(moduleHost)))
        {
            Guard(host.Log, $"{name}: Shutdown", module!.Shutdown);
            moduleHost.Detach();

            return null;
        }

        host.Log($"loaded {name} from {file}");

        return (module!, moduleHost);
    }

    /// <summary>Unloads every module and clears the state, for the tests.</summary>
    internal static void ResetForTest() => UnloadAll();
}
