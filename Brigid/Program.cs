#region
using System.Runtime;
using System.Runtime.CompilerServices;
using Brigid;
using Brigid.Data;
using Brigid.Networking;
#endregion

//a temporary app folder (BRIGID_APP_ROOT, the smoke tests) keeps its own log, so the run does not touch the developer's
//log. The log is not open yet, so a folder that cannot be made is written once the log is
var appFolderReady = !AppPaths.IsAppRootOverridden || DataDiagnostics.Try(() => Directory.CreateDirectory(AppPaths.AppRoot), "create the app folder");

if (AppPaths.IsAppRootOverridden && appFolderReady)
    NoticeDebugLog.UseFolder(AppPaths.AppRoot);

NoticeDebugLog.Reset();
NoticeDebugLog.Write("Program.Main entered");

if (!appFolderReady)
    NoticeDebugLog.Write($"[paths] could not create {AppPaths.AppRoot}; the log stays in the app base directory");

//route data-layer IO diagnostics (best-effort file writes that are caught, never rethrown) into the notice log
DataDiagnostics.Log = NoticeDebugLog.Write;

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    var ex = e.ExceptionObject as Exception;
    NoticeDebugLog.Write($"!!! UNHANDLED {ex?.GetType().Name}: {ex?.Message}");
    NoticeDebugLog.Write($"stack: {ex?.StackTrace}");
    if (ex?.InnerException is { } inner)
    {
        NoticeDebugLog.Write($"inner {inner.GetType().Name}: {inner.Message}");
        NoticeDebugLog.Write($"inner stack: {inner.StackTrace}");
    }
};

System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
{
    NoticeDebugLog.Write($"!!! UNOBSERVED TASK {e.Exception.GetType().Name}: {e.Exception.Message}");
    NoticeDebugLog.Write($"stack: {e.Exception.StackTrace}");
};

//before anything reads AppPaths.AppRoot: GlobalSettings, the launcher config and the client settings
AppPaths.MigrateLegacyVendorFolder();

//must run before ChaosGame constructs its GraphicsDeviceManager (which initializes SDL and creates the window)
Sdl.SDL_SetHint(Sdl.SDL_HINT_WINDOWS_DPI_AWARENESS, "permonitorv2");
Sdl.SDL_SetHint(Sdl.SDL_HINT_MOUSE_FOCUS_CLICKTHROUGH, "1");

GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

CrashLogger.Install();
RuntimeHelpers.RunClassConstructor(typeof(GlobalSettings).TypeHandle);
NoticeDebugLog.Write("GlobalSettings initialized");

using var game = new ChaosGame();

//MonoGame loaded SDL2 inside the ChaosGame ctor; verify it bound the same instance we did
DllResolver.LogLoadedSdlModules();

game.Run();