namespace Brigid.UiPrimitivesReport;

/// <summary>An addressable child control of an authored panel — what a datf pack would target for a reskin.</summary>
internal sealed record ChildRef(
    string Ref,             // e.g. "bankshoppanel.titlelabel"
    string Member,          // C# field/property name, or the prefab control name for prefab children
    string Type,            // element control type (arrays/lists unwrapped): UILabel, TextButton, ItemRow, ...
    string Classification,  // "primitive" | "shared" | "one-off"
    string Source,          // "field" | "prefab" | "inline"
    bool Repeated,          // backed by an array/list of controls
    int? Count,             // element count when statically known
    string? PrefabName,     // the legacy control-file entry name when created via CreateXxx("name")
    bool DatBacked,         // draws from a legacy .dat prefab/sprite
    string? Notes,
    IReadOnlyList<ChildRef> Children); // nested sub-children (e.g. rows -> iconimage)

/// <summary>An authored panel/popup the team built, with its addressable children.</summary>
internal sealed record PanelEntry(
    string Class,           // BankShopPanel
    string RefBase,         // bankshoppanel
    string Base,            // immediate base type
    string File,            // repo-relative source path
    string? Summary,        // first line of the class XML doc
    bool DatBacked,         // uses any legacy .dat prefab/sprite
    string? PrefabName,     // the PrefabPanel control-file this panel loads (base("_x")), if any
    IReadOnlyList<string> Assets, // legacy .dat asset names referenced directly
    int AnonymousChildren,  // inline unnamed UI children (not individually addressable)
    IReadOnlyList<ChildRef> Children);

/// <summary>A custom control used as a child by one or more panels — the reusable authored building blocks.</summary>
internal sealed record SharedControl(
    string Type,
    bool Shared,            // used by >= 2 panels
    string? File,
    IReadOnlyList<string> UsedBy);

internal sealed record ReportStats(int Panels, int SharedCustomControls, int AddressableRefs, int DatBackedPanels);

/// <summary>The full authored-UI-primitive report (serialized to JSON; rendered to Markdown).</summary>
internal sealed record Report(
    string GeneratedUtc,
    string RepoRoot,
    string RefScheme,
    ReportStats Stats,
    IReadOnlyList<PanelEntry> Panels,
    IReadOnlyList<SharedControl> SharedControls);
