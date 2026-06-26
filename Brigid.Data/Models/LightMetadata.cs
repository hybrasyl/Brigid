#region
using DALib.Data;
#endregion

namespace Brigid.Data.Models;

/// <summary>
///     A single light level entry: alpha and RGB color for a darkness overlay at a specific level.
/// </summary>
public sealed record LightPropertyEntry(
    byte Alpha,
    byte R,
    byte G,
    byte B);

/// <summary>
///     Parsed "Light" metadata file. Contains per-level light properties keyed by "{typeName}_{hexLevel}" and
///     map-to-light-type mappings keyed by map ID.
/// </summary>
public sealed class LightMetadata
{
    /// <summary>
    ///     Light color/alpha entries keyed by lowercase "{typeName}_{hexEnumValue}" (e.g. "default_0").
    /// </summary>
    public IReadOnlyDictionary<string, LightPropertyEntry> LightProperties { get; }

    /// <summary>
    ///     Map ID to light type name (lowercase).
    /// </summary>
    public IReadOnlyDictionary<short, string> MapLightTypes { get; }

    private LightMetadata(Dictionary<string, LightPropertyEntry> lightProperties, Dictionary<short, string> mapLightTypes)
    {
        LightProperties = lightProperties;
        MapLightTypes = mapLightTypes;
    }

    /// <summary>
    ///     Parses the "Light" MetaFile into light property entries and map-to-light-type mappings.
    /// </summary>
    public static LightMetadata Parse(MetaFile metaFile)
    {
        var lightProperties = new Dictionary<string, LightPropertyEntry>(StringComparer.OrdinalIgnoreCase);
        var mapLightTypes = new Dictionary<short, string>();

        foreach (var entry in metaFile)
            if (entry.Key.Contains('_'))
            {
                if (entry.Properties.Count < 6)
                    continue;

                if (!byte.TryParse(entry.Properties[2], out var alpha)
                    || !byte.TryParse(entry.Properties[3], out var r)
                    || !byte.TryParse(entry.Properties[4], out var g)
                    || !byte.TryParse(entry.Properties[5], out var b))
                    continue;

                lightProperties[entry.Key.ToLowerInvariant()] = new LightPropertyEntry(
                    alpha,
                    r,
                    g,
                    b);
            } else if (short.TryParse(entry.Key, out var mapId) && (entry.Properties.Count > 0))
                mapLightTypes[mapId] = entry.Properties[0]
                                            .ToLowerInvariant();

        return new LightMetadata(lightProperties, mapLightTypes);
    }
}