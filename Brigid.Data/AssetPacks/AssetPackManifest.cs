#region
using System.Text.Json;
using System.Text.Json.Serialization;
#endregion

namespace Brigid.Data.AssetPacks;

/// <summary>
///     Parsed representation of a <c>.datf</c> asset pack's <c>_manifest.json</c>. Minimal schema for the v1 pilot —
///     fields can be extended later without breaking existing packs as long as <see cref="SchemaVersion" /> is
///     incremented only on breaking changes.
/// </summary>
public sealed class AssetPackManifest
{
    /// <summary>
    ///     Integer, incremented only on breaking schema changes. Client rejects packs declaring a version it doesn't
    ///     understand. Null when the manifest omits the field or sets it to null. The registry refuses both: an absent
    ///     version is not an older version.
    /// </summary>
    [JsonPropertyName("schema_version")]
    public int? SchemaVersion { get; init; }

    /// <summary>
    ///     Identifier for this pack. Required, and used in log messages. The registry does not detect duplicates by this
    ///     id; it orders the packs of one <see cref="ContentType" /> by <see cref="Priority" />, then by file name.
    /// </summary>
    [JsonPropertyName("pack_id")]
    public string PackId { get; init; } = string.Empty;

    /// <summary>
    ///     Semver-style pack version. Informational — shown in debug overlay for support.
    /// </summary>
    [JsonPropertyName("pack_version")]
    public string PackVersion { get; init; } = string.Empty;

    /// <summary>
    ///     Enum discriminator selecting which pack list of the registry this pack goes in. The registry's
    ///     <c>Factories</c> dictionary lists every recognized value; manifests declaring an unknown content type are
    ///     skipped at load with a warning. Known values: <c>ability_icons</c>, <c>ambient_sounds</c>, <c>nation_badges</c>,
    ///     <c>item_icons</c>, <c>npc_portraits</c>, <c>static_tiles</c>, <c>legend_mark_icons</c>,
    ///     <c>ui_sprite_overrides</c>, <c>creature_sprites</c>, <c>music</c>, <c>sound_effects</c>, <c>world_maps</c>,
    ///     <c>town_maps</c>, <c>ui_theme</c>.
    ///     Future content types land as additions to that dictionary.
    /// </summary>
    [JsonPropertyName("content_type")]
    public string ContentType { get; init; } = string.Empty;

    /// <summary>
    ///     The order of the packs of one content type: when two packs cover the same key, the pack with the higher
    ///     priority wins, and at the same priority the pack that registered first (in file-name order) wins; see
    ///     <see cref="AssetPackRegistry" />. A temporary pack, for example a holiday pack, uses a higher priority.
    ///     Default 100 when absent.
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; init; } = 100;

    /// <summary>
    ///     Capability declaration: which asset categories the pack participates in, plus per-category metadata the
    ///     renderer needs (e.g. <c>dimensions</c> drives the draw-offset calculation). NOT a range declaration —
    ///     coverage is emergent from which PNG files the pack actually contains.
    /// </summary>
    [JsonPropertyName("covers")]
    public Dictionary<string, AssetPackCoverageEntry> Covers { get; init; } = new();
}

/// <summary>
///     Per-category metadata inside <see cref="AssetPackManifest.Covers" />. For ability icons, <see cref="Dimensions" />
///     drives whether the client treats the icon as legacy-compatible (31x31) or modern-offset (32x32).
/// </summary>
public sealed class AssetPackCoverageEntry
{
    /// <summary>
    ///     Two-element array [width, height] in pixels. For ability icons, <c>[32, 32]</c> in v1. For
    ///     <c>npc_portraits</c>, the uniform square size every portrait in the pack must match (e.g. <c>[200, 200]</c>);
    ///     non-square values cause the pack to register with an empty lookup and never serve illustrations.
    /// </summary>
    [JsonPropertyName("dimensions")]
    public int[]? Dimensions { get; init; }

    /// <summary>
    ///     Optional list of 1-based item IDs that take the runtime find-and-replace dye pass. Only meaningful for
    ///     <c>item_icons</c> content. When a pack has this list, it is the exact set of dyeable items, and the client does
    ///     not detect dyeability from the art. Older scaffold packs write it; Taliesin does not.
    /// </summary>
    [JsonPropertyName("dyeable")]
    public int[]? Dyeable { get; init; }

    /// <summary>
    ///     Optional list of 1-based item IDs that do not take the dye, although their art contains the canonical dye ramp.
    ///     Only meaningful for <c>item_icons</c> content, and only when the pack has no <see cref="Dyeable" /> list.
    ///     Taliesin writes this list.
    /// </summary>
    [JsonPropertyName("no_dye")]
    public int[]? NoDye { get; init; }

    /// <summary>
    ///     For <c>npc_portraits</c>, a flat <c>(portrait key → PNG filename)</c> lookup. The key is the literal
    ///     value of the Hybrasyl XML <c>Portrait</c> attribute as the server publishes it via the NPCIllust metafile
    ///     — verbatim, no extension stripping or normalization. Examples: <c>"inn.spf"</c> for an NPC declared
    ///     <c>Portrait="inn.spf"</c>, <c>"Gobalt"</c> for an NPC declared <c>Portrait="Gobalt"</c>. The value is the
    ///     PNG entry name inside the ZIP root. Lookup is case-insensitive at runtime. Absent or empty means the pack
    ///     covers no portraits.
    /// </summary>
    [JsonPropertyName("portraits")]
    public Dictionary<string, string>? Portraits { get; init; }

    /// <summary>
    ///     The members that this class does not name. For <c>ambient_sounds</c>, each member name is an ambient id and
    ///     its value is the entry for that id (for example <c>"1": { "loop": false }</c>). Other content types do not
    ///     read these members.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? OtherMembers { get; init; }
}
