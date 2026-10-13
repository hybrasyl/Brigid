#region
using System.Buffers.Binary;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     A structural check of a TrueType or OpenType file before the font parser reads it. The parser (StbTrueType) reads
///     the table directory through unsafe pointers and does no bounds checks. A file that is cut short or damaged makes it
///     read past the end of the buffer, and that can end the process, which no catch stops. This check reads the same
///     structures with bounds checks, so such a file is refused with a reason.
///     <para>
///         The check covers the table directory and the fixed fields that the parser reads at load: every table lies inside
///         the file, and the tables that the parser needs are present and long enough. It cannot prove that the glyph data
///         inside the tables is sound.
///     </para>
/// </summary>
internal static class FontFileCheck
{
    private const int SFNT_HEADER_SIZE = 12;
    private const int TABLE_RECORD_SIZE = 16;
    private const int TTC_HEADER_SIZE = 16;

    //no real font has this many tables; a larger count is a damaged header
    private const int MAX_TABLES = 512;

    //the tables the parser reads at load, with the length its fixed reads need: head reads indexToLocFormat at 50, hhea
    //reads numberOfHMetrics at 34, maxp reads numGlyphs at 4, cmap reads its subtable count at 2
    private static readonly (string Tag, int MinLength)[] RequiredTables =
    [
        ("cmap", 4),
        ("head", 54),
        ("hhea", 36),
        ("hmtx", 0),
        ("maxp", 6)
    ];

    /// <summary>
    ///     Checks a font file. Returns false, with the reason in <paramref name="problem" />, when the parser would read
    ///     outside the file. A font collection is checked at its first font, which is the one the parser loads.
    /// </summary>
    public static bool TryValidate(ReadOnlySpan<byte> bytes, out string problem)
    {
        if (bytes.Length < 4)
        {
            problem = "the file is too short";

            return false;
        }

        var offset = 0;

        if (bytes[..4].SequenceEqual("ttcf"u8))
        {
            if (bytes.Length < TTC_HEADER_SIZE)
            {
                problem = "the collection header is cut short";

                return false;
            }

            if (BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) == 0)
            {
                problem = "the collection has no fonts";

                return false;
            }

            var first = BinaryPrimitives.ReadUInt32BigEndian(bytes[12..]);

            if (first > (uint)(bytes.Length - SFNT_HEADER_SIZE))
            {
                problem = "the first font of the collection is outside the file";

                return false;
            }

            offset = (int)first;
        }

        return TryValidateSfnt(bytes, offset, out problem);
    }

    private static bool TryValidateSfnt(ReadOnlySpan<byte> bytes, int offset, out string problem)
    {
        if (offset + SFNT_HEADER_SIZE > bytes.Length)
        {
            problem = "the font header is cut short";

            return false;
        }

        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset + 4)..]);

        if ((tableCount == 0) || (tableCount > MAX_TABLES))
        {
            problem = $"the font header gives {tableCount} tables";

            return false;
        }

        var directoryEnd = (long)offset + SFNT_HEADER_SIZE + (long)tableCount * TABLE_RECORD_SIZE;

        if (directoryEnd > bytes.Length)
        {
            problem = $"the table directory ({tableCount} tables) runs past the end of the file";

            return false;
        }

        var lengths = new Dictionary<string, long>(StringComparer.Ordinal);

        for (var i = 0; i < tableCount; i++)
        {
            var record = bytes.Slice(offset + SFNT_HEADER_SIZE + i * TABLE_RECORD_SIZE, TABLE_RECORD_SIZE);
            var tag = System.Text.Encoding.ASCII.GetString(record[..4]);
            long tableOffset = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            long tableLength = BinaryPrimitives.ReadUInt32BigEndian(record[12..]);

            if (tableOffset + tableLength > bytes.Length)
            {
                problem = $"the '{tag}' table runs past the end of the file";

                return false;
            }

            lengths.TryAdd(tag, tableLength);
        }

        foreach (var (tag, minLength) in RequiredTables)
        {
            if (!lengths.TryGetValue(tag, out var length))
            {
                problem = $"the font has no '{tag}' table";

                return false;
            }

            if (length < minLength)
            {
                problem = $"the '{tag}' table is too short";

                return false;
            }
        }

        //outlines: TrueType glyphs (glyf with loca) or OpenType CFF
        var hasGlyf = lengths.ContainsKey("glyf") && lengths.ContainsKey("loca");
        var hasCff = lengths.ContainsKey("CFF ") || lengths.ContainsKey("CFF2");

        if (!hasGlyf && !hasCff)
        {
            problem = "the font has no glyph outlines (glyf and loca, or CFF)";

            return false;
        }

        problem = string.Empty;

        return true;
    }
}
