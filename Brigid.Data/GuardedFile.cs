using System.Collections.Concurrent;

namespace Brigid.Data;

/// <summary>What a save of a <see cref="GuardedFile" /> did.</summary>
public enum SaveResult
{
    /// <summary>The file was written.</summary>
    Saved,

    /// <summary>
    ///     Nothing was written: the last read of the file failed, so the client holds no good copy of it, and its defaults
    ///     must not replace the file.
    /// </summary>
    Unreadable,

    /// <summary>The write failed, and the failure was logged.</summary>
    Failed
}

/// <summary>
///     One file of the player's settings that the client reads and saves. A read that fails (another program holds the
///     file, or it is denied) marks the file unreadable, and each save is refused while it is unreadable, so that defaults
///     never replace the player's file (BRIG-177). A later read that succeeds clears the mark. A file that reads but does
///     not parse is copied to <c>&lt;name&gt;.bad</c> (<see cref="MarkCorrupt" />), and its saves are allowed.
/// </summary>
/// <remarks>
///     The client does not support a hand edit of a file while it runs: the next save writes over it. A save that is
///     refused or whose write fails raises <see cref="NotSaved" /> one time for each file in a session, so that the client
///     can tell the player.
/// </remarks>
public sealed class GuardedFile
{
    private static readonly Lock NoticeGate = new();
    private static readonly HashSet<string> Noticed = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, GuardedFile> SessionFiles = new(StringComparer.Ordinal);

    /// <summary>
    ///     A file of one character, which the client reads again at the next login (<see cref="ReadAgainAtLogin" />). Make
    ///     a new one at each login, so that a file that could not be read gets another try.
    /// </summary>
    public GuardedFile(string path, string description) : this(path, description, readAgainAtLogin: true) { }

    private GuardedFile(string path, string description, bool readAgainAtLogin)
    {
        Path = path;
        Description = description;
        ReadAgainAtLogin = readAgainAtLogin;
    }

    /// <summary>
    ///     The one <see cref="GuardedFile" /> of a path for the session, for a file that the client reads at the start and
    ///     saves many times (the client settings). The client reads it again at the next start. The first call for a path
    ///     sets its description, and the file stays in memory for the session.
    /// </summary>
    public static GuardedFile ForSession(string path, string description)
        => SessionFiles.GetOrAdd(path, p => new GuardedFile(p, description, readAgainAtLogin: false));

    /// <summary>The full path of the file.</summary>
    public string Path { get; }

    /// <summary>The file name, for the log and the player.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>What the file holds, in the words of the player ("macros", "key bindings").</summary>
    public string Description { get; }

    /// <summary>True when the client reads the file again at the next login; false when it reads it again at the next start.</summary>
    public bool ReadAgainAtLogin { get; }

    /// <summary>
    ///     True while the last read of the file failed. A save is refused while it is true. A file can be read on one
    ///     thread and saved on another (the trust store saves on the handshake thread), so the flag is volatile.
    /// </summary>
    public bool IsUnreadable
    {
        get => UnreadableFlag;
        private set => UnreadableFlag = value;
    }

    private volatile bool UnreadableFlag;

    //one save at a time for this file: the saves share its temporary file
    private readonly Lock SaveGate = new();

    /// <summary>
    ///     Raised when a save does not go through, with why: <see cref="SaveResult.Unreadable" /> (the last read failed,
    ///     so the save was refused) or <see cref="SaveResult.Failed" /> (the write failed: another program holds the file,
    ///     or it is denied). One time for each file path in a session. It can be raised on any thread that saves. An event
    ///     with no listener is not counted, so subscribe before the first save (<c>Program.cs</c> starts
    ///     <c>SaveNotices</c>).
    /// </summary>
    public static event Action<GuardedFile, SaveResult>? NotSaved;

    /// <summary>
    ///     Reads the file through <see cref="DataDiagnostics.Try" />. A missing file gives <paramref name="empty" />. A file
    ///     that cannot be read gives <paramref name="empty" /> and is marked unreadable.
    /// </summary>
    public T Read<T>(Func<string, T> read, T empty)
    {
        var value = empty;
        var ok = DataDiagnostics.Try(
            () =>
            {
                if (File.Exists(Path))
                    value = read(Path);
            }, $"read {FileName}");

        IsUnreadable = !ok;

        return ok ? value : empty;
    }

    /// <summary>
    ///     The caller read the file but could not parse it, and uses its defaults. The file is copied to
    ///     <c>&lt;name&gt;.bad</c> (one copy, the newest), so the player's contents are kept, and the next save can write
    ///     the defaults. When the copy fails, the file is marked unreadable, because the file is then the one copy.
    /// </summary>
    public void MarkCorrupt(string reason)
    {
        var badPath = Path + ".bad";

        if (DataDiagnostics.Try(() => File.Copy(Path, badPath, true), $"copy {FileName} to {FileName}.bad"))
            DataDiagnostics.Log?.Invoke($"[data] {FileName} could not be parsed ({reason}); the defaults load, and {FileName}.bad holds the file");
        else
        {
            IsUnreadable = true;
            DataDiagnostics.Log?.Invoke($"[data] {FileName} could not be parsed ({reason}), and no copy could be made; its save is refused");
        }
    }

    /// <summary>The suffix of the file that a save writes before it replaces the file.</summary>
    public const string TEMP_SUFFIX = ".tmp";

    /// <summary>
    ///     Writes the file through <see cref="DataDiagnostics.Try" />, unless the last read of it failed. A save that is
    ///     refused or whose write fails logs and raises <see cref="NotSaved" />. <paramref name="write" /> writes to the
    ///     path that it is given, a file beside the target that then replaces the target, so a write that fails part way
    ///     keeps the old file.
    /// </summary>
    public SaveResult Save(Action<string> write)
    {
        if (IsUnreadable)
        {
            DataDiagnostics.Log?.Invoke($"[data] did not save {FileName}: the last read of it failed");
            RaiseNotSaved(SaveResult.Unreadable);

            return SaveResult.Unreadable;
        }

        //the write goes to a file beside the target, which then replaces the target, so a write that fails part way
        //leaves the old file as it was. Two threads can save one file (the trust store saves on the game loop and on the
        //connect task), and they share the temporary file, so one save at a time
        var temp = Path + TEMP_SUFFIX;

        using (SaveGate.EnterScope())
        {
            if (DataDiagnostics.Try(
                    () =>
                    {
                        write(temp);
                        MoveOver(temp, Path);
                    }, $"save {FileName}"))
                return SaveResult.Saved;

            DataDiagnostics.Try(() => File.Delete(temp), $"delete {FileName}{TEMP_SUFFIX}");
        }

        RaiseNotSaved(SaveResult.Failed);

        return SaveResult.Failed;
    }

    /// <summary>The tries of the move over the file, which a virus scanner or a search indexer can hold for a moment.</summary>
    internal const int MOVE_TRIES = 3;

    private const int MOVE_RETRY_MS = 50;

    //a program that opens the new file just after the write (a virus scanner, the search indexer) makes the move fail
    //for a moment; the last failure goes on to the caller
    private static void MoveOver(string temp, string target)
    {
        for (var tries = 1; ; tries++)
            try
            {
                File.Move(temp, target, overwrite: true);

                return;
            } catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && (tries < MOVE_TRIES))
            {
                Thread.Sleep(MOVE_RETRY_MS);
            }
    }

    private void RaiseNotSaved(SaveResult result)
    {
        if (NotSaved is null)
            return;

        using (NoticeGate.EnterScope())
            if (!Noticed.Add(Path))
                return;

        NotSaved?.Invoke(this, result);
    }
}
