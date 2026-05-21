#region
using System.IO.Compression;
using System.Text.Json;
#endregion

namespace Chaos.Client.Data.AssetPacks;

/// <summary>
///     Discovers and registers <c>.datf</c> asset packs from <c>{DataPath}/hybrasyl-data/</c> at startup. Packs are
///     identified by the <c>content_type</c> field in their embedded <c>_manifest.json</c> and exposed via typed
///     accessors (e.g. <see cref="GetIconPack" />). Missing manifests, malformed JSON, or unsupported schema
///     versions cause the pack to be skipped with a warning rather than failing startup.
///     <para>
///     The registry stores packs through the <see cref="IAssetPack" /> interface in a content-type-keyed dictionary;
///     adding a new pack type is one factory entry in <see cref="Factories" /> plus one typed accessor. Per-type
///     register methods (the older shape) have been collapsed into a single <see cref="TryRegisterByContentType" />.
///     </para>
/// </summary>
public static class AssetPackRegistry
{
    private const int SUPPORTED_SCHEMA_VERSION = 1;
    private const string MANIFEST_ENTRY_NAME = "_manifest.json";
    private const string PACK_SUBFOLDER = "hybrasyl-data";

    //factories know how to build each pack type from a (ZipArchive, AssetPackManifest) pair. Adding a new content
    //type = add one line here + write the pack class. Keyed by manifest.content_type.
    private static readonly Dictionary<string, Func<ZipArchive, AssetPackManifest, IAssetPack>> Factories = new()
    {
        ["ability_icons"] = static (a, m) => new IconPack(a, m),
        ["nation_badges"] = static (a, m) => new NationBadgePack(a, m)
    };

    //registered packs keyed by manifest.content_type. Single pack per type — when multiple packs cover the same
    //content type, the highest priority wins (ties broken by first-registered).
    private static readonly Dictionary<string, IAssetPack> Packs = new();
    private static bool Initialized;

    /// <summary>
    ///     Scans <c>{<see cref="DataContext.DataPath" />}/hybrasyl-data/</c> for <c>*.datf</c> files and registers
    ///     each pack by its declared content type. Idempotent; subsequent calls are no-ops.
    /// </summary>
    public static void Initialize()
    {
        if (Initialized)
            return;

        Initialized = true;

        var packDir = Path.Combine(DataContext.DataPath, PACK_SUBFOLDER);

        if (!Directory.Exists(packDir))
            return;

        foreach (var path in Directory.EnumerateFiles(packDir, "*.datf", SearchOption.TopDirectoryOnly))
            TryRegisterPack(path);
    }

    /// <summary>
    ///     Returns the currently-registered ability-icon pack, or null if no pack of <c>content_type: ability_icons</c>
    ///     is present.
    /// </summary>
    public static IconPack? GetIconPack() => Packs.GetValueOrDefault("ability_icons") as IconPack;

    /// <summary>
    ///     Returns the currently-registered nation-badge pack, or null if no pack of <c>content_type: nation_badges</c>
    ///     is present.
    /// </summary>
    public static NationBadgePack? GetNationBadgePack() => Packs.GetValueOrDefault("nation_badges") as NationBadgePack;

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

            if (!TryRegisterByContentType(archive, manifest))
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

    /// <summary>
    ///     Single consolidated registration path. Looks up a factory for the declared content type, applies the
    ///     priority-wins conflict resolution rule (ties broken by first-registered), and either stores the new pack
    ///     or logs the rejected pack and disposes its archive.
    /// </summary>
    private static bool TryRegisterByContentType(ZipArchive archive, AssetPackManifest manifest)
    {
        if (!Factories.TryGetValue(manifest.ContentType, out var factory))
            return false;

        if (Packs.TryGetValue(manifest.ContentType, out var existing) && (manifest.Priority <= existing.Manifest.Priority))
        {
            LogWarning($"{manifest.ContentType} pack '{manifest.PackId}' ignored — lower priority ({manifest.Priority}) than current pack '{existing.Manifest.PackId}' ({existing.Manifest.Priority})");
            archive.Dispose();

            return true;
        }

        existing?.Dispose();
        Packs[manifest.ContentType] = factory(archive, manifest);

        return true;
    }

    private static void LogWarning(string message) => Console.Error.WriteLine($"[asset-pack] {message}");
}
