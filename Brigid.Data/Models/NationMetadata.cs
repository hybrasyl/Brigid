#region
using DALib.Data;
#endregion

namespace Brigid.Data.Models;

/// <summary>
///     Parsed "NationDesc" metadata file. Maps nation IDs to display names.
/// </summary>
public sealed class NationMetadata
{
    /// <summary>
    ///     Nation ID to display name (e.g. 0 -&gt; "Suomi", 1 -&gt; "Mileth").
    /// </summary>
    public IReadOnlyDictionary<int, string> Nations { get; }

    private NationMetadata(Dictionary<int, string> nations) => Nations = nations;

    public static NationMetadata Parse(MetaFile metaFile)
    {
        var nations = new Dictionary<int, string>();

        foreach (var entry in metaFile)
        {
            //key format: "nation_{int}"
            if (!entry.Key.StartsWith("nation_", StringComparison.Ordinal))
                continue;

            if (!int.TryParse(entry.Key.AsSpan(7), out var nationId))
                continue;

            if (entry.Properties.Count > 0)
                nations[nationId] = entry.Properties[0];
        }

        return new NationMetadata(nations);
    }
}