#region
using System.Text.Json.Serialization;
#endregion

namespace Chaos.Client.Data.AssetPacks;

/// <summary>
///     Parsed representation of a <c>.datf</c> asset pack's <c>_manifest.json</c>. Minimal schema for the v1 pilot —
///     fields can be extended later without breaking existing packs as long as <see cref="SchemaVersion" /> is
///     incremented only on breaking changes.
/// </summary>
public sealed class AssetPackManifest
{
    /// <summary>
    ///     Integer, incremented only on breaking schema changes. Client rejects packs declaring a version it doesn't
    ///     understand.
    /// </summary>
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; }

    /// <summary>
    ///     Unique identifier for this pack. Used for logging and duplicate detection.
    /// </summary>
    [JsonPropertyName("pack_id")]
    public string PackId { get; init; } = string.Empty;

    /// <summary>
    ///     Semver-style pack version. Informational — shown in debug overlay for support.
    /// </summary>
    [JsonPropertyName("pack_version")]
    public string PackVersion { get; init; } = string.Empty;

    /// <summary>
    ///     Enum discriminator selecting which typed pack accessor this pack registers with. Known values: <c>ability_icons</c>.
    ///     Future: <c>tiles</c>, <c>creatures</c>, <c>ui_sprites</c>, <c>effects</c>, <c>bundle</c>.
    /// </summary>
    [JsonPropertyName("content_type")]
    public string ContentType { get; init; } = string.Empty;

    /// <summary>
    ///     Higher wins when multiple packs cover the same asset ID. Default 100 when absent.
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
    ///     Two-element array [width, height] in pixels. For ability icons, <c>[32, 32]</c> in v1.
    /// </summary>
    [JsonPropertyName("dimensions")]
    public int[]? Dimensions { get; init; }

    /// <summary>
    ///     Optional list of 1-based item IDs that participate in the runtime find-and-replace dye pass. Only meaningful
    ///     for <c>item_icons</c> content. Items outside this list ignore the server's color byte and render as-is —
    ///     the renderer also collapses the cache key to <c>color = 0</c> for them.
    /// </summary>
    [JsonPropertyName("dyeable")]
    public int[]? Dyeable { get; init; }
}
