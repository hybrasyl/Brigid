#region
using Brigid.Data;
using Brigid.Data.Repositories;
using Brigid.Networking;
using Brigid.Networking.Definitions;
#endregion

namespace Brigid.Systems;

/// <summary>
///     The metafile sync of the game. It runs <see cref="MetaFileSync" /> only while the connection is in World, gives the
///     names of each checksum list to the repository, and forgets the metafile data of the old server at a server change.
///     <see cref="ChaosGame" /> calls it from the game loop.
/// </summary>
public sealed class MetaFileSession
{
    private readonly MetaFileSync Sync;
    private readonly Func<MetaFileRepository> Repository;

    /// <param name="directory">The metafile folder. It is read each time, because it depends on the server scope.</param>
    /// <param name="request">Sends the request for one file by name.</param>
    /// <param name="complete">Called when the sync completes.</param>
    /// <param name="repository">The metafile repository. It is read each time, because it is made at the asset load.</param>
    /// <param name="clock">The clock of the time limit; null is the system clock.</param>
    public MetaFileSession(
        Func<string> directory,
        Action<string> request,
        Action complete,
        Func<MetaFileRepository> repository,
        TimeProvider? clock = null)
    {
        Repository = repository;
        Sync = new MetaFileSync(directory, request, complete, names => Repository().SetListedNames(names), null, clock);
    }

    /// <summary>
    ///     Takes the server's checksum list. A list handled after the connection left World is dropped, and the next world
    ///     entry asks for a new list.
    /// </summary>
    public void OnChecksums(ConnectionState state, IEnumerable<(string Name, uint Checksum)>? entries)
    {
        if (state == ConnectionState.World)
            Sync.OnChecksums(entries);
    }

    /// <summary>Takes the data of one file. Data handled after the connection left World is dropped.</summary>
    public void OnData(ConnectionState state, string? name, uint checksum, byte[] data)
    {
        if (state == ConnectionState.World)
            Sync.OnData(name, checksum, data);
    }

    /// <summary>
    ///     Call it once each frame with the state that the game loop reads. In World it lets the sync check its time limit;
    ///     out of World (a loss, a logout or a world transfer) it ends the sync. A completion from a time limit runs the
    ///     same handlers as a completion from a packet, so it logs a failure as
    ///     <see cref="ConnectionManager.ProcessPackets" /> does, and the game loop continues.
    /// </summary>
    public void Update(ConnectionState state)
    {
        if (state != ConnectionState.World)
        {
            Sync.End();

            return;
        }

        try
        {
            Sync.Tick();
        } catch (Exception ex)
        {
            NoticeDebugLog.Write($"!!! metafile sync tick threw {ex.GetType().Name}: {ex.Message}");
            NoticeDebugLog.Write($"  stack: {ex.StackTrace}");
        }
    }

    /// <summary>
    ///     Ends the sync and forgets the metafile data of the old server: the item index, the NPC illustration data and
    ///     the names of its last checksum list, so the repository reads every file of the new server's folder (BRIG-105).
    /// </summary>
    public void ServerChanged()
    {
        Sync.End();

        var repository = Repository();
        repository.ClearCaches();
        repository.ForgetListedNames();
    }
}
