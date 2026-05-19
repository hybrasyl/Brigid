#region
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Chaos.Client.Data;
using Chaos.Client.Networking;
using Chaos.Client.Rendering;
using Chaos.Client.Systems;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client;

/// <summary>
///     Static configuration for the client: version, data path, lobby host/port, and sampler state. Triggers all one-time
///     initialization (encoding providers, data archives, text colors) via the static constructor.
/// </summary>
public static class GlobalSettings
{
    private static readonly string[] PreLoadedAssemblies = ["Chaos.Networking"];
    private static readonly Type[] PreInitializedStatics = [typeof(DataContext), typeof(MachineIdentity)];
    public static readonly SamplerState Sampler = SamplerState.PointClamp; //SamplerState.LinearClamp;
    private static ushort ClientVersion => 741;

    public static string DataPath { get; set; } = Environment.GetEnvironmentVariable("DA_ASSET_PATH") ?? 
                                                  Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));

    public static string LobbyHost
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("DA_HOST");
            return string.IsNullOrWhiteSpace(env) ? "da0.kru.com" : env;
        }
    }

    public static int LobbyPort
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("DA_HOST_PORT");
            if (int.TryParse(env, out var port) && port is >= 1 and <= 65535)
                return port;
            return 2610;
        }
    }

    /// <summary>
    ///     When true, walking onto a water tile requires either the GM flag or the "Swimming" skill.
    ///     When false (default), any character can swim freely and pathfinding routes through water tiles.
    /// </summary>
    public static bool RequireSwimmingSkill => false;

    static GlobalSettings() => InitializeOthers();

    private static void InitializeOthers()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var hostEnv = Environment.GetEnvironmentVariable("DA_HOST");
        var portEnv = Environment.GetEnvironmentVariable("DA_HOST_PORT");
        NoticeDebugLog.Write(
            $"lobby target {LobbyHost}:{LobbyPort} "
            + $"(DA_HOST={hostEnv ?? "(unset)"}, DA_HOST_PORT={portEnv ?? "(unset)"})");

        DataContext.Initialize(
            ClientVersion,
            DataPath,
            LobbyHost,
            LobbyPort);

        if (DataContext.AislingDrawData.DyeColorTable.Contains(0))
            ItemDyePass.VerifyAgainstLegacy(DataContext.AislingDrawData.DyeColorTable[0].Colors);

        LegendColors.Initialize();
        TextColors.Initialize();

        foreach (var name in PreLoadedAssemblies)
            Assembly.Load(name);

        foreach (var type in PreInitializedStatics)
            RuntimeHelpers.RunClassConstructor(type.TypeHandle);
    }
}