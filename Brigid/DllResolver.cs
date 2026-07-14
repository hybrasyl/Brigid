#region
using System.Diagnostics;
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

        var candidates = OperatingSystem.IsMacOS() ? MacLibraries :
            OperatingSystem.IsLinux() ? LinuxLibraries : WinLibraries;
        if (candidates.TryGetValue(libraryName, out var libs))
        {
            if (TryLoadLibrary(libs, out handle))
                return handle;
        }

        // Fall back to library name resolution as a last chance attempt. Only the failure is worth a
        // line — a successful resolve is the norm, and the resident-module audit (LogLoadedSdlModules)
        // covers the split-SDL2 diagnostic, so the per-resolution trace here was pure log spam.
        if (!NativeLibrary.TryLoad(libraryName, out handle))
            NoticeDebugLog.Write($"DllResolver: {libraryName} not found!");
        return handle;
    }

    private static bool TryLoadLibrary(List<string> libraryNames, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        foreach (var libraryName in libraryNames)
        {
            foreach (var candidate in GetProbePaths(libraryName))
            {
                if (NativeLibrary.TryLoad(candidate, out handle))
                    return true;
            }
        }
        return false;
    }

    private static IEnumerable<string> GetProbePaths(string libraryName)
    {
        var baseDir = AppContext.BaseDirectory;
        var appRoot = Path.Combine(baseDir, libraryName);
        var archSpecific = Path.Combine(baseDir, "runtimes", RuntimeIdentifier, "native", libraryName);
        var independent = Path.Combine(baseDir, "runtimes", Platform, "native", libraryName);

        if (OperatingSystem.IsWindows())
        {
            // Windows dedupes loaded modules strictly by FULL PATH, and Brigid binds SDL2
            // before MonoGame does (Program.cs sets SDL hints ahead of window creation), so
            // we must load the exact file MonoGame's FuncLoader will pick or the process
            // ends up with two independent SDL2 instances — MonoGame's owns the window and
            // ours never sees an event (dead input in packaged builds). Mirror MonoGame
            // 3.8.4.1's FuncLoader probe order — x64|x86 subfolder, runtimes/<rid>/native,
            // app root — and re-verify on any MonoGame upgrade. (FuncLoader anchors on the
            // entry assembly's directory, not AppContext.BaseDirectory; identical for every
            // layout we ship, diverges only under single-file publish.)
            yield return Path.Combine(baseDir, Environment.Is64BitProcess ? "x64" : "x86", libraryName);
            yield return archSpecific;
            yield return appRoot;
            yield return independent;
            yield break;
        }

        // macOS/Linux: the app base directory must be tried FIRST. In a self-contained
        // publish the runtime flattens MonoGame's SDL2 to the app root, and MonoGame loads
        // it from there. If we instead load SDL2 from a runtimes/<rid>/native copy, macOS
        // dyld (which dedupes by path) ends up with two separate SDL2 images with
        // independent state — our SDL_GetMouseState then queries an instance that has no
        // window, so the mouse appears dead. Loading the same app-root file MonoGame uses
        // keeps it a single shared instance. (In a framework-dependent run there's no SDL2
        // at the root, so this falls through to the runtimes/ paths below.)
        yield return appRoot;
        yield return archSpecific;
        yield return independent;
    }

    // One-shot diagnostic, called after MonoGame has loaded SDL2 and created the window: if
    // more than one same-named SDL2 image is resident, SDL state is split per-instance and
    // the input path is dead — surface that in the log instead of leaving it to symptom
    // debugging. Process.Modules is unsupported on macOS, where dyld path-dedup plus the
    // shared app-root probe already covers this.
    internal static void LogLoadedSdlModules()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            return;

        // Module enumeration can transiently fail (ERROR_PARTIAL_COPY) while native libs
        // are still loading on other threads; a diagnostic must never take down startup.
        try
        {
            var sdlModules = new List<string>();
            using var process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
                if (module.ModuleName.Contains("SDL2", StringComparison.OrdinalIgnoreCase))
                {
                    sdlModules.Add(module.FileName);
                    NoticeDebugLog.Write($"DllResolver: resident SDL module: {module.FileName}");
                }

            foreach (var duplicate in sdlModules
                         .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1))
                NoticeDebugLog.Write(
                    $"!!! DllResolver: {duplicate.Key} is loaded {duplicate.Count()} times — split SDL2 instances, input will be dead");
        } catch (Exception ex)
        {
            NoticeDebugLog.Write($"DllResolver: SDL module audit failed: {ex.GetType().Name}: {ex.Message}");
        }
    }



}
