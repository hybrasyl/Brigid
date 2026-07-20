namespace Brigid.Data;

/// <summary>
///     Resolves the per-user, writable application-data locations Brigid owns, following the house standard shared by
///     the Erisco toolset: <c>%LOCALAPPDATA%\erisco\&lt;appname&gt;</c> on Windows and <c>~/.config/erisco/&lt;appname&gt;</c>
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
    private const string VENDOR = "erisco";
    private const string APP_NAME = "Brigid";

    /// <summary>
    ///     <c>%LOCALAPPDATA%\erisco</c> on Windows; <c>~/.config/erisco</c> on macOS/Linux. The platform split is
    ///     deliberate: .NET maps <see cref="Environment.SpecialFolder.LocalApplicationData" /> to <c>~/.local/share</c>
    ///     on Unix, but the standard targets <c>~/.config</c>, which is <see cref="Environment.SpecialFolder.ApplicationData" />
    ///     there. On Windows that same enum is roaming <c>%APPDATA%</c>, so we use <c>LocalApplicationData</c> instead.
    /// </summary>
    public static string VendorRoot
    {
        get
        {
            var special = OperatingSystem.IsWindows()
                ? Environment.SpecialFolder.LocalApplicationData
                : Environment.SpecialFolder.ApplicationData;

            var baseDir = Environment.GetFolderPath(special);

            //GetFolderPath returns "" when the special folder is undefined (stripped-env / service contexts, or
            //Unix with HOME unset). Path.Combine("", VENDOR) would yield a CWD-relative "erisco" that scatters
            //config and packs across whatever directory the process launched from; anchor to the install dir instead.
            if (string.IsNullOrEmpty(baseDir))
                baseDir = AppContext.BaseDirectory;

            return Path.Combine(baseDir, VENDOR);
        }
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
    ///     Key scoping the server-pushed caches, set once the connection target is known via
    ///     <see cref="SetServerScope" />. Defaults to a neutral bucket so a path read before that never lands at the
    ///     cache root.
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
    ///     <c>{AppRoot}/metafile/{serverKey}</c> — cache of server-sent metafiles (checksum-validated, disposable).
    /// </summary>
    public static string MetaFileDir => Path.Combine(AppRoot, "metafile", ServerScope);

    /// <summary><c>{AppRoot}/maps</c> — cache of downloaded map files (disposable).</summary>
    public static string MapsDir => Path.Combine(AppRoot, "maps");

    /// <summary>The per-character config directory for a server profile: <c>{ProfilesDir}/{serverKey}/{character}</c>.</summary>
    public static string ProfileDir(string serverKey, string character) => Path.Combine(ProfilesDir, serverKey, character);
}
