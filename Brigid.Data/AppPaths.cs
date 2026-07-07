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
}
