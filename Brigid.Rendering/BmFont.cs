#region
using System.Globalization;
using System.Text;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Per-glyph entry from a BMFont .fnt file. Coordinates are atlas-space pixels; offsets position the glyph
///     relative to the cursor+baseline at draw time.
/// </summary>
public sealed record BmFontEntry(
    int Id,
    int X,
    int Y,
    int Width,
    int Height,
    int XOffset,
    int YOffset,
    int XAdvance);

/// <summary>
///     Parsed metadata from a BMFont .fnt file. Supports both the text and binary format variants AngelCode BMFont
///     exports (binary is the default as of v1.14; text is a save-time option). Kerning pairs are ignored.
/// </summary>
public sealed class BmFontMetadata
{
    public int Base { get; init; }
    public Dictionary<int, BmFontEntry> Chars { get; } = [];
    public int LineHeight { get; init; }
    public string PageFile { get; init; } = string.Empty;

    public static BmFontMetadata Parse(string path)
    {
        var bytes = File.ReadAllBytes(path);

        //binary format: starts with 'B' 'M' 'F' <version>
        if (bytes.Length >= 4 && bytes[0] == 'B' && bytes[1] == 'M' && bytes[2] == 'F')
            return ParseBinary(bytes);

        return ParseText(path);
    }

    private static BmFontMetadata ParseBinary(byte[] bytes)
    {
        var pageFile = string.Empty;
        var lineHeight = 0;
        var baseValue = 0;
        var chars = new Dictionary<int, BmFontEntry>();

        //offset 0-3: 'B' 'M' 'F' <version>. Blocks start at offset 4.
        var offset = 4;

        while (offset < bytes.Length)
        {
            var blockId = bytes[offset];
            var blockSize = BitConverter.ToInt32(bytes, offset + 1);
            var blockStart = offset + 5;
            var blockEnd = blockStart + blockSize;

            switch (blockId)
            {
                case 2:                                          //common block
                    lineHeight = BitConverter.ToUInt16(bytes, blockStart + 0);
                    baseValue = BitConverter.ToUInt16(bytes, blockStart + 2);

                    break;

                case 3:                                          //pages block — one null-terminated filename per page
                    pageFile = ReadNullTerminated(bytes, blockStart, blockEnd);

                    break;

                case 4:                                          //chars block — 20 bytes per entry
                    for (var p = blockStart; p + 20 <= blockEnd; p += 20)
                    {
                        var id = (int)BitConverter.ToUInt32(bytes, p + 0);
                        var entry = new BmFontEntry(
                            id,
                            BitConverter.ToUInt16(bytes, p + 4),
                            BitConverter.ToUInt16(bytes, p + 6),
                            BitConverter.ToUInt16(bytes, p + 8),
                            BitConverter.ToUInt16(bytes, p + 10),
                            BitConverter.ToInt16(bytes, p + 12),
                            BitConverter.ToInt16(bytes, p + 14),
                            BitConverter.ToInt16(bytes, p + 16));
                        chars[id] = entry;
                    }

                    break;
            }

            offset = blockEnd;
        }

        var md = new BmFontMetadata
        {
            PageFile = pageFile,
            LineHeight = lineHeight,
            Base = baseValue
        };

        foreach (var (k, v) in chars)
            md.Chars[k] = v;

        return md;
    }

    private static string ReadNullTerminated(byte[] bytes, int start, int end)
    {
        var terminator = Array.IndexOf(bytes, (byte)0, start, end - start);
        var length = (terminator < 0 ? end : terminator) - start;

        return Encoding.ASCII.GetString(bytes, start, length);
    }

    private static BmFontMetadata ParseText(string path)
    {
        var pageFile = string.Empty;
        var lineHeight = 0;
        var baseValue = 0;
        var chars = new Dictionary<int, BmFontEntry>();

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();

            if (line.Length == 0)
                continue;

            var tokens = Tokenize(line);

            if (tokens.Count == 0)
                continue;

            var tag = tokens[0];
            var attrs = ParseAttrs(tokens, 1);

            switch (tag)
            {
                case "common":
                    lineHeight = GetInt(attrs, "lineHeight");
                    baseValue = GetInt(attrs, "base");

                    break;

                case "page":
                    if (attrs.TryGetValue("file", out var file))
                        pageFile = file;

                    break;

                case "char":
                    var id = GetInt(attrs, "id");
                    var entry = new BmFontEntry(
                        id,
                        GetInt(attrs, "x"),
                        GetInt(attrs, "y"),
                        GetInt(attrs, "width"),
                        GetInt(attrs, "height"),
                        GetInt(attrs, "xoffset"),
                        GetInt(attrs, "yoffset"),
                        GetInt(attrs, "xadvance"));
                    chars[id] = entry;

                    break;
            }
        }

        var md = new BmFontMetadata
        {
            PageFile = pageFile,
            LineHeight = lineHeight,
            Base = baseValue
        };

        foreach (var (k, v) in chars)
            md.Chars[k] = v;

        return md;
    }

    private static int GetInt(Dictionary<string, string> attrs, string key)
        => attrs.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static Dictionary<string, string> ParseAttrs(List<string> tokens, int startIndex)
    {
        var attrs = new Dictionary<string, string>();

        for (var i = startIndex; i < tokens.Count; i++)
        {
            var eq = tokens[i].IndexOf('=');

            if (eq < 0)
                continue;

            var key = tokens[i][..eq];
            var value = tokens[i][(eq + 1)..].Trim('"');
            attrs[key] = value;
        }

        return attrs;
    }

    private static List<string> Tokenize(string line)
    {
        var result = new List<string>();
        var i = 0;

        while (i < line.Length)
        {
            while ((i < line.Length) && char.IsWhiteSpace(line[i]))
                i++;

            if (i >= line.Length)
                break;

            var start = i;
            var inQuotes = false;

            while (i < line.Length)
            {
                var c = line[i];

                if (c == '"')
                    inQuotes = !inQuotes;
                else if (char.IsWhiteSpace(c) && !inQuotes)
                    break;

                i++;
            }

            result.Add(line[start..i]);
        }

        return result;
    }
}
