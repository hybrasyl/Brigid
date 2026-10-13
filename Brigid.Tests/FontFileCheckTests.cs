#region
using System.Buffers.Binary;
using Brigid.Rendering;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     <see cref="FontFileCheck" /> refuses a font file that would make the unsafe font parser read past its buffer, which
///     can end the process. Every shipped face passes.
/// </summary>
public class FontFileCheckTests
{
    private static string FontsDir => Path.Combine(AppContext.BaseDirectory, "Content", "Fonts");

    public static TheoryData<string> ShippedFonts()
    {
        var data = new TheoryData<string>();

        foreach (var file in Directory.EnumerateFiles(FontsDir, "*.ttf"))
            data.Add(Path.GetFileName(file));

        return data;
    }

    [Theory]
    [MemberData(nameof(ShippedFonts))]
    public void EveryShippedFontPasses(string file)
        => Assert.True(FontFileCheck.TryValidate(File.ReadAllBytes(Path.Combine(FontsDir, file)), out var problem), problem);

    [Fact]
    public void AFontThatIsCutShortIsRefused()
    {
        var bytes = File.ReadAllBytes(Path.Combine(FontsDir, "UncialAntiqua-Regular.ttf"));

        Assert.False(FontFileCheck.TryValidate(bytes.AsSpan(0, bytes.Length / 2), out _));
        Assert.False(FontFileCheck.TryValidate(bytes.AsSpan(0, 40), out _));
    }

    [Fact]
    public void ASignatureWithNoTablesBehindItIsRefused()
    {
        //the bytes of the display-font test that crashed the test host: a signature, then a table count of 0x0102
        Assert.False(FontFileCheck.TryValidate([0x00, 0x01, 0x00, 0x00, 0x01, 0x02, 0x03, 0x04], out _));
    }

    [Fact]
    public void ATableOutsideTheFileIsRefused()
    {
        var bytes = MinimalFont();

        //move the first table's offset past the end
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12 + 8), (uint)bytes.Length);

        Assert.False(FontFileCheck.TryValidate(bytes, out var problem));
        Assert.Contains("past the end", problem);
    }

    [Fact]
    public void AMissingRequiredTableIsRefused()
    {
        var bytes = MinimalFont();

        //rename cmap
        "xmap"u8.CopyTo(bytes.AsSpan(12));

        Assert.False(FontFileCheck.TryValidate(bytes, out var problem));
        Assert.Contains("'cmap'", problem);
    }

    [Fact]
    public void AHeadTableTooShortForTheParserIsRefused()
    {
        var bytes = MinimalFont();

        //head is the second record; its length goes below the 54 bytes the parser reads
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12 + 16 + 12), 20);

        Assert.False(FontFileCheck.TryValidate(bytes, out var problem));
        Assert.Contains("'head'", problem);
    }

    [Fact]
    public void AFontWithNoOutlinesIsRefused()
    {
        var bytes = MinimalFont();

        //rename glyf
        "xlyf"u8.CopyTo(bytes.AsSpan(12 + 5 * 16));

        Assert.False(FontFileCheck.TryValidate(bytes, out var problem));
        Assert.Contains("outlines", problem);
    }

    [Fact]
    public void TheMinimalFontPasses() => Assert.True(FontFileCheck.TryValidate(MinimalFont(), out var problem), problem);

    [Fact]
    public void ACollectionWhoseFirstFontIsOutsideTheFileIsRefused()
    {
        var bytes = new byte[16];
        "ttcf"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), 4096);

        Assert.False(FontFileCheck.TryValidate(bytes, out _));
    }

    [Fact]
    public void ACollectionIsCheckedAtItsFirstFont()
    {
        var font = MinimalFont();
        var bytes = new byte[16 + font.Length];
        "ttcf"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), 16);

        //the table offsets in a collection count from the start of the file
        for (var i = 0; i < 7; i++)
        {
            var at = 12 + i * 16 + 8;
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(at), BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(at)) + 16);
        }

        font.CopyTo(bytes, 16);

        Assert.True(FontFileCheck.TryValidate(bytes, out var problem), problem);
    }

    //a table directory with the seven tables the check needs, each in range, with zeroed table data
    private static byte[] MinimalFont()
    {
        (string Tag, int Length)[] tables = [("cmap", 4), ("head", 54), ("hhea", 36), ("hmtx", 4), ("maxp", 6), ("glyf", 4), ("loca", 4)];

        var dataStart = 12 + tables.Length * 16;
        var bytes = new byte[dataStart + tables.Sum(table => table.Length)];

        BinaryPrimitives.WriteUInt32BigEndian(bytes, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)tables.Length);

        var offset = dataStart;

        for (var i = 0; i < tables.Length; i++)
        {
            var record = bytes.AsSpan(12 + i * 16);
            System.Text.Encoding.ASCII.GetBytes(tables[i].Tag).CopyTo(record);
            BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)tables[i].Length);
            offset += tables[i].Length;
        }

        return bytes;
    }
}
