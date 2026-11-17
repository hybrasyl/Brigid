#region
using System.Reflection;
using System.Runtime.Loader;
#endregion

namespace Brigid.DevModules;

/// <summary>
///     The collectible load context of one dev module file. An assembly of the client or of the runtime (one that the
///     default context has or can load) comes from the default context, so that the module sees the client's own types,
///     <see cref="IDevModule" /> among them. Any other dependency loads from the module's folder. A native library goes
///     through the client's <see cref="DllResolver" />. Each file is read into memory, so the file is not locked and a
///     developer can build it again while the client runs.
/// </summary>
internal sealed class DevModuleLoadContext(string modulePath)
    : AssemblyLoadContext($"devmodule:{Path.GetFileName(modulePath)}", isCollectible: true)
{
    private static readonly Lazy<HashSet<string>> DefaultAssemblyNames = new(ReadDefaultAssemblyNames);

    private readonly string ModuleDirectory = Path.GetDirectoryName(modulePath) ?? ".";

    /// <summary>Loads <paramref name="path" /> and its <c>.pdb</c>, when there is one, from memory.</summary>
    internal Assembly LoadModuleFile(string path)
    {
        using var assembly = new MemoryStream(File.ReadAllBytes(path));
        var pdbPath = Path.ChangeExtension(path, ".pdb");

        if (!File.Exists(pdbPath))
            return LoadFromStream(assembly);

        using var pdb = new MemoryStream(File.ReadAllBytes(pdbPath));

        return LoadFromStream(assembly, pdb);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not { } name || IsDefaultAssembly(name))
            return null;

        var candidate = Path.Combine(ModuleDirectory, name + ".dll");

        return File.Exists(candidate) ? LoadModuleFile(candidate) : null;
    }

    //a module's p/invoke (SDL2 for the test driver) finds the native library that the client loaded, not a second copy
    protected override nint LoadUnmanagedDll(string unmanagedDllName)
        => DllResolver.ImportResolver(unmanagedDllName, typeof(DllResolver).Assembly, null);

    /// <summary>True when <paramref name="name" /> is an assembly of the client or of the runtime.</summary>
    internal static bool IsDefaultAssembly(string name)
        => DefaultAssemblyNames.Value.Contains(name)
           || Default.Assemblies.Any(assembly => string.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase));

    //the client's and the runtime's assemblies: the trusted platform assemblies of the default context
    private static HashSet<string> ReadDefaultAssemblyNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string paths)
            foreach (var path in paths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                names.Add(Path.GetFileNameWithoutExtension(path));

        return names;
    }
}
