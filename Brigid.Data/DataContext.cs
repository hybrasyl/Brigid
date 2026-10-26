#region
using Brigid.Data.AssetPacks;
using Brigid.Data.Repositories;
#endregion

namespace Brigid.Data;

public static class DataContext
{
    public static AislingDrawDataRepository AislingDrawData { get; private set; } = null!;

    /// <summary>
    ///     The Dark Ages protocol version this client identifies as.
    /// </summary>
    public static ushort ClientVersion { get; private set; }

    public static CreatureSpriteRepository CreatureSprites { get; private set; } = null!;

    /// <summary>
    ///     The root path to the Dark Ages data installation directory.
    /// </summary>
    public static string DataPath { get; private set; } = null!;

    public static EffectsRepository Effects { get; private set; } = null!;

    public static LightMaskRepository LightMasks { get; private set; } = null!;

    public static LocalPlayerSettingsRepository LocalPlayerSettings { get; private set; } = null!;

    public static MessageTable Messages { get; private set; } = null!;
    public static MetaFileRepository MetaFiles { get; private set; } = null!;
    public static PanelSpriteRepository PanelSprites { get; private set; } = null!;
    public static TileRepository Tiles { get; private set; } = null!;
    public static UiComponentRepository UserControls { get; private set; } = null!;

    public static void Initialize(ushort clientVersion, string dataPath)
    {
        ClientVersion = clientVersion;
        DataPath = dataPath;

        LegendPalette.Initialize();

        //scan for .datf asset packs before building repositories so renderer paths can query the registry at first use
        AssetPackRegistry.Initialize();

        AislingDrawData = new AislingDrawDataRepository();
        CreatureSprites = new CreatureSpriteRepository();
        Effects = new EffectsRepository();
        LightMasks = new LightMaskRepository();
        LocalPlayerSettings = new LocalPlayerSettingsRepository();
        Messages = MessageTable.Load();
        MetaFiles = new MetaFileRepository();
        PanelSprites = new PanelSpriteRepository();
        Tiles = new TileRepository();
        UserControls = new UiComponentRepository();

        CheckItemDyeRamp();
    }

    //the item dye pass finds the hardcoded ramp. if the game's color0.tbl entry 0 has other colors, a pack icon would
    //not be found dyeable and would not be dyed, so say so in the log. only an item pack needs the check
    private static void CheckItemDyeRamp()
    {
        if (!AssetPackRegistry.ItemPacks.Any() || !AislingDrawData.DyeColorTable.Contains(0))
            return;

        foreach (var difference in ItemDyeRamp.DifferencesFrom(AislingDrawData.DyeColorTable[0].Colors))
            DataDiagnostics.Log?.Invoke($"[asset-pack] warning: {difference}");
    }

    /// <summary>
    ///     True when <paramref name="path" /> is a usable Dark Ages data directory: it exists and contains the core
    ///     archives the client always loads (<c>khanpal.dat</c> for palettes, <c>legend.dat</c>). Matched
    ///     case-insensitively because data directories vary in filename casing across platforms. Used to validate a
    ///     candidate asset path before <see cref="Initialize" /> loads from it.
    /// </summary>
    public static bool IsValidDataDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        return HasFile(path, "khanpal.dat") && HasFile(path, "legend.dat");

        static bool HasFile(string directory, string name)
            => DirectoryEntries.FindName(directory, name, StringComparison.OrdinalIgnoreCase, DirectoryEntryKind.Files)
               is not null;
    }
}