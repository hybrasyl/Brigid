#region
using System.IO.Compression;
using System.Text.Json;
#endregion

namespace Chaos.Client.Data.AssetPacks;

/// <summary>
///     Discovers and registers <c>.datf</c> asset packs from the configured data directory at startup. Packs are
///     identified by the <c>content_type</c> field in their embedded <c>_manifest.json</c> and exposed via typed
///     accessors (e.g. <see cref="GetIconPack" />). Missing manifests, malformed JSON, or unsupported schema versions
///     cause the pack to be skipped with a warning rather than failing startup.
/// </summary>
public static class AssetPackRegistry
{
    private const int SUPPORTED_SCHEMA_VERSION = 1;
    private const string MANIFEST_ENTRY_NAME = "_manifest.json";

    private static IconPack? CurrentIconPack;
    private static NationBadgePack? CurrentNationBadgePack;
    private static ItemPack? CurrentItemPack;
    private static bool Initialized;

    /// <summary>
    ///     Scans <see cref="DataContext.DataPath" /> for <c>*.datf</c> files and registers each pack by its declared
    ///     content type. Idempotent; subsequent calls are no-ops.
    /// </summary>
    public static void Initialize()
    {
        if (Initialized)
            return;

        Initialized = true;

        if (!Directory.Exists(DataContext.DataPath))
            return;

        foreach (var path in Directory.EnumerateFiles(DataContext.DataPath, "*.datf", SearchOption.TopDirectoryOnly))
            TryRegisterPack(path);
    }

    /// <summary>
    ///     Returns the currently-registered ability-icon pack, or null if no pack of <c>content_type: ability_icons</c>
    ///     is present.
    /// </summary>
    public static IconPack? GetIconPack() => CurrentIconPack;

    /// <summary>
    ///     Returns the currently-registered nation-badge pack, or null if no pack of <c>content_type: nation_badges</c>
    ///     is present.
    /// </summary>
    public static NationBadgePack? GetNationBadgePack() => CurrentNationBadgePack;

    /// <summary>
    ///     Returns the currently-registered item-icon pack, or null if no pack of <c>content_type: item_icons</c> is
    ///     present. Stage 1 is single-pack only — multi-pack support (with per-ID dispatch) lands in Stage 2.
    /// </summary>
    public static ItemPack? GetItemPack() => CurrentItemPack;

    private static void TryRegisterPack(string path)
    {
        ZipArchive? archive = null;

        try
        {
            archive = ZipFile.OpenRead(path);

            var manifestEntry = archive.GetEntry(MANIFEST_ENTRY_NAME);

            if (manifestEntry is null)
            {
                LogWarning($"pack {Path.GetFileName(path)} is missing {MANIFEST_ENTRY_NAME}; skipping");
                archive.Dispose();

                return;
            }

            AssetPackManifest? manifest;

            using (var manifestStream = manifestEntry.Open())
                manifest = JsonSerializer.Deserialize<AssetPackManifest>(manifestStream);

            if (manifest is null)
            {
                LogWarning($"pack {Path.GetFileName(path)} has empty or invalid manifest; skipping");
                archive.Dispose();

                return;
            }

            if (manifest.SchemaVersion > SUPPORTED_SCHEMA_VERSION)
            {
                LogWarning($"pack {Path.GetFileName(path)} declares schema_version={manifest.SchemaVersion} which is newer than supported ({SUPPORTED_SCHEMA_VERSION}); skipping");
                archive.Dispose();

                return;
            }

            if (!RegisterByContentType(archive, manifest))
            {
                LogWarning($"pack {Path.GetFileName(path)} has unknown content_type='{manifest.ContentType}'; skipping");
                archive.Dispose();
            }
        }
        catch (Exception ex)
        {
            //swallow to keep startup resilient; a single bad pack mustn't prevent launch
            LogWarning($"failed to open pack {Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
            archive?.Dispose();
        }
    }

    private static bool RegisterByContentType(ZipArchive archive, AssetPackManifest manifest)
    {
        switch (manifest.ContentType)
        {
            case "ability_icons":
                return TryRegisterIconPack(archive, manifest);

            case "nation_badges":
                return TryRegisterNationBadgePack(archive, manifest);

            case "item_icons":
                return TryRegisterItemPack(archive, manifest);

            default:
                return false;
        }
    }

    private static bool TryRegisterIconPack(ZipArchive archive, AssetPackManifest manifest)
    {
        if (CurrentIconPack is null || manifest.Priority > CurrentIconPack.Manifest.Priority)
        {
            CurrentIconPack?.Dispose();
            CurrentIconPack = new IconPack(archive, manifest);

            return true;
        }

        LogWarning($"icon pack '{manifest.PackId}' ignored — lower priority ({manifest.Priority}) than current pack '{CurrentIconPack.Manifest.PackId}' ({CurrentIconPack.Manifest.Priority})");
        archive.Dispose();

        return true;
    }

    private static bool TryRegisterNationBadgePack(ZipArchive archive, AssetPackManifest manifest)
    {
        if (CurrentNationBadgePack is null || manifest.Priority > CurrentNationBadgePack.Manifest.Priority)
        {
            CurrentNationBadgePack?.Dispose();
            CurrentNationBadgePack = new NationBadgePack(archive, manifest);

            return true;
        }

        LogWarning($"nation badge pack '{manifest.PackId}' ignored — lower priority ({manifest.Priority}) than current pack '{CurrentNationBadgePack.Manifest.PackId}' ({CurrentNationBadgePack.Manifest.Priority})");
        archive.Dispose();

        return true;
    }

    private static bool TryRegisterItemPack(ZipArchive archive, AssetPackManifest manifest)
    {
        //item_icons is strictly additive — first-registered pack wins, no priority resolution (per scoping doc).
        //Stage 2 will expand this to a List<ItemPack> with per-ID dispatch.
        if (CurrentItemPack is null)
        {
            CurrentItemPack = new ItemPack(archive, manifest);

            return true;
        }

        LogWarning($"item pack '{manifest.PackId}' ignored — pack '{CurrentItemPack.Manifest.PackId}' is already registered (item_icons is single-pack in Stage 1, no priority resolution)");
        archive.Dispose();

        return true;
    }

    private static void LogWarning(string message) => Console.Error.WriteLine($"[asset-pack] {message}");
}
