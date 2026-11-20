namespace Brigid.Data;

/// <summary>
///     The metafile sync (0x6F). The server sends a checksum list; the client deletes the cached files that the list does
///     not name, and requests each file whose checksum differs. It writes a file only when it requested the file and the
///     CRC-32 of the data matches the checksum in the data packet, as retail does (BRIG-114). When no answer comes for
///     <see cref="REQUEST_TIMEOUT_SECONDS" />, the oldest file that waits is requested again, and the time limit counts
///     for each file that waits, under the same limit as a bad payload (BRIG-165). An answer to any file clears the time
///     limits, because the server answers in order. The sync completes when no request is open. Call its methods on one
///     thread (the game loop).
/// </summary>
public sealed class MetaFileSync
{
    /// <summary>
    ///     The number of requests for one file before the client keeps its cached copy, so that a server that always sends
    ///     bad data, or a disk that cannot be written, cannot hold the sync open.
    /// </summary>
    public const int MAX_REQUESTS = 3;

    /// <summary>
    ///     The time with no answer to any request before the oldest file that waits is requested again. A server that does
    ///     not know a name sends nothing (Hybrasyl); without a time limit, one such file holds the sync open for the session.
    ///     The server answers in order, so only the oldest file is requested again, and each answer restarts the time and
    ///     clears the time limits that the files that wait counted: a slow first download of the whole catalog is not cut
    ///     off, also when answers are more than this time apart, and it sends one request again for each time limit.
    /// </summary>
    public const int REQUEST_TIMEOUT_SECONDS = 10;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS);

    private readonly Func<string> DirectoryOf;
    private readonly Action<string> Request;
    private readonly Action Complete;
    private readonly Action<IReadOnlyCollection<string>> GiveListedNames;
    private readonly Func<string, byte[], bool> Write;
    private readonly TimeProvider Clock;

    //the files that the client requested and has not completed, in the order of the checksum list: the server answers in
    //this order, so the first one is the oldest. A file is written under the listed name, the name that the repository
    //opens
    private readonly OrderedDictionary<string, Entry> Pending = new(StringComparer.OrdinalIgnoreCase);

    //the time limits since the last answer to any file, and when the last answer, request list or time limit came. Every
    //file that waits shares them: an answer restarts the time of each file, and a time limit counts for each file
    private int Silences;
    private long WaitStart;

    /// <param name="Name">The name as the checksum list gives it.</param>
    /// <param name="Failures">The requests again after a bad payload, a failed write or an answer to a replaced list.</param>
    /// <param name="Sent">The requests sent for the file, for the log.</param>
    /// <param name="Listed">The checksum in the list.</param>
    /// <param name="Replaced">The checksum in a list that this list replaced while the file was pending, when it differs.</param>
    private readonly record struct Entry(string Name, int Failures, int Sent, uint Listed, uint? Replaced);

    /// <param name="directory">The metafile folder. It is read each time, because it depends on the server scope.</param>
    /// <param name="request">Sends the request for one file by name.</param>
    /// <param name="complete">Called when the sync completes.</param>
    /// <param name="listed">
    ///     Takes the safe names of each checksum list, before the prune, so that the repository reads only those files
    ///     (BRIG-167).
    /// </param>
    /// <param name="write">Writes one file and returns false on a failure; null writes with <see cref="MetaFileCache.TryWrite" />.</param>
    /// <param name="clock">The clock of the time limit; null is the system clock.</param>
    public MetaFileSync(
        Func<string> directory,
        Action<string> request,
        Action complete,
        Action<IReadOnlyCollection<string>> listed,
        Func<string, byte[], bool>? write = null,
        TimeProvider? clock = null)
    {
        DirectoryOf = directory;
        Request = request;
        Complete = complete;
        GiveListedNames = listed;
        Write = write ?? ((name, data) => MetaFileCache.TryWrite(directory(), name, data));
        Clock = clock ?? TimeProvider.System;
    }

    /// <summary>The number of files that the client requested and has not completed.</summary>
    internal int PendingCount => Pending.Count;

    //the tries that count toward MAX_REQUESTS: the first request, the failures and the time limits since the last answer
    private int TriesOf(Entry entry) => 1 + entry.Failures + Silences;

    /// <summary>
    ///     Starts a sync from the server's checksum list, in place of a sync that is open. A null list ends a sync that is
    ///     open and completes at once, with no change to the cache.
    /// </summary>
    public void OnChecksums(IEnumerable<(string Name, uint Checksum)>? entries)
    {
        //an answer to a list that this list replaces can still arrive, and it carries no mark of its list
        var replaced = Pending.Values.ToDictionary(entry => entry.Name, entry => entry.Listed, StringComparer.OrdinalIgnoreCase);
        Pending.Clear();
        RestartWait();

        if (entries is null)
        {
            Complete();

            return;
        }

        var directory = DirectoryOf();
        var listed = new List<(string Name, uint Checksum)>();

        foreach (var entry in entries)
        {
            if (FileNames.IsSafeName(entry.Name))
                listed.Add(entry);
            else
                Log($"Ignored a metafile checksum with an unsafe name: \"{entry.Name}\".");
        }

        string[] names = [.. listed.Select(entry => entry.Name)];

        //the repository reads only the listed files, so a stale file that the prune cannot delete is not read
        GiveListedNames(names);

        //the list is the server's whole catalog, so a local file that it does not name is stale. An empty list empties
        //the cache. Retail clears only its store in memory, but a file left on disk stays on disk.
        foreach (var name in MetaFileCache.PruneUnlisted(directory, names))
            Log($"Deleted a metafile that the server no longer lists: {name}.");

        foreach (var (name, checksum) in listed)
            if (MetaFileCache.GetChecksum(directory, name) != checksum)
                Pending[name] = new Entry(name, 0, 1, checksum, replaced.TryGetValue(name, out var old) && (old != checksum) ? old : null);

        foreach (var name in Pending.Keys)
            Request(name);

        if (Pending.Count == 0)
            Complete();
    }

    /// <summary>
    ///     Takes the data of one file. A file that the client did not request is dropped. A requested file is written when
    ///     the CRC-32 of its data is <paramref name="checksum" />. A file that does not match, or that cannot be written, is
    ///     requested again, up to <see cref="MAX_REQUESTS" /> requests; then the cache keeps its old copy.
    /// </summary>
    public void OnData(string? name, uint checksum, byte[] data)
    {
        if (string.IsNullOrEmpty(name))
            return;

        //never pending: the checksum list drops an unsafe name before the client requests anything
        if (!FileNames.IsSafeName(name))
        {
            Log($"Ignored a metafile with an unsafe name: \"{name}\".");

            return;
        }

        if (!Pending.TryGetValue(name, out var pending))
        {
            Log($"Ignored a metafile that the client did not request: {name}.");

            return;
        }

        //an answer shows that the server works through the requests, so the time limits that the files counted do not
        //count, and a server that answers more slowly than the time limit completes
        RestartWait();

        var valid = MetaFileCache.IsValidPayload(data, checksum);

        //a late answer to the replaced list matches the replaced checksum: drop it once and request the file again. After
        //that the checksum in the data packet decides, so a server whose list and data disagree for good still completes
        if (valid && (checksum == pending.Replaced) && (checksum != pending.Listed))
        {
            RequestAgain(pending with { Failures = pending.Failures + 1, Replaced = null }, "answers a checksum list that a later list replaced");

            return;
        }

        if (valid && Write(pending.Name, data))
            Pending.Remove(name);

        //a failed write is requested again too: the round trip gives a short lock (a virus scanner, for example) time to
        //clear, and it uses the same limit, so a disk that cannot be written does not hold the sync open
        else if (TriesOf(pending) < MAX_REQUESTS)
            RequestAgain(pending with { Failures = pending.Failures + 1 }, valid ? "could not be written" : "does not match its checksum", TriesOf(pending) + 1);
        else
            Stop(pending, valid ? "could not be written" : "does not match its checksum");

        if (Pending.Count == 0)
            Complete();
    }

    /// <summary>
    ///     When no answer came for <see cref="REQUEST_TIMEOUT_SECONDS" />, requests the oldest file that waits again, and
    ///     counts the time limit for each file that waits. Past <see cref="MAX_REQUESTS" /> tries (the first request, the
    ///     failures, and the time limits since the last answer to any file), the cache keeps its old copy and the entry
    ///     completes, so a file that the server never sends holds the sync for 30 s after the last answer at most. Call it
    ///     once each frame while the connection is in World.
    /// </summary>
    public void Tick()
    {
        if ((Pending.Count == 0) || (Clock.GetElapsedTime(WaitStart) < RequestTimeout))
            return;

        Silences++;
        WaitStart = Clock.GetTimestamp();

        //the server answers in order, so the oldest file is the one it is on; a request again for each file that waits
        //gave a slow server one request for each file at each time limit
        var oldest = Pending.GetAt(0).Key;

        foreach (var entry in Pending.Values.ToArray())
            if (TriesOf(entry) > MAX_REQUESTS)
                Stop(entry, "has no answer");
            else if (entry.Name == oldest)
                RequestAgain(entry, "has no answer", TriesOf(entry));

        if (Pending.Count == 0)
            Complete();
    }

    //sends the request again and counts it, for the log
    private void RequestAgain(Entry next, string problem, int? tries = null)
    {
        Log(tries is { } count
            ? $"{next.Name} {problem}. Requesting it again (try {count} of {MAX_REQUESTS}, request {next.Sent + 1})."
            : $"{next.Name} {problem}. Requesting it again.");
        Pending[next.Name] = next with { Sent = next.Sent + 1 };
        Request(next.Name);
    }

    //the cache keeps its old copy, and the entry leaves the pending set
    private void Stop(Entry entry, string problem)
    {
        Log($"{entry.Name} {problem} after {MAX_REQUESTS} tries ({entry.Sent} requests). The cache keeps its old copy, if it has one.");
        Pending.Remove(entry.Name);
    }

    private void RestartWait()
    {
        Silences = 0;
        WaitStart = Clock.GetTimestamp();
    }

    /// <summary>
    ///     Ends a sync that is open, with no completion: call it when the connection leaves World (a loss, a logout or a
    ///     world transfer) and when the server changes. Nothing waits for the sync then, and the next world entry asks for
    ///     a new checksum list. A late answer is dropped.
    /// </summary>
    public void End()
    {
        if (Pending.Count == 0)
            return;

        Log($"The sync ended with {Pending.Count} file(s) open.");
        Pending.Clear();
    }

    private static void Log(string message) => DataDiagnostics.Log?.Invoke($"[meta] {message}");
}
