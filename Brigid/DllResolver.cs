#region
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Brigid.Networking;
using Brigid.Networking.Definitions;

#endregion

namespace Brigid;

// Handle native resolution of various cross-platform libraries needed (like sdl / sdl_mixer)
internal static class DllResolver
{
    private static string Architecture => RuntimeInformation.ProcessArchitecture.ToString();
    private static string RuntimeIdentifier => RuntimeInformation.RuntimeIdentifier;

    private static string Platform => RuntimeIdentifier.Split('-')[0];

    private static readonly Dictionary<string, List<string>> MacLibraries = new()
    {
        { "SDL2_mixer", ["libSDL2_mixer-2.0.0.dylib", "libSDL2_mixer.dylib"] },
        { "SDL2", ["libSDL2-2.0.0.dylib", "libSDL2.dylib"]}
    };

    private static readonly Dictionary<string, List<string>> LinuxLibraries = new()
    {
        { "SDL2_mixer", ["libSDL2_mixer-2.0.so.0", "libSDL2_mixer.so.0", "libSDL2_mixer.so"] },
        { "SDL2", ["libSDL2-2.0.so.0", "libSDL2.so.0", "libSDL2.so"]}
    };

    private static readonly Dictionary<string, List<string>> WinLibraries = new()
    {
        { "SDL2_mixer", ["SDL2_mixer.dll"] },
        { "SDL2", ["SDL2.dll"]}
    };

    //runs once at module load — before Main, before any static constructor in this assembly,
    //even before [LibraryImport] calls from other types' static initializers. guarantees the
    //resolver is in place no matter what triggers the first p/invoke.
    [ModuleInitializer]
    internal static void Initialize()
        => NativeLibrary.SetDllImportResolver(typeof(DllResolver).Assembly, ImportResolver);

    public static IntPtr ImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        IntPtr handle;
        NoticeDebugLog.Write($"DllResolver: resolving library: {libraryName} in {assembly.GetName()}: arch {Architecture}");

        var candidates = OperatingSystem.IsMacOS() ? MacLibraries :
            OperatingSystem.IsLinux() ? LinuxLibraries : WinLibraries;
        if (candidates.TryGetValue(libraryName, out var libs))
        {
            if (TryLoadLibrary(libs, out handle))
            {
                NoticeDebugLog.Write($"DllResolver: {libraryName} resolved successfully");
                return handle;
            }
        }
        
        // Fall back to library name resolution as a last chance attempt
        NoticeDebugLog.Write(NativeLibrary.TryLoad(libraryName, out handle)
            ? $"DllResolver: {libraryName} resolved successfully"
            : $"DllResolver: {libraryName} not found!");
        return handle;
    }

    private static bool TryLoadLibrary(List<string> libraryNames, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        foreach (var libraryName in libraryNames)
        {
            // The app base directory must be tried FIRST. In a self-contained publish the
            // runtime flattens MonoGame's SDL2 to the app root, and MonoGame loads it from
            // there. If we instead load SDL2 from a runtimes/<rid>/native copy, macOS dyld
            // (which dedupes by path) ends up with two separate SDL2 images with independent
            // state — our SDL_GetMouseState then queries an instance that has no window, so
            // the mouse appears dead. Loading the same app-root file MonoGame uses keeps it a
            // single shared instance. (In a framework-dependent run there's no SDL2 at the
            // root, so this falls through to the runtimes/ paths below, same as before.)
            var appRoot = Path.Combine(AppContext.BaseDirectory, libraryName);

            // Then arch specific, then platform-independent native.
            var archSpecific = Path.Combine(AppContext.BaseDirectory, "runtimes",
                RuntimeIdentifier, "native", libraryName);

            var independent = Path.Combine(AppContext.BaseDirectory, "runtimes",
                Platform, "native", libraryName);

            if (NativeLibrary.TryLoad(appRoot, out handle) ||
                NativeLibrary.TryLoad(archSpecific, out handle) ||
                NativeLibrary.TryLoad(independent, out handle)) return true;
            NoticeDebugLog.Write($"DllResolver: tried {libraryName}: {appRoot}");
            NoticeDebugLog.Write($"DllResolver: tried {libraryName}: {archSpecific}");
            NoticeDebugLog.Write($"DllResolver: tried {libraryName}: {independent}");
            continue;
        }
        return false;
    }



}
