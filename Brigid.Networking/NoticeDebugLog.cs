namespace Brigid.Networking;

// Temporary diagnostic logger for the Hybrasyl login-notice investigation.
// Writes to notice-debug.log in the app base directory (or the folder that UseFolder gives) and also to stdout.
public static class NoticeDebugLog
{
    private const string FILE_NAME = "notice-debug.log";
    private static readonly Lock WriteLock = new();
    private static string LogPath = Path.Combine(AppContext.BaseDirectory, FILE_NAME);

    /// <summary>
    ///     Writes the log in <paramref name="folder" /> in place of the app base directory. <c>Program</c> calls it before
    ///     the first line when <c>BRIGID_APP_ROOT</c> is set, so a smoke run does not write over the developer's own log.
    /// </summary>
    public static void UseFolder(string folder)
    {
        using var scope = WriteLock.EnterScope();
        LogPath = Path.Combine(folder, FILE_NAME);
    }

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
