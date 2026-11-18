using Xunit;

namespace Brigid.SmokeTests;

/// <summary>
///     The settings of a smoke run, from environment variables (BRIG-184):
///     <list type="bullet">
///         <item><c>BRIGID_SMOKE=1</c> turns the smoke tests on.</item>
///         <item><c>BRIGID_SMOKE_DATA</c> is the Dark Ages data folder (the <c>*.dat</c> files). Required.</item>
///         <item><c>BRIGID_SMOKE_PACKS</c> is an optional folder of <c>*.datf</c> packs, copied into each client's assets.</item>
///         <item>
///             <c>BRIGID_SMOKE_SERVER</c> is the <c>server</c> checkout, whose <c>docker-compose.yml</c> the run starts
///             (<see cref="SmokeServer" />). The specs that need a server skip without it.
///         </item>
///     </list>
///     A smoke run is for a developer machine and a local server. It never connects to retail or production.
/// </summary>
internal static class SmokeEnvironment
{
    internal static string? DataPath => Environment.GetEnvironmentVariable("BRIGID_SMOKE_DATA");

    internal static string? PacksPath => Value("BRIGID_SMOKE_PACKS");

    internal static string? ServerPath => Value("BRIGID_SMOKE_SERVER");

    /// <summary>Null when the specs that need a server can run, else why they skip.</summary>
    internal static string? ServerSkipReason
    {
        get
        {
            if (SkipReason is { } reason)
                return reason;

            if (ServerPath is not { } server || !File.Exists(Path.Combine(server, "docker-compose.yml")))
                return "set BRIGID_SMOKE_SERVER to the server checkout (the folder with docker-compose.yml) to run the specs that need a server";

            return null;
        }
    }

    /// <summary>Null when the smoke tests can run, else why they skip.</summary>
    internal static string? SkipReason
    {
        get
        {
            if (Value("BRIGID_SMOKE") != "1")
                return "set BRIGID_SMOKE=1 to run the smoke tests";

            if (DataPath is not { } data || !Directory.Exists(data) || !Directory.EnumerateFiles(data, "*.dat").Any())
                return "set BRIGID_SMOKE_DATA to a Dark Ages data folder (the folder with the *.dat files)";

            return null;
        }
    }

    /// <summary>The built client (<c>Brigid.dll</c>) and test driver, from <c>smoke-paths.txt</c>.</summary>
    internal static (string Client, string Driver) BuildPaths()
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "smoke-paths.txt"));
        var paths = lines.Select(line => line.Split('=', 2)).Where(parts => parts.Length == 2).ToDictionary(parts => parts[0], parts => parts[1]);

        return (paths["client"], paths["driver"]);
    }

    private static string? Value(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value.Trim() : null;
}

/// <summary>A smoke test: it skips unless <see cref="SmokeEnvironment.SkipReason" /> is null.</summary>
public sealed class SmokeFactAttribute : FactAttribute
{
    public SmokeFactAttribute()
    {
        if (SmokeEnvironment.SkipReason is { } reason)
            Skip = reason;
    }
}

/// <summary>A smoke test that needs the server: it skips unless <see cref="SmokeEnvironment.ServerSkipReason" /> is null.</summary>
public sealed class ServerSmokeFactAttribute : FactAttribute
{
    public ServerSmokeFactAttribute()
    {
        if (SmokeEnvironment.ServerSkipReason is { } reason)
            Skip = reason;
    }
}

// The smoke tests start one client at a time, against one server. The server starts when the first test that needs it
// asks, and stops after the last test of the run.
[CollectionDefinition("Smoke", DisableParallelization = true)]
public sealed class SmokeCollection : ICollectionFixture<SmokeServer>;
