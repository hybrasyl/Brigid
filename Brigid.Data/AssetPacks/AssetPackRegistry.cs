#region
using System.IO.Compression;
using System.Text.Json;
using SkiaSharp;
#endregion

namespace Brigid.Data.AssetPacks;

/// <summary>
///     Discovers and registers <c>.datf</c> asset packs from the per-user Erisco assets directory
///     (<see cref="AppPaths.AssetsDir" />) at startup. Packs are identified by the <c>content_type</c> field in their
///     embedded <c>_manifest.json</c>. Missing manifests, malformed JSON, or unsupported schema versions cause the pack
///     to be skipped with a warning rather than failing startup.
///     <para>
///     A content type can have several packs. The registry keeps them in order of the manifest <c>priority</c>, the
///     highest first; packs with the same priority stay in registration order, and the registry registers the files in
///     file-name order (ordinal, case ignored), so the order is the same on every OS. A lookup asks for a key (an id, a
///     name) and tries each pack of the type in that order: a pack that has no entry for the key, or whose entry does
///     not decode, passes the lookup to the next pack, and after the last pack the caller uses legacy art. So when two
///     packs cover the same key, the pack with the higher priority wins, and at the same priority the pack that
///     registered first wins. A temporary pack, for example a holiday pack, uses a higher priority to replace the art
///     of the packs below it while it is installed. Registration logs a warning that names both packs, the shared keys
///     and the pack that a lookup asks first.
///     </para>
///     <para>
///     There are four kinds of lookup. For a key that is one image, the registry gives a <c>TryGet…</c> lookup that
///     decodes the image and asks the next pack when the decode fails. For a key with several values that must come
///     from one pack (a creature: its frames, its pair count and its bounding box; an ambient bed: its sound and its loop
///     flag), the first pack that covers the key owns it (<see cref="GetCreaturePackFor" />,
///     <see cref="GetAmbientPackFor" />), and an entry of that pack that does not decode shows the placeholder or gives
///     no bed. When only the caller can tell whether an entry works, or must keep a second decision with the pack
///     that answered, the caller goes through the ordered <c>…Packs</c> sequence itself: audio, which SDL decodes, and
///     items, whose dye the pack that gives the image decides (<c>ItemIconSource</c>). A UI theme is the fourth kind:
///     themes are alternatives, not layers, so one theme is active (<see cref="GetUiThemePack" />) and a part that it
///     does not have takes the flat look, never the part of another theme.
///     </para>
///     <para>
///     Adding a new pack type is one factory entry in <see cref="Factories" /> plus its typed accessors.
///     </para>
/// </summary>
public static class AssetPackRegistry
{
    private const int SUPPORTED_SCHEMA_VERSION = 1;
    private const string MANIFEST_ENTRY_NAME = "_manifest.json";

    //the pre-Erisco pack location, relative to the retail data path. Packs found here are migrated once into
    //AppPaths.AssetsDir on first launch after the move, smoothing existing manual installs.
    private const string LEGACY_PACK_SUBFOLDER = "hybrasyl-data";

    //sentinel dropped in the assets dir after the one-and-only legacy-migration attempt. Gates migration on
    //"has it ever run" instead of "is the dir empty", so (a) a launcher-placed pack of one type no longer blocks
    //migrating legacy packs of other types, and (b) clearing the assets dir to force a launcher re-fetch never
    //resurrects stale legacy packs. Not a .datf, so pack discovery ignores it.
    private const string MIGRATION_MARKER_NAME = ".legacy-migrated";

    //the SearchOption overload matches with MatchCasing.PlatformDefault, which is case-sensitive on Linux and so
    //missed a pack named Wood.DATF there. The other options keep that overload's behaviour.
    private static readonly EnumerationOptions PackFileSearch = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        AttributesToSkip = 0,
        IgnoreInaccessible = false
    };

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
        ["ambient_sounds"]      = static (a, m) => new AmbientPack(a, m),
        ["world_maps"]          = static (a, m) => new WorldMapPack(a, m),
        ["town_maps"]           = static (a, m) => new TownMapPack(a, m),
        ["ui_theme"]            = static (a, m) => new UiThemePack(a, m)
    };

    //the number of shared keys that an overlap warning names
    private const int OVERLAP_KEYS_SHOWN = 5;

    //registered packs keyed by manifest.content_type: the highest priority first, then in registration order. a
    //lookup tries them in this order, so the first pack that covers a key wins
    private static readonly Dictionary<string, List<IAssetPack>> Packs = new();
    private static bool Initialized;

    //the pack_id of the theme that the player selected, or null for the first theme. The active theme is a field, as
    //ThemeArt asks for it on each draw
    private static string? SelectedUiThemeId;
    private static UiThemePack? ActiveUiTheme;

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
        var registeredBefore = RegisteredCount;

        foreach (var path in InRegistrationOrder(EnumeratePackFiles(packDir)))
        {
            discovered++;
            TryRegisterPack(path);
        }

        if (discovered == 0)
        {
            LogInfo($"asset-pack folder '{packDir}' contains no .datf files");

            return;
        }

        LogInfo($"registered {RegisteredCount - registeredBefore} asset pack(s) from '{packDir}'");
    }

    /// <summary>
    ///     One-time, best-effort copy of legacy <c>{DataPath}/hybrasyl-data/*.datf</c> packs into
    ///     <paramref name="assetsDir" />, run at most once ever (gated by <see cref="MIGRATION_MARKER_NAME" />). A
    ///     per-file <c>File.Exists</c> skip means launcher-placed content is never clobbered, so the whole set can
    ///     migrate even when a pack of some other type already lives here. Packs are normally managed by the launcher;
    ///     this only smooths existing manual installs. Never throws.
    /// </summary>
    private static void TryMigrateLegacyPacks(string assetsDir)
    {
        try
        {
            if (string.IsNullOrEmpty(DataContext.DataPath))
                return;

            var marker = Path.Combine(assetsDir, MIGRATION_MARKER_NAME);

            //already attempted once — never run again, even if the assets dir was later cleared
            if (File.Exists(marker))
                return;

            var legacyDir = Path.Combine(DataContext.DataPath, LEGACY_PACK_SUBFOLDER);

            if (Directory.Exists(legacyDir))
            {
                Directory.CreateDirectory(assetsDir);

                var migrated = 0;

                foreach (var src in EnumeratePackFiles(legacyDir))
                {
                    var dst = Path.Combine(assetsDir, Path.GetFileName(src));

                    if (File.Exists(dst))
                        continue;

                    try
                    {
                        //copy to a temp sibling then move into place, so an interrupted copy never leaves a
                        //truncated .datf that would fail ZipFile.OpenRead on every subsequent launch. One locked
                        //or unreadable source skips just that pack instead of aborting the rest of the set.
                        var tmp = dst + ".tmp";
                        File.Copy(src, tmp, true);
                        File.Move(tmp, dst, true);
                        migrated++;
                    } catch
                    {
                        //skip this pack — a locked/unreadable source shouldn't block the others
                    }
                }

                if (migrated > 0)
                    LogInfo($"migrated {migrated} legacy asset pack(s) from '{legacyDir}' to '{assetsDir}'");
            }

            //drop the marker whether or not anything was found, so we don't re-scan the legacy dir every launch
            TryWriteMigrationMarker(marker);
        } catch
        {
            //best effort — a failed migration just means nothing migrated; the launcher can re-fetch packs
        }
    }

    private static void TryWriteMigrationMarker(string markerPath)
    {
        try
        {
            File.WriteAllText(markerPath, string.Empty);
        } catch
        {
            //non-fatal — worst case migration is re-attempted next launch, and per-file skips keep that idempotent
        }
    }

    /// <summary>The <c>ability_icons</c> packs, in lookup order.</summary>
    public static IEnumerable<IconPack> IconPacks => PacksOf<IconPack>("ability_icons");

    /// <summary>The <c>nation_badges</c> packs, in lookup order.</summary>
    public static IEnumerable<NationBadgePack> NationBadgePacks => PacksOf<NationBadgePack>("nation_badges");

    /// <summary>The <c>item_icons</c> packs, in lookup order.</summary>
    public static IEnumerable<ItemPack> ItemPacks => PacksOf<ItemPack>("item_icons");

    /// <summary>The <c>npc_portraits</c> packs, in lookup order.</summary>
    public static IEnumerable<NpcPortraitPack> NpcPortraitPacks => PacksOf<NpcPortraitPack>("npc_portraits");

    /// <summary>The <c>static_tiles</c> packs, in lookup order.</summary>
    public static IEnumerable<StaticTilePack> StaticTilePacks => PacksOf<StaticTilePack>("static_tiles");

    /// <summary>The <c>legend_mark_icons</c> packs, in lookup order.</summary>
    public static IEnumerable<LegendMarkIconPack> LegendMarkIconPacks => PacksOf<LegendMarkIconPack>("legend_mark_icons");

    /// <summary>The <c>ui_sprite_overrides</c> packs, in lookup order.</summary>
    public static IEnumerable<UiSpriteOverridePack> UiSpriteOverridePacks => PacksOf<UiSpriteOverridePack>("ui_sprite_overrides");

    /// <summary>The <c>creature_sprites</c> packs, in lookup order.</summary>
    public static IEnumerable<CreaturePack> CreaturePacks => PacksOf<CreaturePack>("creature_sprites");

    /// <summary>The <c>music</c> packs, in lookup order.</summary>
    public static IEnumerable<MusicPack> MusicPacks => PacksOf<MusicPack>("music");

    /// <summary>The <c>sound_effects</c> packs, in lookup order.</summary>
    public static IEnumerable<SfxPack> SfxPacks => PacksOf<SfxPack>("sound_effects");

    /// <summary>The <c>ambient_sounds</c> packs, in lookup order. A bed comes from <see cref="GetAmbientPackFor" />.</summary>
    public static IEnumerable<AmbientPack> AmbientPacks => PacksOf<AmbientPack>("ambient_sounds");

    /// <summary>The <c>world_maps</c> packs, in lookup order.</summary>
    public static IEnumerable<WorldMapPack> WorldMapPacks => PacksOf<WorldMapPack>("world_maps");

    /// <summary>The <c>town_maps</c> packs, in lookup order.</summary>
    public static IEnumerable<TownMapPack> TownMapPacks => PacksOf<TownMapPack>("town_maps");

    /// <summary>The installed <c>ui_theme</c> packs, in priority order. Only one is active; see <see cref="GetUiThemePack" />.</summary>
    public static IEnumerable<UiThemePack> UiThemePacks => PacksOf<UiThemePack>("ui_theme");

    /// <summary>True when at least one <c>static_tiles</c> pack is registered. Lets the map preload skip its pack pass.</summary>
    public static bool HasStaticTilePacks => Packs.TryGetValue("static_tiles", out var list) && (list.Count > 0);

    /// <summary>The first <c>ability_icons</c> pack that decodes <c>{prefix}{id:D4}.png</c>. The caller owns the image.</summary>
    public static bool TryGetIconImage(string prefix, int spriteId, out SKImage? image)
        => TryFirstImage("ability_icons", (prefix, spriteId), static (IconPack pack, (string Prefix, int Id) key, out SKImage? img) => pack.TryGetIconImage(key.Prefix, key.Id, out img), out image);

    /// <summary>The first <c>nation_badges</c> pack that decodes the badge. The caller owns the image.</summary>
    public static bool TryGetBadgeImage(byte nationId, out SKImage? image)
        => TryFirstImage("nation_badges", nationId, static (NationBadgePack pack, byte key, out SKImage? img) => pack.TryGetBadgeImage(key, out img), out image);

    /// <summary>The first <c>npc_portraits</c> pack that gives a valid illustration for the key. The caller owns the image.</summary>
    public static bool TryGetNpcIllustration(string portraitKey, out SKImage? image)
        => TryFirstImage("npc_portraits", portraitKey, static (NpcPortraitPack pack, string key, out SKImage? img) => pack.TryGetIllustration(key, out img), out image);

    /// <summary>The first <c>static_tiles</c> pack that decodes the floor tile. The caller owns the image.</summary>
    public static bool TryGetFloorImage(int tileId, out SKImage? image)
        => TryFirstImage("static_tiles", tileId, static (StaticTilePack pack, int key, out SKImage? img) => pack.TryGetFloorImage(key, out img), out image);

    /// <summary>The first <c>static_tiles</c> pack that decodes the wall tile. The caller owns the image.</summary>
    public static bool TryGetWallImage(int tileId, out SKImage? image)
        => TryFirstImage("static_tiles", tileId, static (StaticTilePack pack, int key, out SKImage? img) => pack.TryGetWallImage(key, out img), out image);

    /// <summary>The first <c>legend_mark_icons</c> pack that decodes the icon. The caller owns the image.</summary>
    public static bool TryGetLegendMarkImage(byte iconId, out SKImage? image)
        => TryFirstImage("legend_mark_icons", iconId, static (LegendMarkIconPack pack, byte key, out SKImage? img) => pack.TryGetLegendMarkImage(key, out img), out image);

    /// <summary>
    ///     The first <c>ui_sprite_overrides</c> pack that decodes the frame. The key is the file name with its extension
    ///     and the frame, so one file can take frames from two packs. The caller owns the image.
    /// </summary>
    public static bool TryGetUiSprite(string fileName, int frameIndex, out SKImage? image)
        => TryFirstImage("ui_sprite_overrides", (fileName, frameIndex), static (UiSpriteOverridePack pack, (string File, int Frame) key, out SKImage? img) => pack.TryGetSprite(key.File, key.Frame, out img), out image);

    /// <summary>The first <c>world_maps</c> pack that decodes the field background. The caller owns the image.</summary>
    public static bool TryGetFieldImage(string fieldName, out SKImage? image)
        => TryFirstImage("world_maps", fieldName, static (WorldMapPack pack, string key, out SKImage? img) => pack.TryGetFieldImage(key, out img), out image);

    /// <summary>The first <c>town_maps</c> pack that decodes the town map. The caller owns the image.</summary>
    public static bool TryGetTownMapImage(short mapId, out SKImage? image)
        => TryFirstImage("town_maps", mapId, static (TownMapPack pack, short key, out SKImage? img) => pack.TryGetTownMapImage(key, out img), out image);

    /// <summary>
    ///     The first <c>ambient_sounds</c> pack that has a bed for <paramref name="ambientId" />, or null. That pack owns
    ///     the bed: it gives the sound and the loop flag, so the two never come from different packs. An entry of that
    ///     pack that does not decode gives no bed, as an ambient bed has no legacy fallback.
    /// </summary>
    public static AmbientPack? GetAmbientPackFor(int ambientId)
    {
        if (!Packs.TryGetValue("ambient_sounds", out var list))
            return null;

        foreach (var pack in list)
            if (pack is AmbientPack ambient && ambient.Covers(ambientId))
                return ambient;

        return null;
    }

    /// <summary>
    ///     The first <c>creature_sprites</c> pack that covers <paramref name="spriteId" />, or null. That pack owns the
    ///     creature: it gives the frames, the pair count and the bounding box, so two packs never mix in one creature.
    /// </summary>
    public static CreaturePack? GetCreaturePackFor(int spriteId)
    {
        if (!Packs.TryGetValue("creature_sprites", out var list))
            return null;

        foreach (var pack in list)
            if (pack is CreaturePack creatures && creatures.Covers(spriteId))
                return creatures;

        return null;
    }

    /// <summary>
    ///     Returns the active UI theme: the installed pack whose <c>pack_id</c> the player selected with
    ///     <see cref="SelectUiTheme" />, else the first pack in <see cref="UiThemePacks" />, or null when no theme is
    ///     installed.
    /// </summary>
    public static UiThemePack? GetUiThemePack() => ActiveUiTheme;

    /// <summary>
    ///     Selects the active UI theme by its <c>pack_id</c> (case ignored). Null or blank selects the first installed
    ///     theme, and so does an id that no installed theme has. Call it after <see cref="Initialize" />: a selection made
    ///     before its pack registers takes effect when the pack registers, but it first logs that the theme is not installed.
    /// </summary>
    public static void SelectUiTheme(string? packId)
    {
        SelectedUiThemeId = string.IsNullOrWhiteSpace(packId) ? null : packId;
        UpdateActiveUiTheme();

        if (SelectedUiThemeId is not null && !string.Equals(ActiveUiTheme?.Manifest.PackId, SelectedUiThemeId, StringComparison.OrdinalIgnoreCase))
            LogWarning($"the selected ui_theme '{SelectedUiThemeId}' is not installed; using {(ActiveUiTheme is null ? "the flat look" : $"'{ActiveUiTheme.Manifest.PackId}'")}");
    }

    /// <summary>
    ///     Makes <paramref name="pack" /> the active theme without registering it, or makes no theme active when it is
    ///     null. For the tests of the themed controls, which build a pack in memory and do not test the registry. The next
    ///     registration or selection replaces it.
    /// </summary>
    internal static void SetUiThemeForTests(UiThemePack? pack) => ActiveUiTheme = pack;

    private static void UpdateActiveUiTheme()
        => ActiveUiTheme = UiThemePacks.FirstOrDefault(pack => string.Equals(pack.Manifest.PackId, SelectedUiThemeId, StringComparison.OrdinalIgnoreCase))
                           ?? UiThemePacks.FirstOrDefault();

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

            if (GetManifestRejection(manifest) is { } rejection)
            {
                LogWarning($"pack {Path.GetFileName(path)} {rejection}; skipping");
                archive.Dispose();

                return;
            }

            if (GetDuplicateRejection(manifest) is { } duplicate)
            {
                LogWarning($"pack {Path.GetFileName(path)} {duplicate}; skipping");
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

    /// <summary>The <c>*.datf</c> files directly in <paramref name="dir" />, whatever the case of the extension.</summary>
    internal static IEnumerable<string> EnumeratePackFiles(string dir) => Directory.EnumerateFiles(dir, "*.datf", PackFileSearch);

    /// <summary>
    ///     Returns why the registry refuses a manifest, or null when it accepts it. The registry accepts schema versions 1
    ///     through <see cref="SUPPORTED_SCHEMA_VERSION" />. It refuses an absent, zero or negative <c>schema_version</c>
    ///     and an absent <c>pack_id</c>, as Creidhne and Taliesin also do. It also refuses a blank <c>pack_id</c>, which
    ///     the two editors accept.
    /// </summary>
    internal static string? GetManifestRejection(AssetPackManifest manifest)
    {
        if (manifest.SchemaVersion is not { } version)
            return "has no schema_version";

        if (version < 1)
            return $"declares schema_version={version} which is not a valid version";

        if (version > SUPPORTED_SCHEMA_VERSION)
            return $"declares schema_version={version} which is newer than supported ({SUPPORTED_SCHEMA_VERSION})";

        if (string.IsNullOrWhiteSpace(manifest.PackId))
            return "has no pack_id";

        return null;
    }

    /// <summary>
    ///     Single consolidated registration path. Looks up a factory for the declared content type, warns about the keys
    ///     that the new pack shares with the packs already registered for that type, and puts the new pack in the order:
    ///     after every pack with the same or a higher priority, so a higher priority wins a shared key, and equal
    ///     priorities keep the registration order.
    /// </summary>
    private static bool TryRegisterByContentType(ZipArchive archive, AssetPackManifest manifest)
    {
        if (!Factories.TryGetValue(manifest.ContentType, out var factory))
            return false;

        var pack = factory(archive, manifest);

        if (!Packs.TryGetValue(manifest.ContentType, out var list))
        {
            list = [];
            Packs[manifest.ContentType] = list;
        }

        var index = list.FindIndex(other => other.Manifest.Priority < manifest.Priority);

        if (index < 0)
            index = list.Count;

        for (var i = 0; i < list.Count; i++)
        {
            var other = list[i];
            var shared = SharedKeys(other, pack);

            if (shared.Count == 0)
                continue;

            //registration cannot tell which entries decode, so the warning gives the order, not a winner for each key
            var (first, second) = i < index ? (other.Manifest, manifest) : (manifest, other.Manifest);
            var reason = first.Priority == second.Priority
                ? "it registered first, at the same priority"
                : $"its priority {first.Priority} is higher than {second.Priority}";

            LogWarning($"{manifest.ContentType} packs '{other.Manifest.PackId}' and '{manifest.PackId}' share {shared.Count} key(s) "
                       + $"({string.Join(", ", shared.Take(OVERLAP_KEYS_SHOWN))}{(shared.Count > OVERLAP_KEYS_SHOWN ? ", ..." : string.Empty)}); "
                       + $"'{first.PackId}' is asked first, before '{second.PackId}': {reason}. "
                       + $"An entry of '{first.PackId}' that does not decode passes the lookup to '{second.PackId}'");
        }

        list.Insert(index, pack);

        if (pack is UiThemePack)
            UpdateActiveUiTheme();

        return true;
    }

    /// <summary>
    ///     The keys that both packs cover, sorted. A key is what a lookup asks for: an entry name for most types, the
    ///     id for an audio pack (so <c>sfx_1.wav</c> and <c>sfx_0001.mp3</c> are the same key). Case is ignored.
    /// </summary>
    internal static List<string> SharedKeys(IAssetPack first, IAssetPack second)
    {
        var keys = new HashSet<string>(first.CoverageKeys, StringComparer.OrdinalIgnoreCase);
        var shared = second.CoverageKeys.Where(keys.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        shared.Sort(StringComparer.OrdinalIgnoreCase);

        return shared;
    }

    /// <summary>The pack files in registration order: by file name, ordinal, case ignored.</summary>
    internal static IEnumerable<string> InRegistrationOrder(IEnumerable<string> paths)
        => paths.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ThenBy(Path.GetFileName, StringComparer.Ordinal);

    /// <summary>Registers an archive as if it came from a file. For tests only.</summary>
    internal static bool RegisterForTest(ZipArchive archive, AssetPackManifest manifest)
    {
        if (GetDuplicateRejection(manifest) is { } duplicate)
        {
            LogWarning($"pack '{manifest.PackId}' {duplicate}; skipping");
            archive.Dispose();

            return false;
        }

        return TryRegisterByContentType(archive, manifest);
    }

    /// <summary>
    ///     Returns why the registry refuses a pack whose <c>pack_id</c> a registered pack already has (case ignored, across
    ///     every content type), or null. The pack registered first, in file-name order, stays, also when the second pack
    ///     has a higher priority: two packs with one id are usually two copies of one pack, and the warning tells the
    ///     player to remove one. A <c>ui_theme</c> pack is selected by its id, so two packs with one id would make the
    ///     selection depend on the file names.
    /// </summary>
    private static string? GetDuplicateRejection(AssetPackManifest manifest)
    {
        foreach (var list in Packs.Values)
            foreach (var pack in list)
                if (string.Equals(pack.Manifest.PackId, manifest.PackId, StringComparison.OrdinalIgnoreCase))
                    return $"has pack_id '{manifest.PackId}', which the registered {pack.Manifest.ContentType} pack '{pack.Manifest.PackId}' already has";

        return null;
    }

    /// <summary>
    ///     Disposes every pack and empties the registry, so that a test starts clean. For tests only. It marks the
    ///     registry as initialized, so that nothing scans the real assets folder during a test.
    /// </summary>
    internal static void ResetForTest()
    {
        foreach (var list in Packs.Values)
            foreach (var pack in list)
                pack.Dispose();

        Packs.Clear();
        SelectedUiThemeId = null;
        ActiveUiTheme = null;
        Initialized = true;
    }

    /// <summary>Each warning line that the registry logs. For tests, which must not share the global log seam.</summary>
    internal static event Action<string>? WarningLogged;

    private static int RegisteredCount => Packs.Values.Sum(list => list.Count);

    private static IEnumerable<T> PacksOf<T>(string contentType) where T : class, IAssetPack
        => Packs.TryGetValue(contentType, out var list) ? list.OfType<T>() : [];

    private delegate bool ImageLookup<in TPack, in TKey>(TPack pack, TKey key, out SKImage? image);

    //asks each pack of the type in order. a pack that has no entry, or whose entry does not decode, gives false, and
    //the loop asks the next pack. the lookups are static lambdas with the key as an argument, so a call allocates
    //nothing: some callers run every frame
    private static bool TryFirstImage<TPack, TKey>(string contentType, TKey key, ImageLookup<TPack, TKey> lookup, out SKImage? image)
        where TPack : class, IAssetPack
    {
        if (Packs.TryGetValue(contentType, out var list))
            foreach (var pack in list)
                if (pack is TPack typed && lookup(typed, key, out image) && (image is not null))
                    return true;

        image = null;

        return false;
    }

    //through the data-layer log seam, so that the lines reach notice-debug.log and not only stderr
    private static void LogWarning(string message)
    {
        var line = $"[asset-pack] warning: {message}";
        DataDiagnostics.Log?.Invoke(line);
        WarningLogged?.Invoke(line);
    }

    private static void LogInfo(string message) => DataDiagnostics.Log?.Invoke($"[asset-pack] {message}");
}
