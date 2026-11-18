#region
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Xunit;
#endregion

namespace Brigid.SmokeTests;

/// <summary>
///     The local Hybrasyl server of a smoke run (BRIG-184 decision 3): the <c>server</c> checkout's
///     <c>docker-compose.yml</c> (the published image, Valkey and a <c>ceridwen</c> clone), started under a compose
///     project of its own, so that its volumes, network and containers never touch a developer's own stack. Each run
///     pulls the published image, starts with new volumes (no characters), checks out the pinned <c>ceridwen</c> commit,
///     and removes everything at the end. The server reads the run's own <c>config.xml</c> (<see cref="ConfigXml" />),
///     not the sample in the checkout. It answers at <see cref="HOST" />, ports 2610 to 2612: the server sends its login
///     and world ports to the client, so the ports cannot move, and a server that already answers on 2610 stops the run
///     with a clear message.
/// </summary>
public sealed class SmokeServer : IAsyncLifetime
{
    /// <summary>The address of the smoke server, and the lobby port. The login and world ports follow it.</summary>
    public const string HOST = "127.0.0.1";

    public const int PORT = 2610;

    /// <summary>The <c>ceridwen</c> commit that every smoke run uses, so that a change upstream cannot break a run.</summary>
    public const string CERIDWEN_COMMIT = "da4ee212a54dca55da34fe85af41a8ea0d8773a5";

    /// <summary>
    ///     The one start room of the run, so that every new character starts at the same place: ceridwen's first start
    ///     room, map 137, at a tile whose four neighbours are open and run no reactor (ceridwen's own start tile, 2, 3, is
    ///     a wall, and 3, 4 opens the startup dialog).
    /// </summary>
    public const string START_MAP = "Mileth Inn Room 1";

    public const int START_MAP_ID = 137;

    public const int START_X = 4;

    public const int START_Y = 3;

    private const string SERVICE = "hybrasyl-server";
    private const string PROJECT_PREFIX = "brigid-smoke-";
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     The server configuration of a smoke run: the sample in the <c>server</c> checkout (<c>contrib/config.xml</c>)
    ///     with one start room, and with no admin gRPC endpoint, which a run does not use and which needs certificates. The
    ///     environment of the compose file gives the Redis host.
    /// </summary>
    private static readonly string ConfigXml = $$"""
        <?xml version="1.0"?>
        <ServerConfig xmlns="http://www.hybrasyl.com/XML/Hybrasyl/2020-02">
          <Logging/>
          <WorldDataDir>/world</WorldDataDir>
          <DataStore Host="valkey"/>
          <Motd>Brigid smoke test server.</Motd>
          <Network>
            <Lobby BindAddress="0.0.0.0" Port="{{PORT}}" ExternalAddress="{{HOST}}"/>
            <Login BindAddress="0.0.0.0" Port="{{PORT + 1}}" ExternalAddress="{{HOST}}"/>
            <World BindAddress="0.0.0.0" Port="{{PORT + 2}}" ExternalAddress="{{HOST}}"/>
          </Network>
          <Access>
            <Reserved>Danaan Chadul Deoch Glioca Cail Luathas Gramail Fiosachd Ceannlaidir Sgrios Belenus</Reserved>
          </Access>
          <Boards>
            <Board Name="board" DisplayName="My First Board">
              <AccessList>
                <Read>*</Read>
                <Write>*</Write>
              </AccessList>
            </Board>
          </Boards>
          <Time>
            <Ages>
              <Age Name="Danaan" StartDate="1640-01-03T01:00:00Z"/>
            </Ages>
            <ServerStart>2021-11-18T04:00:00Z</ServerStart>
          </Time>
          <Handlers>
            <Death Active="true" Perishable="true" GroupNotify="true">
              <Map X="14" Y="6">Chaotic Threshold</Map>
              <Coma Timeout="30" Effect="24">Coma</Coma>
              <Penalty Xp="0.02" Hp="0.01"/>
              <LegendMark Increment="true">Sgrios' Scar</LegendMark>
            </Death>
            <NewPlayer>
              <StartMaps>
                <StartMap X="{{START_X}}" Y="{{START_Y}}">{{START_MAP}}</StartMap>
              </StartMaps>
            </NewPlayer>
          </Handlers>
          <ClientSettings>
            <Setting Number="1" Default="true" Key="whisper">Listen to whisper</Setting>
            <Setting Number="2" Default="true" Key="group">Join a group     </Setting>
            <Setting Number="3" Default="true" Key="shout">Listen to shouts </Setting>
            <Setting Number="4" Default="true" Key="wisdom">Believe in wisdom</Setting>
            <Setting Number="5" Default="true" Key="magic">Believe in magic </Setting>
            <Setting Number="6" Default="true" Key="exchange">Exchange         </Setting>
            <Setting Number="7" Default="true" Key="groupwindow">Use group window    </Setting>
            <Setting Number="8" Default="true" Key="guildwhisper">Guild whispers   </Setting>
          </ClientSettings>
        </ServerConfig>
        """;

    private readonly SemaphoreSlim StartLock = new(1, 1);
    private readonly string Project = PROJECT_PREFIX + Guid.NewGuid().ToString("N")[..8];
    private string? OverrideFile;
    private string? ConfigFile;

    //the first start; a start that failed fails every later test with the same error, not with a timeout of its own
    private Task? FirstStart;

    //true while a test has the server stopped (spec 8); the next test starts it again
    private bool ServiceStopped;

    /// <summary>
    ///     Starts the server the first time a test asks for it, and waits until the lobby sends its greeting. A server that
    ///     a test stopped (<see cref="StopServiceAsync" />) starts again here. The first run pulls the images, so it can
    ///     take some minutes. The fixture does not probe the lobby between tests: the server does not answer a connection
    ///     that comes right after the last one.
    /// </summary>
    public async Task EnsureStartedAsync()
    {
        await StartLock.WaitAsync();

        try
        {
            FirstStart ??= StartAsync();
            await FirstStart;

            if (ServiceStopped)
                await StartServiceAsync();
        } finally
        {
            StartLock.Release();
        }
    }

    private async Task StartAsync()
    {
        await RunDockerAsync(["version", "--format", "{{.Server.Version}}"], "Docker does not answer; start Docker Desktop");
        await RemoveStaleStacksAsync();

        if (await GreetsAsync())
            throw new InvalidOperationException(
                $"a server already answers on port {PORT}; stop it (a developer's own server or compose stack) before a smoke run");

        ConfigFile = Path.Combine(Path.GetTempPath(), $"{Project}.config.xml");
        await File.WriteAllTextAsync(ConfigFile, ConfigXml);
        OverrideFile = Path.Combine(Path.GetTempPath(), $"{Project}.override.yml");
        await File.WriteAllTextAsync(OverrideFile, OverrideYaml(ConfigFile));

        await RunComposeAsync(["up", "--detach", "--no-build", "--pull", "always", "--quiet-pull"], StartTimeout);
        await WaitForGreetingAsync();
    }

    /// <summary>Stops the server container and keeps its data, for a test of a server that goes away (spec 8).</summary>
    public async Task StopServiceAsync()
    {
        await RunComposeAsync(["stop", SERVICE], CommandTimeout);
        ServiceStopped = true;
    }

    /// <summary>
    ///     Starts the stopped server container again. <c>--no-deps</c>, because <c>compose start</c> would run the one-shot
    ///     world services again, and they fail on a world that is already there.
    /// </summary>
    public async Task StartServiceAsync()
    {
        await RunComposeAsync(["up", "--detach", "--no-deps", "--no-build", SERVICE], CommandTimeout);
        await WaitForGreetingAsync();
        ServiceStopped = false;
    }

    /// <summary>The server's log so far, for a failed test, or the reason it could not be read.</summary>
    public async Task<string> LogsAsync()
    {
        if (OverrideFile is null)
            return string.Empty;

        try
        {
            return await RunComposeAsync(["logs", "--no-color", "--tail", "400", SERVICE], CommandTimeout);
        } catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            return $"the server log could not be read: {ex.Message}";
        }
    }

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    /// <summary>Removes the run's containers, network and volumes, and its two files.</summary>
    async Task IAsyncLifetime.DisposeAsync()
    {
        if (OverrideFile is not null)
            await RunComposeAsync(["down", "--volumes", "--remove-orphans"], CommandTimeout);

        foreach (var file in new[] { OverrideFile, ConfigFile })
            if (file is not null && File.Exists(file))
                File.Delete(file);
    }

    //the changes to the compose file for a smoke run:
    //- the "hybrasyl" network belongs to the project, not to the machine, so a developer's own Valkey cannot answer to
    //  the name "valkey" and down removes the network
    //- no fixed container name and no build, so a developer's own stack and checkout are not touched; only the game
    //  ports, because the admin gRPC endpoint is off
    //- a one-shot service that checks out the pinned ceridwen commit after the base clone; the server waits for it
    //- the run's own config.xml in place of the checkout's sample
    private static string OverrideYaml(string configFile)
        => $$"""
             services:
               world-pin:
                 image: alpine:latest
                 command: sh -c "apk add --no-cache git >/dev/null && cd /world && git -c advice.detachedHead=false checkout -q {{CERIDWEN_COMMIT}}"
                 volumes:
                   - world:/world
                 depends_on:
                   world-data:
                     condition: service_completed_successfully
               hybrasyl-server:
                 container_name: !reset null
                 build: !reset null
                 ports: !override
                   - "{{PORT}}:{{PORT}}"
                   - "{{PORT + 1}}:{{PORT + 1}}"
                   - "{{PORT + 2}}:{{PORT + 2}}"
                 volumes:
                   - type: bind
                     source: "{{configFile.Replace('\\', '/')}}"
                     target: /config/config.xml
                     read_only: true
                 depends_on:
                   world-pin:
                     condition: service_completed_successfully
             networks:
               hybrasyl:
                 external: false
             """;

    //a run that was killed (a cancelled test run, a crashed test host) leaves its stack, which holds the ports
    private static async Task RemoveStaleStacksAsync()
    {
        var projects = await RunDockerAsync(["compose", "ls", "--all", "--quiet", "--filter", $"name={PROJECT_PREFIX}"], "could not list the compose projects");

        foreach (var project in projects.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            await RunAsync(["compose", "--project-name", project, "down", "--volumes", "--remove-orphans"], CommandTimeout, $"could not remove the stale smoke stack {project}");
    }

    //the lobby is ready when it sends its greeting; Docker's port proxy takes a connection even when the server is down,
    //so a server container that stopped ends the wait at once
    private async Task WaitForGreetingAsync()
    {
        var deadline = DateTime.UtcNow + StartTimeout;

        while (!await GreetsAsync())
        {
            var state = (await RunComposeAsync(["ps", "--all", "--format", "{{.State}}", SERVICE], CommandTimeout)).Trim();

            if (state is "exited" or "dead")
                throw new InvalidOperationException($"the smoke server stopped ({state}){Environment.NewLine}{await LogsAsync()}");

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"the smoke server sent no lobby greeting on port {PORT} in {StartTimeout.TotalMinutes} min{Environment.NewLine}{await LogsAsync()}");

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    //true when a server on the lobby port sends at least one byte after the connection
    private static async Task<bool> GreetsAsync()
    {
        using var client = new TcpClient();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        try
        {
            await client.ConnectAsync(HOST, PORT, cancel.Token);
            var buffer = new byte[1];

            return await client.GetStream().ReadAsync(buffer, cancel.Token) > 0;
        } catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            return false;
        }
    }

    private Task<string> RunComposeAsync(string[] arguments, TimeSpan timeout)
    {
        var compose = Path.Combine(SmokeEnvironment.ServerPath!, "docker-compose.yml");

        return RunAsync(["compose", "--project-name", Project, "--file", compose, "--file", OverrideFile!, .. arguments], timeout, $"docker compose {arguments[0]} failed");
    }

    private static Task<string> RunDockerAsync(string[] arguments, string failure) => RunAsync(arguments, TimeSpan.FromSeconds(60), failure);

    private static async Task<string> RunAsync(string[] arguments, TimeSpan timeout, string failure)
    {
        var start = new ProcessStartInfo("docker")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        Process process;

        try
        {
            process = Process.Start(start)!;
        } catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"{failure}: the docker command was not found ({ex.Message})", ex);
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var cancel = new CancellationTokenSource(timeout);

            try
            {
                await process.WaitForExitAsync(cancel.Token);
            } catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);

                throw new TimeoutException($"{failure}: docker {string.Join(' ', arguments)} took more than {timeout.TotalSeconds} s");
            }

            var output = await stdout;
            var errors = await stderr;

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"{failure} (exit code {process.ExitCode}): {output}{errors}");

            return output;
        }
    }
}
