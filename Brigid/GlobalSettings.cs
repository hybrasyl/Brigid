#region
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Brigid.Data;
using Brigid.Networking;
using Brigid.Systems;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid;

/// <summary>
///     Static configuration for the client: version, data path, lobby host/port, and sampler state. The static
///     constructor performs the one-time initialization that does NOT require the asset path (encoding providers, text
///     colors, machine identity) and resolves the host/port/asset-path from environment variables and the saved
///     <see cref="LauncherConfig" />. Asset-path-dependent initialization (the data archives and repositories) is
///     deferred to <see cref="InitializeAssetData" />, which the client calls once a usable asset path is known —
///     either from config/env at startup, or from the launcher screen.
/// </summary>
public static class GlobalSettings
{
    private static readonly string[] PreLoadedAssemblies = ["DALib"];
    private static readonly Type[] PreInitializedStatics = [typeof(DataContext), typeof(MachineIdentity)];
    public static readonly SamplerState Sampler = SamplerState.PointClamp; //SamplerState.LinearClamp;
    private static ushort ClientVersion => 741;

    /// <summary>
    ///     The Dark Ages asset directory (the folder containing the <c>.dat</c> archives). Empty until resolved from
    ///     env/config or chosen in the launcher screen.
    /// </summary>
    public static string DataPath { get; set; } = "";

    /// <summary>Lobby server host. Empty until resolved from env/config or the launcher screen.</summary>
    public static string LobbyHost { get; set; } = "";

    /// <summary>GitHub API endpoint queried by the startup release check (<see cref="UpdateChecker" />).</summary>
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/hybrasyl/Brigid/releases/latest";

    /// <summary>Fallback releases page for the update notice when the API response carries no html_url.</summary>
    public const string ReleasesUrl = "https://github.com/hybrasyl/Brigid/releases";

    /// <summary>Lobby server port. Defaults to 2610.</summary>
    public static int LobbyPort { get; set; } = 2610;

    /// <summary>
    ///     True when the connected lobby host is Kru's retail Dark Ages (e.g. <c>da0.kru.com</c>). Kru's login notice uses a
    ///     legacy fixed-width, TAB-delimited layout with literal "." spacer lines that must be reflowed for display; Hybrasyl
    ///     (and other modern hosts) send clean newline-delimited notices that should be shown verbatim. Used to gate the
    ///     retail-only notice normalization.
    /// </summary>
    public static bool IsCursed => ServerProfileKey.IsRetail(LobbyHost);

    /// <summary>
    ///     True when the launcher screen should be shown at startup (the normal case). False when the client
    ///     auto-connects instead: either environment variables fully specify a valid host + asset path (CI / scripted),
    ///     or a bypass is requested — <c>DA_NO_LAUNCHER</c> (Epona) or the saved
    ///     <see cref="LauncherConfig.SuppressLauncher" /> flag — and a valid asset path resolves from env or saved
    ///     config. See <see cref="ResolveConnectionConfig" />.
    /// </summary>
    public static bool ShowLauncher { get; private set; }

    /// <summary>
    ///     A sensible default to pre-fill the asset-path field with: the directory above the executable, where the game
    ///     data typically sits relative to the client binary.
    /// </summary>
    public static string DefaultAssetPathGuess => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));

    /// <summary>
    ///     When true, walking onto a water tile requires either the GM flag or the "Swimming" skill.
    ///     When false (default), any character can swim freely and pathfinding routes through water tiles.
    /// </summary>
    public static bool RequireSwimmingSkill => false;

    static GlobalSettings() => InitializeCore();

    private static void InitializeCore()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        ResolveConnectionConfig();

        foreach (var name in PreLoadedAssemblies)
            Assembly.Load(name);

        foreach (var type in PreInitializedStatics)
            RuntimeHelpers.RunClassConstructor(type.TypeHandle);
    }

    /// <summary>
    ///     Loads the saved launcher config and decides whether to show the launcher screen. It is shown on every normal
    ///     launch and skipped (auto-connect) in two cases: (1) environment variables fully specify a valid host + asset
    ///     path (CI / scripted); or (2) a bypass is requested via <c>DA_NO_LAUNCHER</c> (Epona) or the saved
    ///     <see cref="LauncherConfig.SuppressLauncher" /> flag — resolving host from env, then the saved server, then
    ///     <see cref="LauncherConfig.DEFAULT_HOST" />, and asset from env then saved config — but only if a valid asset
    ///     dir resolves; otherwise the launcher is shown so the user can fix it. Seeds <see cref="DataPath" /> for
    ///     prefill/validation; host/port are finalized from env, saved config, or the launcher's selection at connect time.
    /// </summary>
    private static void ResolveConnectionConfig()
    {
        LauncherConfig.Load();

        var envHost = Environment.GetEnvironmentVariable("DA_HOST");
        var envPort = Environment.GetEnvironmentVariable("DA_HOST_PORT");
        var envPath = Environment.GetEnvironmentVariable("DA_ASSET_PATH");
        var envNoLauncher = IsTruthy(Environment.GetEnvironmentVariable("DA_NO_LAUNCHER"));

        var envHostSet = !string.IsNullOrWhiteSpace(envHost);
        var envPathValid = DataContext.IsValidDataDirectory(envPath);

        string reason;

        if (envHostSet && envPathValid)
        {
            //full env spec — CI / scripted auto-connect
            LobbyHost = envHost!;
            LobbyPort = ParsePort(envPort);
            DataPath = envPath!;
            ShowLauncher = false;
            reason = "env";
        } else if (envNoLauncher || LauncherConfig.SuppressLauncher)
        {
            //bypass requested — Epona's DA_NO_LAUNCHER, or the user's saved SuppressLauncher flag. Resolve the asset
            //dir and host from env first, then saved config. Only skip the launcher if a valid asset dir resolves;
            //otherwise fall back to the (now-working) launcher so the user can fix the path rather than boot blind.
            var assetPath = envPathValid
                ? envPath!
                : DataContext.IsValidDataDirectory(LauncherConfig.AssetPath) ? LauncherConfig.AssetPath! : null;

            if (assetPath is not null)
            {
                var server = LauncherConfig.GetSelectedServer();
                LobbyHost = envHostSet ? envHost! : (server?.Host ?? LauncherConfig.DEFAULT_HOST);
                LobbyPort = envHostSet ? ParsePort(envPort) : (server?.Port ?? LauncherConfig.DEFAULT_PORT);
                DataPath = assetPath;
                ShowLauncher = false;
                reason = envNoLauncher ? "env-no-launcher" : "config-suppress";
            } else
            {
                DataPath = FirstNonBlank(envPath, LauncherConfig.AssetPath) ?? "";
                ShowLauncher = true;
                reason = "bypass-requested-no-valid-asset";
            }
        } else
        {
            DataPath = FirstNonBlank(envPath, LauncherConfig.AssetPath) ?? "";
            ShowLauncher = true;
            reason = "default";
        }

        NoticeDebugLog.Write(
            $"connection config resolved: showLauncher={ShowLauncher} ({reason}), "
            + $"host={(string.IsNullOrEmpty(LobbyHost) ? "(unset)" : LobbyHost)}:{LobbyPort}, "
            + $"assetPath={(string.IsNullOrEmpty(DataPath) ? "(unset)" : DataPath)} "
            + $"(DA_HOST={envHost ?? "(unset)"}, DA_HOST_PORT={envPort ?? "(unset)"}, DA_ASSET_PATH={envPath ?? "(unset)"}, "
            + $"DA_NO_LAUNCHER={(envNoLauncher ? "1" : "(unset)")}, SuppressLauncher={LauncherConfig.SuppressLauncher})");
    }

    /// <summary>
    ///     Loads the data archives and repositories for the resolved <see cref="DataPath" />, then initializes the
    ///     asset-derived color tables (<see cref="LegendColors" /> reads <c>legend.pal</c> via <c>LegendPalette</c>, so it
    ///     must run after the data context). Must be called exactly once, after a usable asset path is known. Separated
    ///     from the static constructor so the launcher screen can run (and render) before any asset is loaded.
    /// </summary>
    public static void InitializeAssetData()
    {
        DataContext.Initialize(ClientVersion, DataPath, LobbyHost, LobbyPort);

        LegendColors.Initialize();
        TextColors.Initialize();
    }

    private static string? FirstNonBlank(string? primary, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
            return primary;

        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }

    private static int ParsePort(string? value)
        => int.TryParse(value, out var p) && p is >= 1 and <= 65535 ? p : LauncherConfig.DEFAULT_PORT;

    private static bool IsTruthy(string? value)
    {
        var v = value?.Trim();

        return !string.IsNullOrEmpty(v)
               && (v.Equals("1", StringComparison.Ordinal)
                   || v.Equals("true", StringComparison.OrdinalIgnoreCase)
                   || v.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }
}
