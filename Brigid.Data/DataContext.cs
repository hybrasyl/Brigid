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

    /// <summary>
    ///     The lobby server hostname or IP address.
    /// </summary>
    public static string LobbyHost { get; private set; } = null!;

    /// <summary>
    ///     TCP port for the lobby server connection.
    /// </summary>
    public static int LobbyPort { get; private set; }

    public static LocalPlayerSettingsRepository LocalPlayerSettings { get; private set; } = null!;

    public static MapFileRepository MapsFiles { get; private set; } = null!;
    public static MessageTable Messages { get; private set; } = null!;
    public static MetaFileRepository MetaFiles { get; private set; } = null!;
    public static PanelSpriteRepository PanelSprites { get; private set; } = null!;
    public static TileRepository Tiles { get; private set; } = null!;
    public static UiComponentRepository UserControls { get; private set; } = null!;

    public static void Initialize(
        ushort clientVersion,
        string dataPath,
        string lobbyHost,
        int lobbyPort)
    {
        ClientVersion = clientVersion;
        DataPath = dataPath;
        LobbyHost = lobbyHost;
        LobbyPort = lobbyPort;

        LegendPalette.Initialize();

        //scan for .datf asset packs before building repositories so renderer paths can query the registry at first use
        AssetPackRegistry.Initialize();

        AislingDrawData = new AislingDrawDataRepository();
        CreatureSprites = new CreatureSpriteRepository();
        Effects = new EffectsRepository();
        LightMasks = new LightMaskRepository();
        LocalPlayerSettings = new LocalPlayerSettingsRepository();
        MapsFiles = new MapFileRepository();
        Messages = MessageTable.Load();
        MetaFiles = new MetaFileRepository();
        PanelSprites = new PanelSpriteRepository();
        Tiles = new TileRepository();
        UserControls = new UiComponentRepository();
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

        var present = Directory.EnumerateFiles(path)
                               .Select(Path.GetFileName)
                               .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return present.Contains("khanpal.dat") && present.Contains("legend.dat");
    }
}