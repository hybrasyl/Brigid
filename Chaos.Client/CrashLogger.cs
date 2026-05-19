namespace Chaos.Client;

/// <summary>
///     Captures unhandled exceptions from both managed code and the task scheduler and writes them to a per-crash file
///     under <c>crashLogs/</c> next to the executable. Install this before any other code runs.
/// </summary>
internal static class CrashLogger
{
    private const string LOG_DIR = "crashLogs";

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => WriteLog("UnhandledException", e.ExceptionObject as Exception, e.IsTerminating);

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteLog("UnobservedTaskException", e.Exception, false);
        e.SetObserved();
    }

    private static void WriteLog(string source, Exception? exception, bool isTerminating)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, LOG_DIR);
            Directory.CreateDirectory(dir);

            //yyyy-MM-dd_HH-mm-ss avoids the colon characters that are illegal in Windows filenames
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var path = Path.Combine(dir, $"crashLog{timestamp}.log");

            using var writer = new StreamWriter(path, true);
            writer.WriteLine($"[{DateTime.Now:O}] {source} (IsTerminating: {isTerminating})");
            writer.WriteLine($"OS: {Environment.OSVersion}");
            writer.WriteLine($"Runtime: {Environment.Version}");
            writer.WriteLine();
            writer.WriteLine(exception?.ToString() ?? "(no exception details available)");
        } catch
        {
            //don't throw to avoid infinite recursion
        }
    }
}