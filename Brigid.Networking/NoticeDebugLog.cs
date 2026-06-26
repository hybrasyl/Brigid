namespace Brigid.Networking;

// Temporary diagnostic logger for the Hybrasyl login-notice investigation.
// Writes to notice-debug.log in the app base directory and also to stdout.
public static class NoticeDebugLog
{
    private static readonly Lock WriteLock = new();
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "notice-debug.log");

    public static void Reset()
    {
        using var scope = WriteLock.EnterScope();
        try { File.WriteAllText(LogPath, string.Empty); } catch { }
    }

    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";

        using var scope = WriteLock.EnterScope();
        Console.Write(line);
        try { File.AppendAllText(LogPath, line); } catch { }
    }
}
