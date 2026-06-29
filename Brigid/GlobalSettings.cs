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

    /// <summary>Lobby server port. Defaults to 2610.</summary>
    public static int LobbyPort { get; set; } = 2610;

    /// <summary>
    ///     True when the launcher screen should be shown at startup (the normal case). False only when environment
    ///     variables fully specify a valid host + asset path, in which case the client auto-connects without the launcher
    ///     (for CI / scripted launches).
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
    ///     Loads the saved launcher config and decides whether to show the launcher screen. The launcher is shown on every
    ///     normal launch; it is skipped (auto-connect) only when environment variables fully specify a valid host + asset
    ///     path, so CI / scripted launches can run non-interactively. Seeds <see cref="DataPath" /> from env/config so the
    ///     launcher can prefill and validate it; the host/port are finalized either from env (auto-connect) or from the
    ///     launcher's server selection at connect time.
    /// </summary>
    private static void ResolveConnectionConfig()
    {
        LauncherConfig.Load();

        var envHost = Environment.GetEnvironmentVariable("DA_HOST");
        var envPort = Environment.GetEnvironmentVariable("DA_HOST_PORT");
        var envPath = Environment.GetEnvironmentVariable("DA_ASSET_PATH");

        var envHostSet = !string.IsNullOrWhiteSpace(envHost);
        var envPathValid = DataContext.IsValidDataDirectory(envPath);

        if (envHostSet && envPathValid)
        {
            LobbyHost = envHost!;
            LobbyPort = int.TryParse(envPort, out var p) && p is >= 1 and <= 65535 ? p : LauncherConfig.DEFAULT_PORT;
            DataPath = envPath!;
            ShowLauncher = false;
        } else
        {
            DataPath = FirstNonBlank(envPath, LauncherConfig.AssetPath) ?? "";
            ShowLauncher = true;
        }

        NoticeDebugLog.Write(
            $"connection config resolved: showLauncher={ShowLauncher}, "
            + $"host={(string.IsNullOrEmpty(LobbyHost) ? "(unset)" : LobbyHost)}:{LobbyPort}, "
            + $"assetPath={(string.IsNullOrEmpty(DataPath) ? "(unset)" : DataPath)} "
            + $"(DA_HOST={envHost ?? "(unset)"}, DA_HOST_PORT={envPort ?? "(unset)"}, DA_ASSET_PATH={envPath ?? "(unset)"})");
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
}
