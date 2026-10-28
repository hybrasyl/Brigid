using System.IO.Compression;
using Brigid.Data;
using DALib.Cryptography;
using Xunit;

namespace Brigid.Tests;

// The server names each metafile and the client stores it under that name, so a name must stay a bare file name inside
// the metafile directory. A file that the server no longer lists must not stay on disk, because the repository reads
// every ItemInfo* and SEvent* file that it finds.
//TryWrite and PruneUnlisted write through DataDiagnostics.Try, which reads DataDiagnostics.Log
[Collection("DataDiagnostics")]
public sealed class MetaFileCacheTests : IDisposable
{
    private readonly string Root = Path.Combine(Path.GetTempPath(), "brigid-metafile-" + Guid.NewGuid().ToString("N"));

    public MetaFileCacheTests() => Directory.CreateDirectory(Root);

    public void Dispose() => Directory.Delete(Root, true);

    private void Touch(string name) => File.WriteAllBytes(Path.Combine(Root, name), [1]);

    private string[] OnDisk() => Directory.GetFiles(Root).Select(path => Path.GetFileName(path)).Order().ToArray();

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();

        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
            zlib.Write(data);

        return output.ToArray();
    }

    [Fact]
    public void GetChecksum_IsTheCrcOfTheDecompressedFile()
    {
        byte[] content = [1, 2, 3, 4, 5];
        File.WriteAllBytes(Path.Combine(Root, "Light"), Compress(content));

        Assert.Equal(CRC32.Calculate(content), MetaFileCache.GetChecksum(Root, "Light"));
    }

    [Theory]
    [InlineData("Absent")]
    [InlineData("../Light")]
    public void GetChecksum_IsZeroForAnAbsentFileOrARefusedName(string name)
    {
        File.WriteAllBytes(Path.Combine(Root, "Light"), Compress([1]));

        Assert.Equal(0u, MetaFileCache.GetChecksum(Path.Combine(Root, "sub"), name));
    }

    [Fact]
    public void GetChecksum_IsZeroForAFileThatIsNotZlib()
    {
        Touch("Light");

        Assert.Equal(0u, MetaFileCache.GetChecksum(Root, "Light"));
    }

    [Fact]
    public void IsValidPayload_AcceptsAPayloadThatMatchesItsChecksum()
    {
        byte[] content = [1, 2, 3, 4, 5];

        Assert.True(MetaFileCache.IsValidPayload(Compress(content), CRC32.Calculate(content)));
    }

    //an empty payload, a payload that is not zlib, and a payload whose data does not match are all refused
    public static TheoryData<byte[]> BadPayloads => new() { Array.Empty<byte>(), new byte[] { 1, 2, 3 }, Compress([9, 9]) };

    [Theory]
    [MemberData(nameof(BadPayloads))]
    public void IsValidPayload_RefusesABadPayload(byte[] payload)
        => Assert.False(MetaFileCache.IsValidPayload(payload, CRC32.Calculate([1, 2, 3, 4, 5])));

    //the CRC-32 of no data is 0, so an empty payload must not match a checksum of 0
    [Fact]
    public void IsValidPayload_RefusesAnEmptyPayloadForAZeroChecksum()
        => Assert.False(MetaFileCache.IsValidPayload([], 0));

    [Fact]
    public void TryWrite_MakesTheDirectoryWhenItIsNotThere()
    {
        var directory = Path.Combine(Root, "deleted");

        Assert.True(MetaFileCache.TryWrite(directory, "Light", [7, 8]));
        Assert.Equal([7, 8], File.ReadAllBytes(Path.Combine(directory, "Light")));
    }

    [Fact]
    public void TryWrite_WritesABareName()
    {
        Assert.True(MetaFileCache.TryWrite(Root, "Light", [7, 8]));
        Assert.Equal([7, 8], File.ReadAllBytes(Path.Combine(Root, "Light")));
    }

    [Theory]
    [InlineData("../escaped")]
    [InlineData(@"..\escaped")]
    [InlineData("NUL")]
    public void TryWrite_RefusesAnUnsafeNameAndWritesNothing(string name)
    {
        var inner = Path.Combine(Root, "inner");
        Directory.CreateDirectory(inner);

        Assert.False(MetaFileCache.TryWrite(inner, name, [1]));
        Assert.Empty(Directory.GetFiles(Root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void PruneUnlisted_DeletesOnlyTheFilesTheServerNoLongerLists()
    {
        Touch("ItemInfo0");
        Touch("ItemInfo1");
        Touch("ItemInfo2");
        Touch("SEvent1");

        var deleted = MetaFileCache.PruneUnlisted(Root, ["ItemInfo0", "ItemInfo1", "SEvent1", "Light"]);

        Assert.Equal(["ItemInfo2"], deleted);
        Assert.Equal(["ItemInfo0", "ItemInfo1", "SEvent1"], OnDisk());
    }

    [Fact]
    public void PruneUnlisted_AnEmptyListEmptiesTheCacheButLeavesSubdirectories()
    {
        Touch("ItemInfo0");
        Directory.CreateDirectory(Path.Combine(Root, "sub"));

        MetaFileCache.PruneUnlisted(Root, []);

        Assert.Empty(OnDisk());
        Assert.True(Directory.Exists(Path.Combine(Root, "sub")));
    }

    [Fact]
    public void PruneUnlisted_AMissingDirectoryDeletesNothing()
        => Assert.Empty(MetaFileCache.PruneUnlisted(Path.Combine(Root, "absent"), ["ItemInfo0"]));
}
