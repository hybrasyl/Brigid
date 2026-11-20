using System.Text;
using Brigid.Data;
using Brigid.Data.Repositories;
using Brigid.Networking.Definitions;
using Brigid.Systems;
using DALib.Cryptography;
using DALib.Data;
using Xunit;
using static Brigid.Tests.MetaFileTestData;

namespace Brigid.Tests;

// The game ran the metafile sync from ChaosGame: the World rule, the time limit, the names for the repository and the
// server change. No test reached that code, and three mutations of it left the suite green. It is in MetaFileSession now.
[Collection("DataDiagnostics")]
public sealed class MetaFileSessionTests : IDisposable
{
    private static readonly byte[] Content = Encoding.ASCII.GetBytes("Light content");
    private static readonly uint ContentChecksum = CRC32.Calculate(Content);

    private readonly string Root = Path.Combine(Path.GetTempPath(), "brigid-metasession-" + Guid.NewGuid().ToString("N"));
    private readonly MetaFileRepository Repository;
    private readonly List<string> Requests = [];
    private readonly ManualClock Clock = new();
    private int Completions;

    public MetaFileSessionTests()
    {
        Directory.CreateDirectory(Root);
        Repository = new MetaFileRepository(Root);

        WriteMetaFile(Root, "ItemInfo0", new MetaFileEntry("Stick", ["1", "0", "1", "Weapon", "A stick."]));
        WriteMetaFile(Root, "SEvent1", new MetaFileEntry("Listed event"));
        WriteMetaFile(Root, "SEvent2", new MetaFileEntry("Other event"));
    }

    public void Dispose() => Directory.Delete(Root, true);

    private MetaFileSession NewSession(Action? complete = null)
        => new(() => Root, Requests.Add, complete ?? (() => Completions++), () => Repository, Clock);

    //the checksum of a file on disk, so the sync does not request it
    private (string, uint) Current(string name) => (name, MetaFileCache.GetChecksum(Root, name)!.Value);

    private void AnswerLight(MetaFileSession session, ConnectionState state) => session.OnData(state, "Light", ContentChecksum, Compress(Content));

    private bool OnDisk(string name) => File.Exists(Path.Combine(Root, name));

    [Fact]
    public void AListInWorldGivesTheRepositoryItsNames()
    {
        var session = NewSession();

        session.OnChecksums(ConnectionState.World, [Current("SEvent1")]);

        Assert.Equal(["SEvent1"], Repository.ListedNamesForTests);
        Assert.Equal(1, Completions);
    }

    [Fact]
    public void AListOutOfWorldIsDropped()
    {
        var session = NewSession();

        session.OnChecksums(ConnectionState.Lobby, [("Light", ContentChecksum)]);

        Assert.Empty(Requests);
        Assert.Equal(0, Completions);
        Assert.True(OnDisk("SEvent2"));
        Assert.Null(Repository.ListedNamesForTests);
    }

    [Fact]
    public void DataOutOfWorldIsDropped()
    {
        var session = NewSession();
        session.OnChecksums(ConnectionState.World, [("Light", ContentChecksum)]);

        AnswerLight(session, ConnectionState.Lobby);

        Assert.False(OnDisk("Light"));
        Assert.Equal(0, Completions);

        AnswerLight(session, ConnectionState.World);

        Assert.True(OnDisk("Light"));
        Assert.Equal(1, Completions);
    }

    [Fact]
    public void AnUpdateInWorldRequestsAFileWithNoAnswerAgain()
    {
        var session = NewSession();
        session.OnChecksums(ConnectionState.World, [("Light", ContentChecksum)]);

        Clock.Ms += TIMEOUT_MS;
        session.Update(ConnectionState.World);

        Assert.Equal(["Light", "Light"], Requests);
    }

    //a loss, a logout and a world transfer each leave World, and a late answer is then dropped
    [Fact]
    public void AnUpdateOutOfWorldEndsTheSync()
    {
        var session = NewSession();
        session.OnChecksums(ConnectionState.World, [("Light", ContentChecksum)]);

        session.Update(ConnectionState.Disconnected);
        AnswerLight(session, ConnectionState.World);

        Assert.False(OnDisk("Light"));
        Assert.Equal(0, Completions);
    }

    //a completion from a time limit runs the screen handlers outside the packet handler's catch
    [Fact]
    public void AnUpdateLogsAThrowFromTheCompletion()
    {
        var calls = 0;
        var session = NewSession(() =>
        {
            calls++;

            throw new InvalidOperationException("a completion handler failed");
        });
        session.OnChecksums(ConnectionState.World, [("Light", ContentChecksum)]);

        for (var i = 0; i < MetaFileSync.MAX_REQUESTS; i++)
        {
            Clock.Ms += TIMEOUT_MS;
            session.Update(ConnectionState.World);
        }

        Assert.Equal(1, calls);
    }

    [Fact]
    public void AServerChangeForgetsTheListedNames()
    {
        var session = NewSession();
        session.OnChecksums(ConnectionState.World, [Current("SEvent1")]);

        session.ServerChanged();

        Assert.Null(Repository.ListedNamesForTests);
    }

    [Fact]
    public void AServerChangeClearsTheItemIndex()
    {
        var session = NewSession();
        Repository.BuildItemIndex();
        Assert.Single(Repository.GetItemMetadata("Stick"));

        session.ServerChanged();

        Assert.Empty(Repository.GetItemMetadata("Stick"));
    }

    [Fact]
    public void AServerChangeEndsTheSync()
    {
        var session = NewSession();
        session.OnChecksums(ConnectionState.World, [("Light", ContentChecksum)]);

        session.ServerChanged();
        AnswerLight(session, ConnectionState.World);

        Assert.False(OnDisk("Light"));
        Assert.Equal(0, Completions);
    }
}
