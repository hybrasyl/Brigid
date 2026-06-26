#region
using DALib.Data;
#endregion

namespace Brigid.Data.Models;

/// <summary>
///     A single parsed item from an ItemInfo metadata file.
/// </summary>
public sealed record ItemMetadataEntry
{
    public string Category { get; init; } = string.Empty;
    public byte Class { get; init; }
    public string Description { get; init; } = string.Empty;
    public int Level { get; init; }
    public required string Name { get; init; }
    public int Weight { get; init; }

    /// <summary>
    ///     Parses all entries from one or more ItemInfo MetaFiles into a single list.
    /// </summary>
    public static IReadOnlyList<ItemMetadataEntry> ParseAll(IEnumerable<MetaFile> metaFiles)
    {
        var items = new List<ItemMetadataEntry>();

        foreach (var metaFile in metaFiles)
            foreach (var entry in metaFile)
            {
                var parsed = ParseEntry(entry);

                if (parsed is not null)
                    items.Add(parsed);
            }

        return items;
    }

    /// <summary>
    ///     Parses a single MetaFileEntry into an ItemMetadataEntry. Returns null if the entry has insufficient properties.
    /// </summary>
    public static ItemMetadataEntry? ParseEntry(MetaFileEntry entry) => ParseEntry(entry.Key, entry.Properties);

    /// <summary>
    ///     Parses item metadata from a name and raw property list. Returns null if the properties are insufficient.
    /// </summary>
    public static ItemMetadataEntry? ParseEntry(string name, IReadOnlyList<string> properties)
    {
        if (properties.Count < 5)
            return null;

        int.TryParse(properties[0], out var level);
        byte.TryParse(properties[1], out var cls);
        int.TryParse(properties[2], out var weight);

        return new ItemMetadataEntry
        {
            Name = name,
            Level = level,
            Class = cls,
            Weight = weight,
            Category = properties[3],
            Description = properties[4]
        };
    }
}