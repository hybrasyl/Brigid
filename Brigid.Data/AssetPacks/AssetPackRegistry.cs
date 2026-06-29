#region
using System.IO.Compression;
using System.Text.Json;
#endregion

namespace Brigid.Data.AssetPacks;

/// <summary>
///     Discovers and registers <c>.datf</c> asset packs from the per-user erisco assets directory
///     (<see cref="AppPaths.AssetsDir" />) at startup. Packs are identified by the <c>content_type</c> field in their
///     embedded <c>_manifest.json</c> and exposed via typed accessors (e.g. <see cref="GetIconPack" />). Missing
///     manifests, malformed JSON, or unsupported schema versions cause the pack to be skipped with a warning rather
///     than failing startup.
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

    //the pre-erisco pack location, relative to the retail data path. Packs found here are migrated once into
    //AppPaths.AssetsDir on first launch after the move, smoothing existing manual installs.
    private const string LEGACY_PACK_SUBFOLDER = "hybrasyl-data";

    //factories know how to build each pack type from a (ZipArchive, AssetPackManifest) pair. Adding a new content
    //type = add one line here + write the pack class. Keyed by manifest.content_type.
    private static readonly Dictionary<string, Func<ZipArchive, AssetPackManifest, IAssetPack>> Factories = new()
    {
        ["ability_icons"] = static (a, m) => new IconPack(a, m),
        ["nation_badges"] = static (a, m) => new NationBadgePack(a, m),
        ["item_icons"]          = static (a, m) => new ItemPack(a, m),
        ["npc_portraits"]       = static (a, m) => new NpcPortraitPack(a, m),
        ["static_tiles"]        = static (a, m) => new StaticTilePack(a, m),
        ["legend_mark_icons"]   = static (a, m) => new LegendMarkIconPack(a, m),
        ["ui_sprite_overrides"] = static (a, m) => new UiSpriteOverridePack(a, m),
        ["creature_sprites"]    = static (a, m) => new CreaturePack(a, m),
        ["music"]               = static (a, m) => new MusicPack(a, m),
        ["sound_effects"]       = static (a, m) => new SfxPack(a, m),
        ["world_maps"]          = static (a, m) => new WorldMapPack(a, m)
    };

    //registered packs keyed by manifest.content_type. Single pack per type — when multiple packs cover the same
    //content type, the highest priority wins (ties broken by first-registered).
    private static readonly Dictionary<string, IAssetPack> Packs = new();
    private static bool Initialized;

    /// <summary>
    ///     Scans <see cref="AppPaths.AssetsDir" /> for <c>*.datf</c> files and registers each pack by its declared
    ///     content type. Ensures the directory exists and migrates any legacy <c>{DataPath}/hybrasyl-data/</c> packs
    ///     into it once. Idempotent; subsequent calls are no-ops. An empty/absent directory is a clean no-op — renderers
    ///     then fall through to legacy assets.
    /// </summary>
    public static void Initialize()
    {
        if (Initialized)
            return;

        Initialized = true;

        var packDir = AppPaths.AssetsDir;

        try
        {
            Directory.CreateDirectory(packDir);
        } catch
        {
            //best effort — if the dir can't be created, the enumerate below simply finds nothing
        }

        TryMigrateLegacyPacks(packDir);

        if (!Directory.Exists(packDir))
        {
            LogInfo($"no asset-pack folder at '{packDir}'; skipping pack discovery");

            return;
        }

        var discovered = 0;
        var registeredBefore = Packs.Count;

        foreach (var path in Directory.EnumerateFiles(packDir, "*.datf", SearchOption.TopDirectoryOnly))
        {
            discovered++;
            TryRegisterPack(path);
        }

        if (discovered == 0)
        {
            LogInfo($"asset-pack folder '{packDir}' contains no .datf files");

            return;
        }

        LogInfo($"registered {Packs.Count - registeredBefore} asset pack(s) from '{packDir}'");
    }

    /// <summary>
    ///     One-time, best-effort copy of legacy <c>{DataPath}/hybrasyl-data/*.datf</c> packs into
    ///     <paramref name="assetsDir" />, run only when the new directory holds no packs yet so launcher-managed content
    ///     is never clobbered. Packs are normally managed by the launcher; this only smooths existing manual installs.
    ///     Never throws.
    /// </summary>
    private static void TryMigrateLegacyPacks(string assetsDir)
    {
        try
        {
            if (string.IsNullOrEmpty(DataContext.DataPath))
                return;

            //migrate only into an empty assets dir — never overwrite content already placed here
            if (!Directory.Exists(assetsDir) || Directory.EnumerateFiles(assetsDir, "*.datf", SearchOption.TopDirectoryOnly).Any())
                return;

            var legacyDir = Path.Combine(DataContext.DataPath, LEGACY_PACK_SUBFOLDER);

            if (!Directory.Exists(legacyDir))
                return;

            var migrated = 0;

            foreach (var src in Directory.EnumerateFiles(legacyDir, "*.datf", SearchOption.TopDirectoryOnly))
            {
                var dst = Path.Combine(assetsDir, Path.GetFileName(src));

                if (File.Exists(dst))
                    continue;

                File.Copy(src, dst);
                migrated++;
            }

            if (migrated > 0)
                LogInfo($"migrated {migrated} legacy asset pack(s) from '{legacyDir}' to '{assetsDir}'");
        } catch
        {
            //best effort — a failed copy just means nothing migrated; the launcher can re-fetch packs
        }
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

    /// <summary>
    ///     Returns the currently-registered item-icon pack, or null if no pack of <c>content_type: item_icons</c> is
    ///     present.
    /// </summary>
    public static ItemPack? GetItemPack() => Packs.GetValueOrDefault("item_icons") as ItemPack;

    /// <summary>
    ///     Returns the currently-registered NPC-portrait pack, or null if no pack of
    ///     <c>content_type: npc_portraits</c> is present.
    /// </summary>
    public static NpcPortraitPack? GetNpcPortraitPack() => Packs.GetValueOrDefault("npc_portraits") as NpcPortraitPack;

    /// <summary>
    ///     Returns the currently-registered static-tile pack, or null if no pack of <c>content_type: static_tiles</c>
    ///     is present.
    /// </summary>
    public static StaticTilePack? GetStaticTilePack() => Packs.GetValueOrDefault("static_tiles") as StaticTilePack;

    /// <summary>
    ///     Returns the currently-registered legend-mark-icon pack, or null if no pack of
    ///     <c>content_type: legend_mark_icons</c> is present.
    /// </summary>
    public static LegendMarkIconPack? GetLegendMarkIconPack() => Packs.GetValueOrDefault("legend_mark_icons") as LegendMarkIconPack;

    /// <summary>
    ///     Returns the currently-registered UI-sprite-override pack, or null if no pack of
    ///     <c>content_type: ui_sprite_overrides</c> is present.
    /// </summary>
    public static UiSpriteOverridePack? GetUiSpriteOverridePack() => Packs.GetValueOrDefault("ui_sprite_overrides") as UiSpriteOverridePack;

    /// <summary>
    ///     Returns the currently-registered creature-sprite pack, or null if no pack of
    ///     <c>content_type: creature_sprites</c> is present.
    /// </summary>
    public static CreaturePack? GetCreaturePack() => Packs.GetValueOrDefault("creature_sprites") as CreaturePack;

    /// <summary>
    ///     Returns the currently-registered music pack, or null if no pack of <c>content_type: music</c> is present.
    /// </summary>
    public static MusicPack? GetMusicPack() => Packs.GetValueOrDefault("music") as MusicPack;

    /// <summary>
    ///     Returns the currently-registered sound-effects pack, or null if no pack of <c>content_type: sound_effects</c>
    ///     is present.
    /// </summary>
    public static SfxPack? GetSfxPack() => Packs.GetValueOrDefault("sound_effects") as SfxPack;

    /// <summary>
    ///     Returns the currently-registered world-map pack, or null if no pack of <c>content_type: world_maps</c> is
    ///     present.
    /// </summary>
    public static WorldMapPack? GetWorldMapPack() => Packs.GetValueOrDefault("world_maps") as WorldMapPack;

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

    private static void LogInfo(string message) => Console.Out.WriteLine($"[asset-pack] {message}");
}
