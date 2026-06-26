#region
using System.Text;
using DALib.Data;
#endregion

namespace Brigid.Data.Models;

/// <summary>
///     Name → ordered list of SPF illustration filename variants, merged from the two sources the original Dark Ages
///     client reads: <c>npci.tbl</c> inside <c>npcbase.dat</c> (client-side only, ships with the game data) and the
///     <c>NPCIllust</c> metafile the server pushes at login. <c>npci.tbl</c> occupies the low variant indices;
///     metafile entries are appended after them. A dialog packet's <c>IllustrationIndex</c> picks which variant to
///     load.
/// </summary>
public sealed class NpcIllustrationMetadata
{
    /// <summary>
    ///     NPC name (case-insensitive) → ordered filename variants. Empty if no illustration defined.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Illustrations { get; }

    private NpcIllustrationMetadata(IReadOnlyDictionary<string, IReadOnlyList<string>> illustrations)
        => Illustrations = illustrations;

    /// <summary>
    ///     Builds merged illustration metadata from both sources. Either source may be null.
    ///     <c>npci.tbl</c> entries are loaded first and own the low variant indices; <c>serverMetafile</c> entries
    ///     are then appended to the existing filename list for each name, or create a new entry if the name was not
    ///     present in <c>npci.tbl</c>.
    /// </summary>
    public static NpcIllustrationMetadata Build(DataArchive? npcbaseArchive, MetaFile? serverMetafile)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        MergeNpciTbl(map, npcbaseArchive);
        MergeServerMetafile(map, serverMetafile);

        var frozen = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach ((var name, var filenames) in map)
            frozen[name] = filenames;

        return new NpcIllustrationMetadata(frozen);
    }

    /// <summary>
    ///     Reads <c>npci.tbl</c> from the npcbase archive and populates the merge map. Each line is
    ///     <c>name\tfilename1[\tfilename2...]</c> in CP949. Empty tab fields are skipped (vanilla
    ///     <c>npci.tbl</c> has records with multiple tab separators between name and filename).
    /// </summary>
    private static void MergeNpciTbl(Dictionary<string, List<string>> map, DataArchive? npcbaseArchive)
    {
        if (npcbaseArchive is null)
            return;

        if (!npcbaseArchive.TryGetValue("npci.tbl", out var entry))
            return;

        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(949) ?? Encoding.UTF8;
        var text = encoding.GetString(entry.ToSpan());

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.Length == 0)
                continue;

            var fields = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length < 2)
                continue;

            var name = fields[0].Trim();

            if (name.Length == 0)
                continue;

            if (!map.TryGetValue(name, out var filenames))
            {
                filenames = [];
                map[name] = filenames;
            }

            for (var i = 1; i < fields.Length; i++)
            {
                var filename = fields[i].Trim();

                if (filename.Length > 0)
                    filenames.Add(filename);
            }
        }
    }

    /// <summary>
    ///     Appends server metafile entries to the merge map. Each metafile entry carries one or more filename
    ///     properties; all are appended in declared order after whatever <c>npci.tbl</c> already contributed for
    ///     that name.
    /// </summary>
    private static void MergeServerMetafile(Dictionary<string, List<string>> map, MetaFile? serverMetafile)
    {
        if (serverMetafile is null)
            return;

        foreach (var entry in serverMetafile)
        {
            if (entry.Properties.Count == 0)
                continue;

            if (!map.TryGetValue(entry.Key, out var filenames))
            {
                filenames = [];
                map[entry.Key] = filenames;
            }

            foreach (var prop in entry.Properties)
                if (!string.IsNullOrEmpty(prop))
                    filenames.Add(prop);
        }
    }
}
