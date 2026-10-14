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
    private static string RuntimeIdentifier => RuntimeInformation.RuntimeIdentifier;

    private static string Platform => RuntimeIdentifier.Split('-')[0];

    //NuGet puts native assets under a portable RID (linux-x64, osx-arm64). The host RID can be distro-specific
    //(ubuntu24.04-x64 on Linux Mint 22), and no package ships a folder for it.
    private static string PortableRuntimeIdentifier
        => GetPortableRuntimeIdentifier(
            OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux",
            RuntimeInformation.ProcessArchitecture);

    private static readonly Dictionary<string, List<string>> MacLibraries = new()
    {
        { SdlMixer.LIBRARY_NAME, ["libSDL2_mixer-2.0.0.dylib", "libSDL2_mixer.dylib"] },
        { "SDL2", ["libSDL2-2.0.0.dylib", "libSDL2.dylib"]}
    };

    private static readonly Dictionary<string, List<string>> LinuxLibraries = new()
    {
        { SdlMixer.LIBRARY_NAME, ["libSDL2_mixer-2.0.so.0", "libSDL2_mixer.so.0", "libSDL2_mixer.so"] },
        { "SDL2", ["libSDL2-2.0.so.0", "libSDL2.so.0", "libSDL2.so"]}
    };

    private static readonly Dictionary<string, List<string>> WinLibraries = new()
    {
        { SdlMixer.LIBRARY_NAME, ["SDL2_mixer.dll"] },
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

        var libs = GetCandidates(libraryName);
        if (libs is not null)
        {
            if (TryLoadLibrary(libs, out handle))
                return handle;
        }

        // Fall back to library name resolution as a last chance attempt. Only the failure is worth a
        // line — a successful resolve is the norm, and the resident-module audit (LogLoadedSdlModules)
        // covers the split-SDL2 diagnostic, so the per-resolution trace here was pure log spam.
        if (!NativeLibrary.TryLoad(libraryName, out handle))
            NoticeDebugLog.Write(
                $"DllResolver: {libraryName} not found! host RID {RuntimeIdentifier}, portable RID {PortableRuntimeIdentifier}, "
                + $"probed: {string.Join(", ", GetLoadCandidates(libs ?? [], GetProbePaths))}");
        return handle;
    }

    /// <summary>
    ///     Loads <paramref name="libraryName" /> the way its p/invokes do: <see cref="ImportResolver" />, then the
    ///     runtime's default search, which a p/invoke falls back to when the resolver returns zero.
    ///     <see cref="NativeLibrary.TryLoad(string, Assembly, DllImportSearchPath?, out IntPtr)" /> alone skips the
    ///     registered resolver, so it can miss the bundled copy. On failure, logs the loader's error for the first
    ///     bundled candidate that exists, which names a missing dependency the probe-path list cannot.
    /// </summary>
    internal static bool TryResolve(string libraryName)
    {
        var assembly = typeof(DllResolver).Assembly;

        if ((ImportResolver(libraryName, assembly, null) != IntPtr.Zero)
            || NativeLibrary.TryLoad(libraryName, assembly, null, out _))
            return true;

        LogLoadError(libraryName);

        return false;
    }

    private static List<string>? GetCandidates(string libraryName)
    {
        var candidates = OperatingSystem.IsMacOS() ? MacLibraries :
            OperatingSystem.IsLinux() ? LinuxLibraries : WinLibraries;

        return candidates.GetValueOrDefault(libraryName);
    }

    private static void LogLoadError(string libraryName)
    {
        var path = (GetCandidates(libraryName) ?? []).SelectMany(GetProbePaths).FirstOrDefault(File.Exists);

        if (path is null)
            return;

        //TryLoad discards the loader's message (dlerror / LoadLibrary), and Load is the only API that surfaces it. A
        //library for the wrong architecture throws BadImageFormatException instead of DllNotFoundException
        try
        {
            NativeLibrary.Free(NativeLibrary.Load(path));
        } catch (Exception e) when (e is DllNotFoundException or BadImageFormatException)
        {
            NoticeDebugLog.Write($"DllResolver: {libraryName} at {path} failed to load: {e.Message}");
        }
    }

    private static bool TryLoadLibrary(List<string> libraryNames, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        foreach (var candidate in GetLoadCandidates(libraryNames, GetProbePaths))
        {
            if (NativeLibrary.TryLoad(candidate, out handle))
                return true;
        }
        return false;
    }

    /// <summary>
    ///     The load order for a library: every bundled path of every name, then each name alone, which the system loader
    ///     searches for.
    /// </summary>
    /// <remarks>
    ///     A Linux distribution package installs only the versioned name (<c>libSDL2_mixer-2.0.so.0</c>). The unversioned
    ///     <c>libSDL2_mixer.so</c> is in the <c>-dev</c> package, and the last-chance load of the bare library name never
    ///     finds the versioned file. Without the bare names, a system mixer does not load when the bundled mixer cannot
    ///     load its codecs (Ubuntu 22.04, Fedora). The bundled paths come first, so a bundled copy that loads is always the
    ///     copy in use.
    /// </remarks>
    internal static IEnumerable<string> GetLoadCandidates(
        IReadOnlyList<string> libraryNames,
        Func<string, IEnumerable<string>> getProbePaths)
        => libraryNames.SelectMany(getProbePaths).Concat(libraryNames);

    private static IEnumerable<string> GetProbePaths(string libraryName)
    {
        var baseDir = AppContext.BaseDirectory;

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
            yield return Path.Combine(baseDir, "runtimes", RuntimeIdentifier, "native", libraryName);
            yield return Path.Combine(baseDir, libraryName);
            yield return Path.Combine(baseDir, "runtimes", Platform, "native", libraryName);
            yield break;
        }

        foreach (var path in GetUnixProbePaths(baseDir, libraryName, RuntimeIdentifier, PortableRuntimeIdentifier))
            yield return path;
    }

    /// <summary>
    ///     The macOS/Linux probe order: the app root, then <c>runtimes/{host RID}/native</c>, then
    ///     <c>runtimes/{portable RID}/native</c> when the two RIDs differ, then <c>runtimes/{portable OS}/native</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The app root comes first. A self-contained publish puts MonoGame's SDL2 in the app root, and MonoGame loads
    ///         it from there. If the resolver loads SDL2 from a <c>runtimes/</c> copy, macOS dyld (which dedupes by path)
    ///         keeps two SDL2 images with separate state. SDL_GetMouseState then reads an instance that has no window, and
    ///         the mouse does not work. Loading the app-root file keeps one shared instance.
    ///     </para>
    ///     <para>
    ///         A framework-dependent run has no SDL2 in the app root, so the probe continues to <c>runtimes/</c>. There the
    ///         portable RID is the folder that exists, and it is the folder MonoGame loads from. The resolver loads each
    ///         library from where the build put it. It never copies a library.
    ///     </para>
    /// </remarks>
    internal static IEnumerable<string> GetUnixProbePaths(string baseDir, string libraryName, string hostRid, string portableRid)
    {
        yield return Path.Combine(baseDir, libraryName);
        yield return Path.Combine(baseDir, "runtimes", hostRid, "native", libraryName);

        if (!string.Equals(hostRid, portableRid, StringComparison.Ordinal))
            yield return Path.Combine(baseDir, "runtimes", portableRid, "native", libraryName);

        yield return Path.Combine(baseDir, "runtimes", portableRid.Split('-')[0], "native", libraryName);
    }

    /// <summary>The portable NuGet RID for an OS prefix and a process architecture, for example <c>linux-arm64</c>.</summary>
    internal static string GetPortableRuntimeIdentifier(string osPrefix, Architecture architecture)
    {
        var arch = architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => architecture.ToString().ToLowerInvariant()
        };

        return $"{osPrefix}-{arch}";
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
