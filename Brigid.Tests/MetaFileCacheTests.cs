using System.IO.Compression;
using Brigid.Data;
using DALib.Cryptography;
using Xunit;

namespace Brigid.Tests;

// The server names each metafile and the client stores it under that name, so a name must stay a bare file name inside
// the metafile directory. A file that the server no longer lists must not stay on disk, because the repository reads
// every ItemInfo* and SEvent* file that it finds.
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

    [Theory]
    [InlineData("ItemInfo0")]
    [InlineData("SClass1")]
    [InlineData("SEvent12")]
    [InlineData("NationDesc")]
    [InlineData("Light")]
    [InlineData("a..b")]
    [InlineData("CONSOLE")]
    public void IsSafeName_AcceptsBareFileNames(string name) => Assert.True(MetaFileCache.IsSafeName(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]
    [InlineData(@"..\x.bat")]
    [InlineData("../x.bat")]
    [InlineData(@"sub\x")]
    [InlineData("sub/x")]
    [InlineData(@"C:\Users\u\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\x.bat")]
    [InlineData("/home/u/.bashrc")]
    [InlineData(@"\\host\share\x")]
    [InlineData("x\0y")]
    [InlineData("a:b")]
    [InlineData("a|b")]
    [InlineData("Light.")]
    [InlineData("Light ")]
    [InlineData("NUL")]
    [InlineData("con.txt")]
    [InlineData("Com1")]
    public void IsSafeName_RejectsAnythingThatIsNotTheSameBareFileOnEveryPlatform(string? name)
        => Assert.False(MetaFileCache.IsSafeName(name));

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
    public void CheckReceived_AcceptsAPayloadThatMatchesItsChecksum()
    {
        byte[] content = [1, 2, 3, 4, 5];

        Assert.Equal(MetaFileReceipt.Valid, MetaFileCache.CheckReceived(Compress(content), CRC32.Calculate(content), 1, 3));
    }

    //an empty payload, a payload that is not zlib, and a payload whose data does not match all ask for the file again
    public static TheoryData<byte[]> BadPayloads => new() { Array.Empty<byte>(), new byte[] { 1, 2, 3 }, Compress([9, 9]) };

    [Theory]
    [MemberData(nameof(BadPayloads))]
    public void CheckReceived_RequestsABadPayloadAgainBelowTheLimit(byte[] payload)
        => Assert.Equal(MetaFileReceipt.Retry, MetaFileCache.CheckReceived(payload, CRC32.Calculate([1, 2, 3, 4, 5]), 2, 3));

    [Theory]
    [MemberData(nameof(BadPayloads))]
    public void CheckReceived_GivesUpOnABadPayloadAtTheLimit(byte[] payload)
        => Assert.Equal(MetaFileReceipt.GiveUp, MetaFileCache.CheckReceived(payload, CRC32.Calculate([1, 2, 3, 4, 5]), 3, 3));

    //the CRC-32 of no data is 0, so an empty payload must not match a checksum of 0
    [Fact]
    public void CheckReceived_RefusesAnEmptyPayloadForAZeroChecksum()
        => Assert.Equal(MetaFileReceipt.Retry, MetaFileCache.CheckReceived([], 0, 1, 3));

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
