using System.IO.Compression;
using Brigid.Data;
using DALib.Cryptography;
using Xunit;

namespace Brigid.Tests;

// The server names each metafile and the client stores it under that name, so a name must stay a bare file name inside
// the metafile directory.
public sealed class MetaFileCacheTests : IDisposable
{
    private readonly string Root = Path.Combine(Path.GetTempPath(), "brigid-metafile-" + Guid.NewGuid().ToString("N"));

    public MetaFileCacheTests() => Directory.CreateDirectory(Root);

    public void Dispose() => Directory.Delete(Root, true);

    private void Touch(string name) => File.WriteAllBytes(Path.Combine(Root, name), [1]);

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
}
