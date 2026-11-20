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
///     A smoke run is for a developer machine and a local server. It never connects to retail or production. With
///     <c>BRIGID_SMOKE=1</c> set, a data folder or a server checkout that is not there fails the tests, so a script that
///     reads only the exit code sees it; only a server checkout that is not set skips.
/// </summary>
internal static class SmokeEnvironment
{
    internal static string? DataPath => Environment.GetEnvironmentVariable("BRIGID_SMOKE_DATA");

    internal static string? PacksPath => Value(Environment.GetEnvironmentVariable, "BRIGID_SMOKE_PACKS");

    internal static string? ServerPath => Value(Environment.GetEnvironmentVariable, "BRIGID_SMOKE_SERVER");

    /// <summary>Null when the smoke tests run, else why they skip: <c>BRIGID_SMOKE</c> is not 1.</summary>
    internal static string? SkipReason => SkipReasonFor(Environment.GetEnvironmentVariable);

    /// <summary>Null when the specs that need a server run, else why they skip.</summary>
    internal static string? ServerSkipReason => ServerSkipReasonFor(Environment.GetEnvironmentVariable);

    /// <summary>Null when the data folder holds the <c>*.dat</c> files, else what is wrong. A smoke test fails on it.</summary>
    internal static string? DataError => DataErrorFor(Environment.GetEnvironmentVariable);

    /// <summary>Null when <c>BRIGID_SMOKE_SERVER</c> is not set or is a server checkout, else what is wrong. A server test fails on it.</summary>
    internal static string? ServerError => ServerErrorFor(Environment.GetEnvironmentVariable);

    internal static string? SkipReasonFor(Func<string, string?> environment)
        => Value(environment, "BRIGID_SMOKE") != "1" ? "set BRIGID_SMOKE=1 to run the smoke tests" : null;

    internal static string? ServerSkipReasonFor(Func<string, string?> environment)
        => SkipReasonFor(environment)
           ?? (Value(environment, "BRIGID_SMOKE_SERVER") is null
               ? "set BRIGID_SMOKE_SERVER to the server checkout (the folder with docker-compose.yml) to run the specs that need a server"
               : null);

    //the client's own rule (DataContext.IsValidDataDirectory): khanpal.dat and legend.dat, in any case. A folder with other
    //archives passed here, and the client then refused it at the start
    internal static string? DataErrorFor(Func<string, string?> environment)
        => environment("BRIGID_SMOKE_DATA") is not { } data || !Directory.Exists(data) || !HasArchive(data, "khanpal.dat") || !HasArchive(data, "legend.dat")
            ? "BRIGID_SMOKE=1 is set, but BRIGID_SMOKE_DATA is not a Dark Ages data folder (the folder with khanpal.dat and legend.dat)"
            : null;

    private static bool HasArchive(string folder, string name)
        => Directory.EnumerateFiles(folder).Any(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase));

    internal static string? ServerErrorFor(Func<string, string?> environment)
        => Value(environment, "BRIGID_SMOKE_SERVER") is { } server && !File.Exists(Path.Combine(server, "docker-compose.yml"))
            ? $"BRIGID_SMOKE_SERVER is not a server checkout (it has no docker-compose.yml): {server}"
            : null;

    /// <summary>The built client (<c>Brigid.dll</c>) and test driver, from <c>smoke-paths.txt</c>.</summary>
    internal static (string Client, string Driver) BuildPaths()
    {
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "smoke-paths.txt"));
        var paths = lines.Select(line => line.Split('=', 2)).Where(parts => parts.Length == 2).ToDictionary(parts => parts[0], parts => parts[1]);

        return (paths["client"], paths["driver"]);
    }

    private static string? Value(Func<string, string?> environment, string name) => environment(name) is { Length: > 0 } value ? value.Trim() : null;
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
