namespace Brigid.Data;

/// <summary>
///     The metafile sync (0x6F). The server sends a checksum list; the client deletes the cached files that the list does
///     not name, and requests each file whose checksum differs. It writes a file only when it requested the file and the
///     CRC-32 of the data matches the checksum in the data packet, as retail does (BRIG-114). The sync completes when no
///     request is open.
/// </summary>
public sealed class MetaFileSync
{
    /// <summary>
    ///     The number of requests for one file before the client keeps its cached copy, so that a server that always sends
    ///     bad data, or a disk that cannot be written, cannot hold the sync open.
    /// </summary>
    public const int MAX_REQUESTS = 3;

    private readonly Func<string> DirectoryOf;
    private readonly Action<string> Request;
    private readonly Action Complete;
    private readonly Func<string, byte[], bool> Write;

    //the files that the client requested and has not completed, with the number of requests sent for each
    private readonly Dictionary<string, int> Pending = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="directory">The metafile folder. It is read each time, because it depends on the server scope.</param>
    /// <param name="request">Sends the request for one file by name.</param>
    /// <param name="complete">Called when the sync completes.</param>
    public MetaFileSync(Func<string> directory, Action<string> request, Action complete)
        : this(directory, request, complete, null) { }

    /// <param name="directory">The metafile folder.</param>
    /// <param name="request">Sends the request for one file by name.</param>
    /// <param name="complete">Called when the sync completes.</param>
    /// <param name="write">Writes one file and returns false on a failure; null writes with <see cref="MetaFileCache.TryWrite" />.</param>
    internal MetaFileSync(Func<string> directory, Action<string> request, Action complete, Func<string, byte[], bool>? write)
    {
        DirectoryOf = directory;
        Request = request;
        Complete = complete;
        Write = write ?? ((name, data) => MetaFileCache.TryWrite(directory(), name, data));
    }

    /// <summary>The number of files that the client requested and has not completed.</summary>
    internal int PendingCount => Pending.Count;

    /// <summary>
    ///     Starts a sync from the server's checksum list, in place of a sync that is open. A null list completes at once,
    ///     with no change to the cache.
    /// </summary>
    public void OnChecksums(IEnumerable<(string Name, uint Checksum)>? entries)
    {
        if (entries is null)
        {
            Complete();

            return;
        }

        Pending.Clear();

        var directory = DirectoryOf();
        var listed = new List<(string Name, uint Checksum)>();

        foreach (var entry in entries)
        {
            if (MetaFileCache.IsSafeName(entry.Name))
                listed.Add(entry);
            else
                Log($"Ignored a metafile checksum with an unsafe name: \"{entry.Name}\".");
        }

        //the list is the server's whole catalog, so a local file that it does not name is stale. An empty list empties
        //the cache. Retail clears only its store in memory, but MetaFileRepository reads every file in the folder, so a
        //file left on disk stays in use.
        foreach (var name in MetaFileCache.PruneUnlisted(directory, listed.Select(entry => entry.Name)))
            Log($"Deleted a metafile that the server no longer lists: {name}.");

        foreach (var (name, checksum) in listed)
            if (MetaFileCache.GetChecksum(directory, name) != checksum)
                Pending[name] = 1;

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
        if (!MetaFileCache.IsSafeName(name))
        {
            Log($"Ignored a metafile with an unsafe name: \"{name}\".");

            return;
        }

        if (!Pending.TryGetValue(name, out var requests))
        {
            Log($"Ignored a metafile that the client did not request: {name}.");

            return;
        }

        var valid = MetaFileCache.IsValidPayload(data, checksum);

        if (!valid || !Write(name, data))
        {
            var problem = valid ? "could not be written" : "does not match its checksum";

            //a failed write is requested again too: the round trip gives a short lock (a virus scanner, for example) time
            //to clear, and it uses the same limit, so a disk that cannot be written does not hold the sync open
            if (requests < MAX_REQUESTS)
            {
                Log($"{name} {problem}. Requesting it again ({requests + 1} of {MAX_REQUESTS}).");
                Pending[name] = requests + 1;
                Request(name);

                return;
            }

            Log($"{name} {problem} after {requests} requests. The cache keeps its old copy, if it has one.");
        }

        Pending.Remove(name);

        if (Pending.Count == 0)
            Complete();
    }

    private static void Log(string message) => DataDiagnostics.Log?.Invoke($"[meta] {message}");
}
