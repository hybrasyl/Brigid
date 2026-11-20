using System.IO.Compression;
using System.Text;
using Brigid.Data;
using DALib.Cryptography;
using Xunit;
using static Brigid.Tests.MetaFileTestData;

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
    private readonly List<IReadOnlyCollection<string>> ListedNames = [];
    private readonly ManualClock Clock = new();

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

    private MetaFileSync NewSync(Func<string, byte[], bool>? write = null) => new(() => Root, Requests.Add, () => Completions++, ListedNames.Add, write, Clock);

    //moves the clock and lets the sync check its requests
    private void Wait(MetaFileSync sync, long ms) => At(sync, Clock.Ms + ms);

    //sets the clock to a time from the start of the test and lets the sync check its requests
    private void At(MetaFileSync sync, long ms)
    {
        Clock.Ms = ms;
        sync.Tick();
    }

    private static (string, uint)[] Listing(params string[] names) => [.. names.Select(name => (name, ContentChecksum))];

    private bool OnDisk(string name) => File.Exists(Path.Combine(Root, name));

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);

        return output.ToArray();
    }

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

    //the prune keeps the listed name and the repository reads it first, so the file takes the name from the list, not
    //from the answer
    [Fact]
    public void OnData_MatchesARequestedNameWhateverItsCase_AndWritesItUnderTheListedName()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));

        Answer(sync, "iteminfo0");

        Assert.Equal(0, sync.PendingCount);
        Assert.Equal(1, Completions);
        Assert.Equal(["ItemInfo0"], Directory.GetFiles(Root).Select(Path.GetFileName));
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

    //a null list ends the open sync, so a late answer for its file does not complete a second time
    [Fact]
    public void OnChecksums_ANullListEndsASyncThatIsOpen()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));

        sync.OnChecksums(null);
        Answer(sync, "ItemInfo0");

        Assert.Equal(1, Completions);
        Assert.Equal(0, sync.PendingCount);
        Assert.False(OnDisk("ItemInfo0"));
    }

    //BRIG-166: a file that is not in the cache had a checksum of 0, so a listed checksum of 0 counted as current
    [Fact]
    public void OnChecksums_RequestsAMissingFileWhoseListedChecksumIsZero()
    {
        var sync = NewSync();

        sync.OnChecksums([("Light", 0u)]);

        Assert.Equal(["Light"], Requests);
        Assert.Equal(0, Completions);
    }

    //BRIG-167: the repository reads only the files that the last list names, so a stale file that could not be deleted
    //is not read
    [Fact]
    public void OnChecksums_GivesTheRepositoryTheSafeListedNames()
    {
        var sync = NewSync();

        sync.OnChecksums([("ItemInfo0", ContentChecksum), ("../ItemInfo1", ContentChecksum), ("Light", ContentChecksum)]);

        Assert.Equal([["ItemInfo0", "Light"]], ListedNames.Select(names => names.Order().ToArray()));
    }

    //a null list changes nothing in the cache, so the repository keeps the names it has
    [Fact]
    public void OnChecksums_ANullListGivesNoNames()
    {
        var sync = NewSync();

        sync.OnChecksums(null);

        Assert.Empty(ListedNames);
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

    //an answer carries no mark of its list. A late answer to a replaced list matches the replaced checksum, so it is
    //dropped once and the file is requested again; the answer to the current list is then written
    [Fact]
    public void OnData_DropsALateAnswerToAReplacedListOnce()
    {
        var older = Encoding.ASCII.GetBytes("ItemInfo0 before the server changed it");
        var olderChecksum = CRC32.Calculate(older);
        var sync = NewSync();
        sync.OnChecksums([("ItemInfo0", olderChecksum)]);
        sync.OnChecksums(Listing("ItemInfo0"));
        Requests.Clear();

        sync.OnData("ItemInfo0", olderChecksum, Compress(older));

        Assert.False(OnDisk("ItemInfo0"));
        Assert.Equal(0, Completions);
        Assert.Equal(["ItemInfo0"], Requests);

        Answer(sync, "ItemInfo0");

        Assert.Equal(Content, Decompress(File.ReadAllBytes(Path.Combine(Root, "ItemInfo0"))));
        Assert.Equal(1, Completions);
    }

    //the drop is once only: a server whose list and data disagree for good still completes, with the data it sends
    [Fact]
    public void OnData_TakesTheSecondAnswerThatMatchesAReplacedList()
    {
        var older = Encoding.ASCII.GetBytes("ItemInfo0 before the server changed it");
        var olderChecksum = CRC32.Calculate(older);
        var sync = NewSync();
        sync.OnChecksums([("ItemInfo0", olderChecksum)]);
        sync.OnChecksums(Listing("ItemInfo0"));

        sync.OnData("ItemInfo0", olderChecksum, Compress(older));
        sync.OnData("ItemInfo0", olderChecksum, Compress(older));

        Assert.True(OnDisk("ItemInfo0"));
        Assert.Equal(1, Completions);
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

    //BRIG-165: a request that the server never answers (Hybrasyl sends nothing for a name it does not know) held the sync
    //open for the session, so the item index and the light metadata were never built again

    [Fact]
    public void Tick_DoesNothingBeforeTheTimeLimit()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));

        Wait(sync, TIMEOUT_MS - 1);

        Assert.Equal(["ItemInfo0"], Requests);
        Assert.Equal(1, sync.PendingCount);
    }

    [Fact]
    public void Tick_RequestsAFileWithNoAnswerAgain_AndCompletesAtTheLimit()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));

        for (var request = 2; request <= MetaFileSync.MAX_REQUESTS; request++)
        {
            Wait(sync, TIMEOUT_MS);

            Assert.Equal(request, Requests.Count);
            Assert.Equal(0, Completions);
        }

        Wait(sync, TIMEOUT_MS);

        Assert.Equal(MetaFileSync.MAX_REQUESTS, Requests.Count);
        Assert.Equal(1, Completions);
        Assert.Equal(0, sync.PendingCount);
        Assert.False(OnDisk("ItemInfo0"));
    }

    //each request has its own time: a request again after a bad payload starts a new time limit
    [Fact]
    public void Tick_TimesARequestFromWhenItWasSent()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));

        Wait(sync, TIMEOUT_MS - 1);
        sync.OnData("ItemInfo0", ContentChecksum, Compress([9, 9]));
        Wait(sync, TIMEOUT_MS - 1);

        Assert.Equal(2, Requests.Count);
    }

    //a request again after a time limit has a time limit of its own
    [Fact]
    public void Tick_TimesARequestAgainFromWhenItWasSent()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));

        Wait(sync, TIMEOUT_MS);
        Wait(sync, 1);

        Assert.Equal(2, Requests.Count);
    }

    //a file that stops at the limit while another file waits does not complete the sync. Both files are requested at 0;
    //a bad payload puts ItemInfo1 one request ahead, so it reaches the limit first
    [Fact]
    public void Tick_DoesNotCompleteWhileAnotherFileWaits()
    {
        const long t = TIMEOUT_MS;
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0", "ItemInfo1"));

        At(sync, t - 1);
        sync.OnData("ItemInfo1", ContentChecksum, Compress([9, 9])); //ItemInfo1 request 2; both times restart at t - 1
        At(sync, (2 * t) - 1);                                        //ItemInfo1 try 3 with no request; ItemInfo0 request 2

        Assert.Equal(4, Requests.Count);
        Assert.Equal("ItemInfo0", Requests[^1]);

        At(sync, (3 * t) - 1); //ItemInfo1 stops at the limit; ItemInfo0 request 3

        Assert.Equal(1, sync.PendingCount);
        Assert.Equal(0, Completions);

        At(sync, (4 * t) - 1); //ItemInfo0 stops at the limit

        Assert.Equal(1, Completions);
    }

    //a time limit that stops the oldest file requests the next file at once; it waited another time limit before
    [Fact]
    public void Tick_RequestsTheNextFileWhenTheOldestStops()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0", "ItemInfo1"));
        sync.OnData("ItemInfo0", ContentChecksum, Compress([9, 9]));
        sync.OnData("ItemInfo0", ContentChecksum, Compress([9, 9]));

        Wait(sync, TIMEOUT_MS);

        Assert.Equal(1, sync.PendingCount);
        Assert.Equal(["ItemInfo0", "ItemInfo1", "ItemInfo0", "ItemInfo0", "ItemInfo1"], Requests);
    }

    //the server answers in order: a slow download of the whole catalog keeps the files that wait, while answers come
    [Fact]
    public void Tick_AnAnswerRestartsTheTimeOfTheFilesThatWait()
    {
        const long t = TIMEOUT_MS;
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0", "ItemInfo1"));

        At(sync, t - 1);
        Answer(sync, "ItemInfo1");
        At(sync, t);

        Assert.Equal(2, Requests.Count);

        At(sync, (2 * t) - 1);

        Assert.Equal(3, Requests.Count);
        Assert.Equal("ItemInfo0", Requests[^1]);
    }

    //answers that come more than the time limit apart still complete every file: each answer clears the time limits that
    //the files that wait used
    [Fact]
    public void Tick_AnswersMoreThanTheTimeLimitApartCompleteEveryFile()
    {
        const long gap = TIMEOUT_MS + 2000;
        string[] names = ["ItemInfo0", "ItemInfo1", "ItemInfo2", "ItemInfo3", "ItemInfo4"];
        var sync = NewSync();
        sync.OnChecksums(Listing(names));

        for (var i = 0; i < names.Length; i++)
        {
            At(sync, ((i + 1) * gap) - 2000);
            At(sync, (i + 1) * gap);
            Answer(sync, names[i]);
        }

        Assert.Equal(1, Completions);
        Assert.All(names, name => Assert.True(OnDisk(name)));

        //each time limit requests only the oldest file again: one request for each gap, not one for each file that waits
        Assert.Equal(names.Length * 2, Requests.Count);
    }

    //two time limits in a gap still leave the third try: answers 25 s apart complete every file, with two requests again
    //for each gap
    [Fact]
    public void Tick_AnswersTwoTimeLimitsApartCompleteEveryFile()
    {
        const long gap = (2 * TIMEOUT_MS) + 5000;
        string[] names = ["ItemInfo0", "ItemInfo1", "ItemInfo2"];
        var sync = NewSync();
        sync.OnChecksums(Listing(names));

        for (var i = 0; i < names.Length; i++)
        {
            var start = i * gap;
            At(sync, start + TIMEOUT_MS);
            At(sync, start + (2 * TIMEOUT_MS));
            At(sync, start + gap);
            Answer(sync, names[i]);
        }

        Assert.Equal(1, Completions);
        Assert.All(names, name => Assert.True(OnDisk(name)));
        Assert.Equal(names.Length * 3, Requests.Count);
    }

    //an answer clears only the time limits: a bad payload still counts, so a server that always sends bad data stops
    [Fact]
    public void Tick_AnAnswerToAnotherFileDoesNotClearABadPayloadCount()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0", "ItemInfo1"));

        sync.OnData("ItemInfo0", ContentChecksum, Compress([9, 9]));
        sync.OnData("ItemInfo0", ContentChecksum, Compress([9, 9]));
        Answer(sync, "ItemInfo1");
        sync.OnData("ItemInfo0", ContentChecksum, Compress([9, 9]));

        Assert.Equal(MetaFileSync.MAX_REQUESTS, Requests.Count(name => name == "ItemInfo0"));
        Assert.Equal(1, Completions);
        Assert.False(OnDisk("ItemInfo0"));
    }

    //an answer clears the time limits before its own payload counts: a bad payload after two time limits is requested
    //again, and the file is not given up
    [Fact]
    public void OnData_ABadPayloadAfterTwoTimeLimitsIsRequestedAgain()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));
        Wait(sync, TIMEOUT_MS);
        Wait(sync, TIMEOUT_MS);

        sync.OnData("ItemInfo0", ContentChecksum, Compress([9, 9]));

        Assert.Equal(4, Requests.Count);
        Assert.Equal(1, sync.PendingCount);
        Assert.Equal(0, Completions);
    }

    //the cache keeps its old copy of a file that has no answer
    [Fact]
    public void Tick_KeepsTheOldCopyOfAFileWithNoAnswer()
    {
        var old = Compress(Encoding.ASCII.GetBytes("old ItemInfo0"));
        File.WriteAllBytes(Path.Combine(Root, "ItemInfo0"), old);
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));

        for (var i = 0; i < MetaFileSync.MAX_REQUESTS; i++)
            Wait(sync, TIMEOUT_MS);

        Assert.Equal(1, Completions);
        Assert.Equal(old, File.ReadAllBytes(Path.Combine(Root, "ItemInfo0")));
    }

    //a time limit and a bad payload use the one count, so the sync ends after MAX_REQUESTS requests in all
    [Fact]
    public void Tick_AndABadPayloadShareTheRequestCount()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));

        sync.OnData("ItemInfo0", ContentChecksum, Compress([9, 9]));
        Wait(sync, TIMEOUT_MS);

        Assert.Equal(MetaFileSync.MAX_REQUESTS, Requests.Count);

        Wait(sync, TIMEOUT_MS);

        Assert.Equal(MetaFileSync.MAX_REQUESTS, Requests.Count);
        Assert.Equal(1, Completions);
    }

    //only the file with no answer stops at the limit; the sync completes when the other file comes
    [Fact]
    public void Tick_CompletesOnlyWhenTheOtherFilesAreDone()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0", "ItemInfo1"));
        Answer(sync, "ItemInfo1");

        for (var i = 0; i < MetaFileSync.MAX_REQUESTS; i++)
            Wait(sync, TIMEOUT_MS);

        Assert.Equal(1, Completions);
        Assert.True(OnDisk("ItemInfo1"));
    }

    //the connection left World, or the server changed: nothing waits for the sync, and the next world entry starts a new one
    [Fact]
    public void End_ClearsTheSyncWithNoCompletion()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));

        sync.End();

        Assert.Equal(0, sync.PendingCount);
        Assert.Equal(0, Completions);

        Wait(sync, TIMEOUT_MS * MetaFileSync.MAX_REQUESTS);

        Assert.Equal(["ItemInfo0"], Requests);
        Assert.Equal(0, Completions);
    }

    [Fact]
    public void End_DropsALateAnswer()
    {
        var sync = NewSync();
        sync.OnChecksums(Listing("ItemInfo0"));
        sync.End();

        Answer(sync, "ItemInfo0");

        Assert.False(OnDisk("ItemInfo0"));
        Assert.Equal(0, Completions);
    }
}
