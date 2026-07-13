#region
using System.Runtime;
using System.Runtime.CompilerServices;
using Brigid;
using Brigid.Data;
using Brigid.Networking;
#endregion

NoticeDebugLog.Reset();
NoticeDebugLog.Write("Program.Main entered");

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