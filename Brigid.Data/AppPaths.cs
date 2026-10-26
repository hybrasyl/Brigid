namespace Brigid.Data;

/// <summary>
///     Resolves the per-user, writable application-data locations Brigid owns, following the house standard shared by
///     the Erisco toolset: <c>%LOCALAPPDATA%\Erisco\&lt;appname&gt;</c> on Windows and <c>~/.config/Erisco/&lt;appname&gt;</c>
///     on macOS/Linux. These are deliberately <em>separate</em> from the retail Dark Ages <c>.dat</c> install
///     (<see cref="DataContext.DataPath" />): they hold Brigid-owned content (modern <c>.datf</c> asset packs) and the
///     launcher config, neither of which should be coupled to whichever game-data folder the user points at.
/// </summary>
/// <remarks>
///     Lives in <c>Brigid.Data</c> (the lowest layer) so both the data layer (asset-pack discovery) and the client
///     layer (launcher config) can resolve the same roots without a reverse dependency.
/// </remarks>
public static class AppPaths
{
    //the case is part of the standard: every Electron sibling writes "Erisco", and Linux keeps "erisco" a separate folder
    private const string VENDOR = "Erisco";
    private const string APP_NAME = "Brigid";

    //the lower-case vendor folder Brigid wrote before it matched the standard; read only to migrate from
    private const string LEGACY_VENDOR = "erisco";

    /// <summary>
    ///     <c>%LOCALAPPDATA%\Erisco</c> on Windows; <c>~/.config/Erisco</c> on macOS/Linux. The platform split is
    ///     deliberate: .NET maps <see cref="Environment.SpecialFolder.LocalApplicationData" /> to <c>~/.local/share</c>
    ///     on Unix, but the standard targets <c>~/.config</c>, which is <see cref="Environment.SpecialFolder.ApplicationData" />
    ///     there. On Windows that same enum is roaming <c>%APPDATA%</c>, so we use <c>LocalApplicationData</c> instead.
    /// </summary>
    public static string VendorRoot => Path.Combine(VendorBaseDirectory, VENDOR);

    private static string VendorBaseDirectory
    {
        get
        {
            var special = OperatingSystem.IsWindows()
                ? Environment.SpecialFolder.LocalApplicationData
                : Environment.SpecialFolder.ApplicationData;

            var baseDir = Environment.GetFolderPath(special);

            //GetFolderPath returns "" when the special folder is undefined (stripped-env / service contexts, or
            //Unix with HOME unset). Path.Combine("", VENDOR) would yield a CWD-relative "Erisco" that scatters
            //config and packs across whatever directory the process launched from; anchor to the install dir instead.
            if (string.IsNullOrEmpty(baseDir))
                baseDir = AppContext.BaseDirectory;

            return baseDir;
        }
    }

    /// <summary>
    ///     Moves the user's data from <c>erisco/Brigid</c> to <c>Erisco/Brigid</c> on a file system that keeps the two
    ///     names apart (Linux), so an existing user keeps their config, profiles, keybinds and packs. Call it once at
    ///     startup, before anything reads <see cref="AppRoot" />. Writes the result to <see cref="DataDiagnostics.Log" />.
    /// </summary>
    public static void MigrateLegacyVendorFolder()
    {
        var result = MigrateLegacyVendorFolder(VendorBaseDirectory);

        if (result.Moved.Count > 0)
            DataDiagnostics.Log?.Invoke($"[paths] moved {string.Join(", ", result.Moved)} from {result.From} to {result.To}");

        if (result.LeftInPlace.Count > 0)
            DataDiagnostics.Log?.Invoke(
                $"[paths] left {string.Join(", ", result.LeftInPlace)} in {result.From}, because {result.To} already has them or the move failed");
    }

    /// <summary>
    ///     Moves each entry of <c>{baseDir}/erisco/Brigid</c> that <c>{baseDir}/Erisco/Brigid</c> does not already have,
    ///     then removes the old folders when they are empty. An entry that the new folder already has stays where it is.
    ///     The move is per entry because elatha installs the binaries in <c>Erisco/Brigid/app</c>, so the new folder can
    ///     exist before the user's data moves.
    /// </summary>
    internal static VendorFolderMigration MigrateLegacyVendorFolder(string baseDir)
    {
        var legacyVendor = Path.Combine(baseDir, LEGACY_VENDOR);
        var legacyApp = Path.Combine(legacyVendor, APP_NAME);
        var app = Path.Combine(baseDir, VENDOR, APP_NAME);
        var nothing = new VendorFolderMigration(legacyApp, app, [], []);

        //no lower-case folder on disk
        if (!HasDirectoryNamedExactly(baseDir, LEGACY_VENDOR))
            return nothing;

        //a file system that ignores case: the two names are one folder
        if (!HasDirectoryNamedExactly(baseDir, VENDOR) && Directory.Exists(Path.Combine(baseDir, VENDOR)))
            return nothing;

        string[] entries = [];

        if (!Directory.Exists(legacyApp)
            || !DataDiagnostics.Try(() => entries = Directory.GetFileSystemEntries(legacyApp), "list the legacy Brigid folder")
            || !DataDiagnostics.Try(() => Directory.CreateDirectory(app), "create the Brigid folder"))
            return nothing;

        var moved = new List<string>();
        var leftInPlace = new List<string>();

        foreach (var source in entries.Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(source);
            var target = Path.Combine(app, name);

            if (!Path.Exists(target) && DataDiagnostics.Try(() => MoveEntry(source, target), $"move {name}"))
                moved.Add(name);
            else
                leftInPlace.Add(name);
        }

        RemoveIfEmpty(legacyApp);
        RemoveIfEmpty(legacyVendor);

        return new VendorFolderMigration(legacyApp, app, moved, leftInPlace);
    }

    private static void MoveEntry(string source, string target)
    {
        if (Directory.Exists(source))
            Directory.Move(source, target);
        else
            File.Move(source, target);
    }

    private static void RemoveIfEmpty(string directory)
        => DataDiagnostics.Try(
            () =>
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            },
            $"remove the empty folder {directory}");

    private static bool HasDirectoryNamedExactly(string parent, string name)
    {
        var found = false;

        DataDiagnostics.Try(
            () => found = Directory.Exists(parent)
                          && Directory.EnumerateDirectories(parent)
                                      .Any(path => string.Equals(Path.GetFileName(path), name, StringComparison.Ordinal)),
            $"list {parent}");

        return found;
    }

    /// <summary><c>{VendorRoot}/Brigid</c> — the root of all per-user state Brigid writes.</summary>
    public static string AppRoot => Path.Combine(VendorRoot, APP_NAME);

    /// <summary><c>{AppRoot}/assets</c> — where modern <c>.datf</c> asset packs are discovered.</summary>
    public static string AssetsDir => Path.Combine(AppRoot, "assets");

    /// <summary><c>{AppRoot}/config.json</c> — the launcher configuration file.</summary>
    public static string ConfigFile => Path.Combine(AppRoot, "config.json");

    /// <summary>
    ///     <c>{AppRoot}/brigid.cfg</c> — global client settings (volume, font, option toggles), formerly written as
    ///     <c>Darkages.cfg</c> inside the game-data folder. Client-wide, not per-server/character.
    /// </summary>
    public static string SettingsFile => Path.Combine(AppRoot, "brigid.cfg");

    /// <summary>
    ///     <c>{AppRoot}/profiles</c> — root of per-server, per-character config. Individual character config lives under
    ///     <c>{ProfilesDir}/{serverKey}/{character}</c> (see <see cref="ProfileDir" />), keyed by server so the same
    ///     character name on different servers no longer collides.
    /// </summary>
    public static string ProfilesDir => Path.Combine(AppRoot, "profiles");

    /// <summary><c>{AppRoot}/screenshots</c> — captured screenshots (<c>lod*</c> on retail, <c>hyb*</c> otherwise).</summary>
    public static string ScreenshotsDir => Path.Combine(AppRoot, "screenshots");

    /// <summary>
    ///     Key scoping the server-pushed caches (metafiles and maps), set when the connection target is known via
    ///     <see cref="SetServerScope" />, and again when Config picks another server. Defaults to a neutral bucket so a
    ///     path read before that never lands at the cache root.
    /// </summary>
    private static string ServerScope = "default";

    /// <summary>
    ///     Scopes the server-pushed caches to the server being connected to. Metafiles are server-specific content
    ///     shipped under identical filenames — retail and Hybrasyl both send <c>SClass1</c>, <c>ItemInfo0</c>, … with
    ///     different contents — so a shared cache lets one server's catalog answer for another's until the next
    ///     checksum sync overwrites it. Routed through <see cref="ServerProfileKey" />, the single home for
    ///     "which server is this".
    /// </summary>
    public static void SetServerScope(string? host, string? serverName = null)
        => ServerScope = ServerProfileKey.Resolve(host, serverName);

    /// <summary>
    ///     <c>{AppRoot}/metafile/{serverKey}</c> — cache of server-sent metafiles (disposable). <see cref="MetaFileSync" />
    ///     writes a file only when the client requested it and its CRC-32 matches the checksum in the data packet.
    /// </summary>
    public static string MetaFileDir => Path.Combine(AppRoot, "metafile", ServerScope);

    /// <summary>
    ///     <c>{AppRoot}/maps/{serverKey}</c> — cache of downloaded map files (disposable). Scoped per server, because
    ///     two servers can send different maps under one map number (BRIG-105).
    /// </summary>
    public static string MapsDir => Path.Combine(AppRoot, "maps", ServerScope);

    /// <summary>The per-character config directory for a server profile: <c>{ProfilesDir}/{serverKey}/{character}</c>.</summary>
    public static string ProfileDir(string serverKey, string character) => Path.Combine(ProfilesDir, serverKey, character);
}

/// <summary>The result of <see cref="AppPaths.MigrateLegacyVendorFolder(string)" />.</summary>
internal sealed record VendorFolderMigration(string From, string To, IReadOnlyList<string> Moved, IReadOnlyList<string> LeftInPlace);
