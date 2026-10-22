using Brigid.Data;

namespace Brigid.Systems;

/// <summary>
///     Reads and writes the global client settings file (brigid.cfg) in the original DarkAges line-delimited
///     "Key : Value" format. Stored in the per-user app root (<see cref="AppPaths.SettingsFile" />); on first run,
///     settings are imported from the legacy <c>Darkages.cfg</c> in the game-data folder when the new file is absent.
///     Client-wide preferences only (volume, font, UI theme, option toggles) — not per-server/character.
/// </summary>
public static class ClientSettings
{
    private const string LEGACY_FILE_NAME = "Darkages.cfg";
    public static bool UseGroupWindow { get; set; } = true;
    public static int ChattingMode { get; set; }
    public static bool DoGroundAnimation { get; set; } = true;
    public static bool EnableProfileClick { get; set; } = true;

    //index into FontEngine's selectable UI faces; persisted in the legacy "EngFont" slot.
    public static int FontIndex { get; set; }

    //one-time decision on importing legacy per-character config from the old in-game-folder location.
    public const int MigrationUndecided = -1;
    public const int MigrationSkip = 0;
    public const int MigrationImport = 1;
    public static int LegacyCharacterImport { get; set; } = MigrationUndecided;

    //the pack_id of the selected ui_theme pack; null selects the first installed theme. Not a legacy key.
    public static string? UiTheme { get; set; }
    public static bool GroupOpen { get; set; }
    public static int MusicVolume { get; set; } = 5;
    public static int AmbienceVolume { get; set; } = 5;
    public static bool RecordNpcChat { get; set; } = true;
    public static int ScrollLevel { get; set; }

    //defaults match the original client
    public static int SoundVolume { get; set; } = 5;
    public static int Speed { get; set; } = 100;
    public static bool UseShiftKeyForAltPanels { get; set; } = true;

    private static string FilePath => AppPaths.SettingsFile;

    //legacy in-game-folder location, read once on first run when the new file doesn't exist yet
    private static string? LegacyFilePath =>
        string.IsNullOrEmpty(GlobalSettings.DataPath) ? null : Path.Combine(GlobalSettings.DataPath, LEGACY_FILE_NAME);

    /// <summary>
    ///     Replaces the saved settings with the choices made in the launcher, and saves. Call it after <see cref="Load" />,
    ///     which would otherwise replace them.
    /// </summary>
    public static void Apply(LauncherChoices choices)
    {
        UiTheme = choices.UiTheme;
        Save();
    }

    /// <summary>
    ///     The <c>UiTheme</c> value saved in <c>brigid.cfg</c>, read without <see cref="Load" />: the launcher shows it
    ///     before the settings load. Null when the file, the line or the value is absent, or the file cannot be read.
    /// </summary>
    public static string? ReadSavedUiTheme() => ReadSavedUiTheme(FilePath);

    /// <summary>The <c>UiTheme</c> value saved in the settings file at <paramref name="path" />. Internal for the tests.</summary>
    internal static string? ReadSavedUiTheme(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            foreach (var line in File.ReadLines(path))
                if (TrySplit(line, out var key, out var value) && (key == "UiTheme"))
                    return value.Length > 0 ? value : null;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            //Load reads the file again when the assets load, and uses its defaults if it still cannot
        }

        return null;
    }

    //a "Key: Value" line, both trimmed
    private static bool TrySplit(string line, out string key, out string value)
    {
        var colonIndex = line.IndexOf(':');

        if (colonIndex < 0)
        {
            key = value = string.Empty;

            return false;
        }

        key = line[..colonIndex].Trim();
        value = line[(colonIndex + 1)..].Trim();

        return true;
    }

    /// <summary>
    ///     Loads settings into static properties. Reads the per-user <c>brigid.cfg</c>; on first run (before it exists)
    ///     falls back to the legacy <c>Darkages.cfg</c> and migrates it to the new location. Uses defaults if neither
    ///     file exists or the file is corrupt.
    /// </summary>
    public static void Load()
    {
        var loadPath = File.Exists(FilePath) ? FilePath
            : LegacyFilePath is { } legacy && File.Exists(legacy) ? legacy
            : null;

        if (loadPath is null)
            return;

        try
        {
            foreach (var line in File.ReadLines(loadPath))
            {
                if (!TrySplit(line, out var key, out var value))
                    continue;

                switch (key)
                {
                    case "Sound Volume":
                        if (int.TryParse(value, out var sv))
                            SoundVolume = Math.Clamp(sv, 0, 10);

                        break;

                    case "Music Volume":
                        if (int.TryParse(value, out var mv))
                            MusicVolume = Math.Clamp(mv, 0, 10);

                        break;

                    case "Ambience Volume":
                        if (int.TryParse(value, out var av))
                            AmbienceVolume = Math.Clamp(av, 0, 10);

                        break;

                    case "doGroundAnimation":
                        DoGroundAnimation = value == "1";

                        break;

                    case "SkillSpellSelectByToggle":
                        UseShiftKeyForAltPanels = value != "1";

                        break;

                    case "GroupAnswer":
                        GroupOpen = value == "1";

                        break;

                    case "ScrollLevel":
                        if (int.TryParse(value, out var sl))
                            ScrollLevel = sl;

                        break;

                    case "UserClickMode":
                        EnableProfileClick = value != "1";

                        break;

                    case "MonsterSayRecordMode":
                        RecordNpcChat = value == "1";

                        break;

                    case "GroupObjectOption":
                        UseGroupWindow = value == "1";

                        break;

                    case "Chatting Mode":
                        if (int.TryParse(value, out var cm))
                            ChattingMode = cm;

                        break;

                    case "Speed":
                        if (int.TryParse(value, out var spd))
                            Speed = spd;

                        break;

                    case "EngFont":
                        if (int.TryParse(value, out var ef) && ef >= 0)
                            FontIndex = ef;

                        break;

                    case "ImportCharacterData":
                        if (int.TryParse(value, out var ci))
                            LegacyCharacterImport = ci;

                        break;

                    case "UiTheme":
                        UiTheme = value.Length > 0 ? value : null;

                        break;
                }
            }
        } catch
        {
            //corrupted file — use whatever defaults/partial state was already set
        }

        //first-run migration: if we just read the legacy file, persist to the new per-user location
        if (loadPath != FilePath)
            Save();
    }

    /// <summary>
    ///     Saves the current settings to <c>brigid.cfg</c> (per-user app root) in the original format. Best-effort —
    ///     never throws on a write failure.
    /// </summary>
    public static void Save() => DataDiagnostics.Try(
        () =>
        {
            Directory.CreateDirectory(AppPaths.AppRoot);

            using var writer = new StreamWriter(FilePath, false);
            writer.WriteLine("Version: 9728");
            writer.WriteLine("Port: 5");
            writer.WriteLine($"Speed: {Speed}");
            writer.WriteLine("KeyBoard: 0");
            writer.WriteLine("Tel: 1");
            writer.WriteLine("HanFont: 0");
            writer.WriteLine($"EngFont: {FontIndex}");
            writer.WriteLine($"ImportCharacterData: {LegacyCharacterImport}");
            writer.WriteLine($"UiTheme: {UiTheme}");
            writer.WriteLine("Tel1: \"Nexus\",\"1\"");
            writer.WriteLine("Tel2: \"Nexus\",\"2\"");
            writer.WriteLine("Tel3: \"Nexus\",\"3\"");
            writer.WriteLine("Tel4: \"Nexus\",\"4\"");
            writer.WriteLine($"Chatting Mode : {ChattingMode}");
            writer.WriteLine($"doGroundAnimation : {(DoGroundAnimation ? 1 : 0)}");
            writer.WriteLine($"Sound Volume : {SoundVolume}");
            writer.WriteLine($"Music Volume : {MusicVolume}");
            writer.WriteLine($"Ambience Volume : {AmbienceVolume}");
            writer.WriteLine($"SkillSpellSelectByToggle : {(UseShiftKeyForAltPanels ? 0 : 1)}");
            writer.WriteLine($"GroupAnswer : {(GroupOpen ? 1 : 0)}");
            writer.WriteLine($"ScrollLevel : {ScrollLevel}");
            writer.WriteLine($"UserClickMode : {(EnableProfileClick ? 0 : 1)}");
            writer.WriteLine($"MonsterSayRecordMode : {(RecordNpcChat ? 1 : 0)}");
            writer.WriteLine($"GroupObjectOption : {(UseGroupWindow ? 1 : 0)}");
        }, "ClientSettings.Save");
}