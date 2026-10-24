using System.IO.Compression;
using System.Text;
using Brigid.Data;
using DALib.Cryptography;
using Xunit;

namespace Brigid.Tests;

// BRIG-114: the metafile sync wrote a file that the client never requested, with no check, and removed a file from the
// pending set after a failed write, so the sync completed with a cache that did not hold the file. A file that the client
// did not request is now dropped, and a failed write is requested again, up to the same limit as a bad checksum.
[Collection("DataDiagnostics")]
public sealed class MetaFileSyncTests : IDisposable
{
    private static readonly byte[] Content = Encoding.ASCII.GetBytes("ItemInfo0 content");
    private static readonly uint ContentChecksum = CRC32.Calculate(Content);

    private readonly string Root = Path.Combine(Path.GetTempPath(), "brigid-metasync-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> Requests = [];
    private readonly List<string> Logs = [];
    private readonly Action<string>? OriginalLog = DataDiagnostics.Log;
    private int Completions;

    public MetaFileSyncTests()
    {
        Directory.CreateDirectory(Root);
        DataDiagnostics.Log = Logs.Add;
    }

    public void Dispose()
    {
        DataDiagnostics.Log = OriginalLog;
        Directory.Delete(Root, true);
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();

        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
            zlib.Write(data);

        return output.ToArray();
    }

    private MetaFileSync NewSync(Func<string, byte[], bool>? write = null) => new(() => Root, Requests.Add, () => Completions++, write);

    private static (string, uint)[] Listing(params string[] names) => [.. names.Select(name => (name, ContentChecksum))];

    private bool OnDisk(string name) => File.Exists(Path.Combine(Root, name));

    private static void Answer(MetaFileSync sync, string name) => sync.OnData(name, ContentChecksum, Compress(Content));

    [Fact]
    public void OnChecksums_RequestsADifferingFile_AndOnDataWritesItAndCompletes()
    {
        var sync = NewSync();

        sync.OnChecksums(Listing("ItemInfo0"));

        Assert.Equal(["ItemInfo0"], Requests);
        Assert.Equal(0, Completions);

        Answer(sync, "ItemInfo0");

        Assert.True(OnDisk("ItemInfo0"));
        Assert.Equal(1, Completions);
        Assert.Equal(0, sync.PendingCount);
    }

    [Fact]
    public void OnData_DropsAFileThatWasNotRequested()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));

        Answer(sync, "SClass1");

        Assert.False(OnDisk("SClass1"));
        Assert.Equal(0, Completions);
        Assert.Equal(1, sync.PendingCount);
        Assert.Contains(Logs, line => line.Contains("did not request") && line.Contains("SClass1"));
    }

    [Fact]
    public void OnData_DropsASecondAnswerAfterTheFileCompleted()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));
        Answer(sync, "ItemInfo0");
        File.Delete(Path.Combine(Root, "ItemInfo0"));

        Answer(sync, "ItemInfo0");

        Assert.False(OnDisk("ItemInfo0"));
        Assert.Equal(1, Completions);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("..")]
    [InlineData("a/b")]
    public void OnData_IgnoresAnEmptyOrUnsafeName(string? name)
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));

        sync.OnData(name, ContentChecksum, Compress(Content));

        Assert.Empty(Directory.GetFiles(Root, "*", SearchOption.AllDirectories));
        Assert.Equal(0, Completions);
        Assert.Equal(1, sync.PendingCount);
    }

    //retail checks the data against the checksum in the data packet, so a file that the server changed after the list
    //is still accepted
    [Fact]
    public void OnData_ChecksTheDataAgainstTheChecksumInTheDataPacket()
    {
        var sync = NewSync();
        sync.OnChecksums([("ItemInfo0", ContentChecksum ^ 1)]);

        Answer(sync, "ItemInfo0");

        Assert.True(OnDisk("ItemInfo0"));
        Assert.Equal(1, Completions);
    }

    [Fact]
    public void OnData_MatchesARequestedNameWhateverItsCase()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));

        Answer(sync, "iteminfo0");

        Assert.Equal(0, sync.PendingCount);
        Assert.Equal(1, Completions);
    }

    [Fact]
    public void OnData_RequestsABadPayloadAgainUpToTheLimit()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));

        sync.OnData("ItemInfo0", ContentChecksum, []);
        sync.OnData("ItemInfo0", ContentChecksum, Compress(Encoding.ASCII.GetBytes("other content")));

        Assert.Equal(MetaFileSync.MAX_REQUESTS, Requests.Count);
        Assert.Equal(0, Completions);

        sync.OnData("ItemInfo0", ContentChecksum, []);

        Assert.Equal(MetaFileSync.MAX_REQUESTS, Requests.Count);
        Assert.False(OnDisk("ItemInfo0"));
        Assert.Equal(1, Completions);
        Assert.Contains(Logs, line => line.Contains("does not match its checksum after"));
    }

    [Fact]
    public void OnData_RequestsAFailedWriteAgain_AndCompletesAtTheLimit()
    {
        var writes = 0;
        var sync = NewSync((_, _) =>
        {
            writes++;

            return false;
        });
        sync.OnChecksums(Listing("ItemInfo0"));

        for (var i = 0; i < MetaFileSync.MAX_REQUESTS; i++)
        {
            Assert.Equal(0, Completions);
            Answer(sync, "ItemInfo0");
        }

        Assert.Equal(MetaFileSync.MAX_REQUESTS, writes);
        Assert.Equal(MetaFileSync.MAX_REQUESTS, Requests.Count);
        Assert.Equal(1, Completions);
        Assert.Contains(Logs, line => line.Contains("could not be written after"));
    }

    [Fact]
    public void OnData_CompletesWhenTheWriteSucceedsOnTheNextRequest()
    {
        var writes = 0;
        var sync = NewSync((_, _) => ++writes > 1);
        sync.OnChecksums(Listing("ItemInfo0"));

        Answer(sync, "ItemInfo0");

        Assert.Equal(2, Requests.Count);
        Assert.Equal(0, Completions);

        Answer(sync, "ItemInfo0");

        Assert.Equal(1, Completions);
        Assert.Equal(0, sync.PendingCount);
    }

    [Fact]
    public void OnChecksums_CompletesOnlyWhenEveryFileIsDone()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0", "SClass1"));

        Answer(sync, "ItemInfo0");

        Assert.Equal(0, Completions);

        Answer(sync, "SClass1");

        Assert.Equal(1, Completions);
    }

    [Fact]
    public void OnChecksums_CompletesAtOnceWithNothingToFetch()
    {
        File.WriteAllBytes(Path.Combine(Root, "ItemInfo0"), Compress(Content));
        var sync = NewSync();

        sync.OnChecksums(Listing("ItemInfo0"));

        Assert.Empty(Requests);
        Assert.Equal(1, Completions);
    }

    [Fact]
    public void OnChecksums_CompletesAtOnceForANullList()
    {
        var sync = NewSync();

        sync.OnChecksums(null);

        Assert.Empty(Requests);
        Assert.Equal(1, Completions);
    }

    [Fact]
    public void OnChecksums_SkipsAnUnsafeName_AndDeletesAFileThatIsNotListed()
    {
        File.WriteAllBytes(Path.Combine(Root, "Stale"), [1]);
        var sync = NewSync();

        sync.OnChecksums([("..", ContentChecksum), ("ItemInfo0", ContentChecksum)]);

        Assert.Equal(["ItemInfo0"], Requests);
        Assert.False(OnDisk("Stale"));
        Assert.Contains(Logs, line => line.Contains("unsafe name"));
    }

    [Fact]
    public void OnChecksums_ReplacesASyncThatIsOpen()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));

        sync.OnChecksums(Listing("SClass1"));
        Answer(sync, "ItemInfo0");

        Assert.Equal(0, Completions);

        Answer(sync, "SClass1");

        Assert.Equal(1, Completions);
        Assert.Equal(0, sync.PendingCount);
    }
}
