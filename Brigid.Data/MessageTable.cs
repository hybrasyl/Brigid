#region
using System.Text;
#endregion

namespace Brigid.Data;

/// <summary>
///     <c>national.dat / msg.tbl</c> — a plain-text (CRLF-delimited) string table the legacy Dark Ages client
///     indexes by line number for assorted UI strings: status names, money prompts, and the F4 settings-panel
///     option text. There is no structure beyond position, so the client hardcodes which line each string lives
///     on (and the settings' on/off variants sit on adjacent lines in inconsistent order — hence the opaque
///     "nonsense table"). Loaded once at startup; callers read by <b>1-based</b> line number to match how the
///     strings are referenced against the file.
/// </summary>
public sealed class MessageTable
{
    private readonly string[] Lines;

    private MessageTable(string[] lines) => Lines = lines;

    /// <summary>The string at the given 1-based line, or empty if out of range / the table failed to load.</summary>
    public string Get(int line) => line >= 1 && line <= Lines.Length ? Lines[line - 1] : string.Empty;

    /// <summary>Reads <c>msg.tbl</c> from <c>national.dat</c>. Returns an empty table if the entry is missing.</summary>
    public static MessageTable Load()
    {
        if (!DatArchives.National.TryGetValue("msg.tbl", out var entry))
            return new MessageTable([]);

        //CP949 like the other legacy .tbl files; msg.tbl content is ASCII, so this decodes cleanly either way.
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(949) ?? Encoding.UTF8;
        var text = encoding.GetString(entry.ToSpan());

        var lines = text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
            lines[i] = lines[i].TrimEnd('\r');

        return new MessageTable(lines);
    }
}
