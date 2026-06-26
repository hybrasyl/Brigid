#region
using System.Text;
using Brigid.Data.Models;
using DALib.Data;
#endregion

namespace Brigid.Data.Utilities;

/// <summary>
///     Parses Skill_e.tbl and Skill_i.tbl from Legend.dat into an <see cref="AbilityAnimationTable" />.
/// </summary>
/// <remarks>
///     Each file is a tab-delimited text table (EUC-KR encoded) mapping skill animation entry indices to sets of armor
///     sprite IDs that support the animation.
/// </remarks>
public static class AbilityAnimationTableParser
{
    private static readonly Encoding EucKr = Encoding.GetEncoding(949);

    /// <summary>
    ///     Parses normal and overcoat armor animation tables into a single merged <see cref="AbilityAnimationTable" />.
    /// </summary>
    /// <remarks>
    ///     Overcoat armor IDs from Skill_i.tbl are stored with a +1000 offset to distinguish them from normal armor IDs from
    ///     Skill_e.tbl.
    /// </remarks>
    public static AbilityAnimationTable Parse(DataArchiveEntry normalEntry, DataArchiveEntry? overcoatEntry)
    {
        var data = new Dictionary<byte, HashSet<int>>();

        ParseInto(data, normalEntry, 0);

        if (overcoatEntry is not null)
            ParseInto(data, overcoatEntry, 1000);

        return new AbilityAnimationTable(data);
    }

    private static void ParseInto(Dictionary<byte, HashSet<int>> data, DataArchiveEntry entry, int idOffset)
    {
        var text = EucKr.GetString(entry.ToSpan());

        var lines = text.Split(
            [
                '\r',
                '\n'
            ],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.StartsWith(';'))
                continue;

            var columns = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);

            if (columns.Length < 5)
                continue;

            if (!byte.TryParse(columns[0], out var entryIndex))
                continue;

            if (!data.TryGetValue(entryIndex, out var allowedIds))
            {
                allowedIds = [];
                data[entryIndex] = allowedIds;
            }

            //columns 4+ are space-separated armor sprite ids (may span multiple tab columns)
            for (var i = 4; i < columns.Length; i++)
            {
                var tokens = columns[i]
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);

                foreach (var token in tokens)
                    if (int.TryParse(token, out var armorId))
                        allowedIds.Add(armorId + idOffset);
            }
        }
    }
}