#region
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
#endregion

namespace Brigid.SmokeTests;

/// <summary>What a control looks like to the driver: its place in virtual 640×480 pixels, and its state.</summary>
public sealed record ControlInfo(string Path, string Type, bool Shown, bool Enabled, int X, int Y, int Width, int Height, string? Text);

/// <summary>
///     The state of the client: the type name of the active screen and the connection state. In the world, also the map
///     id, whether its tiles are loaded, and the player's tile; outside the world those are null.
/// </summary>
public sealed record ClientState(string? Screen, string Connection, int? MapId, bool? MapLoaded, int? PlayerX, int? PlayerY);

/// <summary>The virtual point at the centre of a world tile, and whether that point is in the map viewport.</summary>
public sealed record TilePoint(int X, int Y, bool InViewport);

/// <summary>
///     One running client, driven through the test driver (BRIG-184). <see cref="RunAsync" /> starts a client in a new
///     temporary app folder (<c>BRIGID_APP_ROOT</c>), with the driver in its dev module folder, runs the test body, and
///     on a failure saves a frame capture and the client log into <c>smoke-artifacts</c> beside the tests. The client
///     never reads or writes the developer's own settings, and closes itself when the test is gone.
/// </summary>
public sealed class BrigidApp : IAsyncDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private static int Swept;

    private readonly Process Client;
    private readonly List<string> Output = [];
    private readonly Lock OutputGate = new();
    private bool Started;
    private readonly List<string> Captures = [];
    private readonly Lock CapturesGate = new();
    private DriverClient? Driver;

    private BrigidApp(string root, Process client)
    {
        Root = root;
        Client = client;
    }

    /// <summary>The temporary app folder of this client.</summary>
    public string Root { get; }

    /// <summary>The lines that the client wrote to its output so far.</summary>
    public IReadOnlyList<string> OutputLines
    {
        get
        {
            using (OutputGate.EnterScope())
                return Output.ToArray();
        }
    }

    /// <summary>
    ///     Starts a client, runs <paramref name="body" />, and stops the client. On a failure, the exception names the
    ///     folder with the frame captures and the client log.
    /// </summary>
    public static Task RunAsync(Func<BrigidApp, Task> body, [CallerMemberName] string testName = "") => RunAsync(null, body, testName);

    /// <summary>
    ///     Starts the server when it is not running, then a client, and runs <paramref name="body" />. On a failure, the
    ///     artifacts also have the server's log.
    /// </summary>
    public static async Task RunAsync(SmokeServer? server, Func<BrigidApp, Task> body, [CallerMemberName] string testName = "")
    {
        if (server is not null)
            await server.EnsureStartedAsync();

        var app = await StartAsync(testName);

        try
        {
            await body(app);
        } catch (Exception ex)
        {
            //an artifact that cannot be saved must not take the place of the failure of the test
            string folder;

            try
            {
                folder = await app.SaveArtifactsAsync(testName);

                if (server is not null)
                    await File.WriteAllTextAsync(Path.Combine(folder, "server.log"), await server.LogsAsync());
            } catch (Exception saveError)
            {
                folder = $"(not saved: {saveError.GetType().Name}: {saveError.Message})";
            }

            throw new SmokeFailure($"{ex.Message}{Environment.NewLine}Artifacts: {folder}", ex);
        } finally
        {
            await app.DisposeAsync();
        }
    }

    private static async Task<BrigidApp> StartAsync(string testName)
    {
        if (SmokeEnvironment.DataError is { } dataError)
            throw new InvalidOperationException(dataError);

        var (clientDll, driverDll) = SmokeEnvironment.BuildPaths();

        if (Interlocked.Exchange(ref Swept, 1) == 0)
            SweepStaleTempEntries();

        var root = Path.Combine(Path.GetTempPath(), SmokeOwner.NewName(32));
        BrigidApp? app = null;

        try
        {
            PrepareRoot(root, driverDll);
            app = new BrigidApp(root, CreateProcess(clientDll, root, out var pipe));
            app.Client.OutputDataReceived += (_, e) => app.Record(e.Data);
            app.Client.ErrorDataReceived += (_, e) => app.Record(e.Data);
            app.Client.Start();
            app.Started = true;
            app.Client.BeginOutputReadLine();
            app.Client.BeginErrorReadLine();

            using var cancel = new CancellationTokenSource(StartTimeout);

            try
            {
                var exited = app.Client.WaitForExitAsync(cancel.Token);
                var connect = DriverClient.ConnectAsync(pipe, cancel.Token);

                if ((await Task.WhenAny(connect, exited) != connect) || !connect.IsCompletedSuccessfully)
                {
                    if (app.Client.HasExited)
                        throw new InvalidOperationException($"the client stopped before the driver started (exit code {app.Client.ExitCode})");

                    if (cancel.IsCancellationRequested)
                        throw new TimeoutException($"the driver did not start in {StartTimeout.TotalSeconds} s");
                }

                app.Driver = await connect;
            } finally
            {
                //a connect that still waits stops here, not at the end of the run
                await cancel.CancelAsync();
            }

            return app;
        } catch (Exception ex)
        {
            //every failure here stops the client and removes the folder, so a failed start leaves nothing behind. An
            //artifact folder that cannot be made hid the real failure and skipped the cleanup
            string folder;

            try
            {
                folder = app is null ? "(none)" : await app.SaveArtifactsAsync($"{testName}-start");
            } catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException)
            {
                folder = $"(not saved: {saveError.Message})";
            }

            if (app is not null)
                await app.DisposeAsync();
            else
                DeleteRoot(root);

            throw new SmokeFailure($"the client did not start the test driver: {ex.Message}{Environment.NewLine}Artifacts: {folder}", ex);
        }
    }

    private static void PrepareRoot(string root, string driverDll)
    {
        var modules = Directory.CreateDirectory(Path.Combine(root, "devmodules")).FullName;
        var assets = Directory.CreateDirectory(Path.Combine(root, "assets")).FullName;

        foreach (var file in new[] { driverDll, Path.ChangeExtension(driverDll, ".pdb") }.Where(File.Exists))
            File.Copy(file, Path.Combine(modules, Path.GetFileName(file)));

        if (SmokeEnvironment.PacksPath is { } packs && Directory.Exists(packs))
            foreach (var pack in Directory.EnumerateFiles(packs, "*.datf"))
                File.Copy(pack, Path.Combine(assets, Path.GetFileName(pack)));
    }

    private static Process CreateProcess(string clientDll, string root, out string pipe)
    {
        //on Unix the pipe is a socket at $TMPDIR/CoreFxPipe_<name>, and macOS limits a socket path to 104 bytes with a
        //$TMPDIR of about 50 characters, so the name keeps 12 hex digits of the GUID
        pipe = "brigid-smoke-" + Guid.NewGuid().ToString("N")[..12];
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(clientDll)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(clientDll);
        start.Environment["BRIGID_APP_ROOT"] = root;
        start.Environment["BRIGID_DEV_MODULES"] = "1";
        start.Environment["BRIGID_TEST_PIPE"] = pipe;
        start.Environment["DA_HOST"] = SmokeServer.HOST;
        start.Environment["DA_HOST_PORT"] = SmokeServer.PORT.ToString();
        start.Environment["DA_ASSET_PATH"] = SmokeEnvironment.DataPath;
        start.Environment["DA_NO_LAUNCHER"] = "1";

        //an SDL hint that SDL reads from the environment: the new window does not take the focus from the developer
        start.Environment["SDL_WINDOW_NO_ACTIVATION_WHEN_SHOWN"] = "1";

        return new Process { StartInfo = start, EnableRaisingEvents = true };
    }

    /// <summary>The active screen, the connection and, in the world, the map and the player's tile.</summary>
    public async Task<ClientState> StateAsync()
    {
        var state = await SendAsync("state");

        return new ClientState(
            state["screen"]?.GetValue<string>(),
            state["connection"]!.GetValue<string>(),
            state["mapId"]?.GetValue<int>(),
            state["mapLoaded"]?.GetValue<bool>(),
            state["playerX"]?.GetValue<int>(),
            state["playerY"]?.GetValue<int>());
    }

    /// <summary>The client's own app folder, as the client resolves it.</summary>
    public async Task<string> AppRootAsync() => (await SendAsync("paths"))["appRoot"]!.GetValue<string>();

    /// <summary>The path of the character's <c>Macro.cfg</c>, as the client resolves it; null before the first world entry.</summary>
    public async Task<string?> MacroFileAsync() => (await SendAsync("paths"))["macroFile"]?.GetValue<string>();

    /// <summary>The point at the centre of the world tile (<paramref name="x" />, <paramref name="y" />).</summary>
    public async Task<TilePoint> TileAsync(int x, int y)
    {
        var tile = await SendAsync("tile", new JsonObject { ["x"] = x, ["y"] = y });

        return new TilePoint(tile["x"]!.GetValue<int>(), tile["y"]!.GetValue<int>(), tile["inViewport"]!.GetValue<bool>());
    }

    /// <summary>Clicks the centre of the world tile (<paramref name="x" />, <paramref name="y" />), which must be in the map viewport.</summary>
    public async Task<TilePoint> ClickTileAsync(int x, int y, string button = "left")
    {
        var tile = await TileAsync(x, y);

        if (!tile.InViewport)
            throw new InvalidOperationException($"the tile {x}, {y} is at {tile.X}, {tile.Y}, outside the map viewport");

        await ClickAtAsync(tile.X, tile.Y, button);

        return tile;
    }

    /// <summary>The text of the orange bar messages in this session, oldest first.</summary>
    public async Task<IReadOnlyList<string>> OrangeBarAsync()
        => (await SendAsync("orangebar"))["lines"]!.AsArray().Select(line => line!.GetValue<string>()).ToList();

    /// <summary>The control at <paramref name="path" /> (see the driver's locator), or null.</summary>
    public async Task<ControlInfo?> FindAsync(string path)
    {
        var found = await SendAsync("find", new JsonObject { ["path"] = path });

        if (found["found"]?.GetValue<bool>() != true)
            return null;

        return new ControlInfo(
            found["path"]!.GetValue<string>(),
            found["type"]!.GetValue<string>(),
            found["shown"]!.GetValue<bool>(),
            found["enabled"]!.GetValue<bool>(),
            found["x"]!.GetValue<int>(),
            found["y"]!.GetValue<int>(),
            found["width"]!.GetValue<int>(),
            found["height"]!.GetValue<int>(),
            found["text"]?.GetValue<string>());
    }

    /// <summary>The titles of the main menu's alerts.</summary>
    public async Task<IReadOnlyList<string>> AlertsAsync()
    {
        var alerts = await SendAsync("alerts");

        return alerts["titles"]!.AsArray().Select(title => title!.GetValue<string>()).ToList();
    }

    /// <summary>
    ///     Waits until the control at <paramref name="path" /> is shown and enabled, then clicks its centre as a player
    ///     does.
    /// </summary>
    public async Task ClickAsync(string path, string button = "left", params string[] mods)
    {
        await WaitForAsync(() => FindAsync(path), control => control is { Shown: true, Enabled: true }, $"the control {path} to show and enable");
        await SendAsync("click", new JsonObject { ["path"] = path, ["button"] = button, ["mods"] = Array(mods) });
    }

    /// <summary>Clicks the virtual point (<paramref name="x" />, <paramref name="y" />).</summary>
    public Task ClickAtAsync(int x, int y, string button = "left", params string[] mods)
        => SendAsync("click", new JsonObject { ["x"] = x, ["y"] = y, ["button"] = button, ["mods"] = Array(mods) });

    /// <summary>Presses and releases <paramref name="key" /> (a MonoGame <c>Keys</c> name) with the modifiers held.</summary>
    public Task PressAsync(string key, params string[] mods)
        => SendAsync("key", new JsonObject { ["key"] = key, ["mods"] = Array(mods) });

    /// <summary>Types <paramref name="text" /> as text input.</summary>
    public Task TypeAsync(string text) => SendAsync("text", new JsonObject { ["text"] = text });

    /// <summary>Saves the next frame, UI included, and returns its path in the client's temporary app folder.</summary>
    public async Task<string> ScreenshotAsync()
    {
        var path = (await SendAsync("screenshot"))["path"]!.GetValue<string>();

        using (CapturesGate.EnterScope())
            Captures.Add(path);

        return path;
    }

    /// <summary>
    ///     Reads <paramref name="probe" /> until <paramref name="condition" /> is true, and returns the value. A timeout
    ///     names <paramref name="what" /> and the last value read. Never a fixed sleep: a slow machine only waits longer.
    /// </summary>
    public async Task<T> WaitForAsync<T>(Func<Task<T>> probe, Func<T, bool> condition, string what, TimeSpan? timeout = null)
    {
        var wait = timeout ?? DefaultWait;
        var (met, last) = await PollAsync(probe, condition, wait);

        return met ? last : throw new TimeoutException($"waited {wait.TotalSeconds} s for {what}; the last value was {last?.ToString() ?? "null"}");
    }

    /// <summary>
    ///     Reads <paramref name="probe" /> until <paramref name="condition" /> is true, and returns the value; or returns
    ///     null when <paramref name="timeout" /> ends first. For a wait whose end is not a failure.
    /// </summary>
    public async Task<T?> TryWaitForAsync<T>(Func<Task<T>> probe, Func<T, bool> condition, TimeSpan timeout) where T: class
    {
        var (met, last) = await PollAsync(probe, condition, timeout);

        return met ? last : null;
    }

    //reads the probe until the condition is true or the time ends, and gives the last value read
    private static async Task<(bool Met, T Last)> PollAsync<T>(Func<Task<T>> probe, Func<T, bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            var last = await probe();

            if (condition(last))
                return (true, last);

            if (DateTime.UtcNow >= deadline)
                return (false, last);

            await Task.Delay(PollInterval);
        }
    }

    /// <summary>Waits until the active screen is of the type <paramref name="screen" />.</summary>
    public Task<ClientState> WaitForScreenAsync(string screen, TimeSpan? timeout = null)
        => WaitForAsync(StateAsync, state => state.Screen == screen, $"the screen {screen}", timeout);

    /// <summary>Waits until the control at <paramref name="path" /> is shown, and returns it.</summary>
    public async Task<ControlInfo> WaitForShownAsync(string path, TimeSpan? timeout = null)
        => (await WaitForAsync(() => FindAsync(path), control => control is { Shown: true }, $"the control {path} to show", timeout))!;

    private Task<JsonObject> SendAsync(string command, JsonObject? arguments = null)
    {
        var request = arguments ?? new JsonObject();
        request["cmd"] = command;

        return (Driver ?? throw new InvalidOperationException("the driver is not connected")).SendAsync(request, CommandTimeout);
    }

    private static JsonArray Array(string[] values) => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private void Record(string? line)
    {
        if (line is null)
            return;

        using (OutputGate.EnterScope())
            Output.Add(line);
    }

    //a frame capture at the failure (when the driver still answers), the captures of the run, and the client output
    private async Task<string> SaveArtifactsAsync(string testName)
    {
        var folder = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "smoke-artifacts", $"{testName}-{DateTime.Now:yyyyMMdd-HHmmss}")).FullName;

        if (Driver is not null && !Client.HasExited)
            try
            {
                await ScreenshotAsync();
            } catch (Exception ex) when (ex is SmokeCommandException or IOException or OperationCanceledException)
            {
                Record($"[smoke] no frame capture at the failure: {ex.Message}");
            }

        string[] captures;

        using (CapturesGate.EnterScope())
            captures = Captures.ToArray();

        foreach (var capture in captures.Where(File.Exists))
            File.Copy(capture, Path.Combine(folder, Path.GetFileName(capture)), true);

        using (OutputGate.EnterScope())
            File.WriteAllLines(Path.Combine(folder, "client.log"), Output);

        return folder;
    }

    /// <summary>Asks the client to close, kills it when it does not, and deletes its temporary app folder.</summary>
    public async ValueTask DisposeAsync()
    {
        //a process that did not start has no state to read
        if (!Started)
        {
            Client.Dispose();
            DeleteRoot(Root);

            return;
        }

        if (Driver is not null)
        {
            if (!Client.HasExited)
                try
                {
                    await Driver.SendAsync(new JsonObject { ["cmd"] = "quit" }, TimeSpan.FromSeconds(5));
                } catch (Exception ex) when (ex is SmokeCommandException or IOException or OperationCanceledException)
                {
                    Record($"[smoke] quit failed: {ex.Message}");
                }

            await Driver.DisposeAsync();
            Driver = null;
        }

        if (!Client.HasExited)
        {
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            try
            {
                await Client.WaitForExitAsync(wait.Token);
            } catch (OperationCanceledException)
            {
                Client.Kill(entireProcessTree: true);
                await Client.WaitForExitAsync();
            }
        }

        Client.Dispose();
        DeleteRoot(Root);
    }

    private static void DeleteRoot(string root)
    {
        //a client that just closed can still hold a file for a moment
        for (var attempt = 0; attempt < 5 && Directory.Exists(root); attempt++)
            try
            {
                Directory.Delete(root, true);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
    }

    //the app folders and the server files of runs whose test host was killed, so DisposeAsync did not run. The age of a
    //folder does not change when a file in it grows, so a long run looked stale; the owner's process id decides now
    private static void SweepStaleTempEntries()
    {
        var temp = new DirectoryInfo(Path.GetTempPath());

        foreach (var entry in temp.EnumerateFileSystemInfos(SmokeOwner.PREFIX + "*")
                                  .Where(entry => SmokeOwner.IsStale(entry.Name)))
            try
            {
                if (entry is DirectoryInfo folder)
                    folder.Delete(true);
                else
                    entry.Delete();
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                //still in use, or not ours to delete: the next run tries again
            }
    }
}

/// <summary>A smoke test failure, with the folder of its artifacts in the message.</summary>
public sealed class SmokeFailure(string message, Exception inner) : Exception(message, inner);
