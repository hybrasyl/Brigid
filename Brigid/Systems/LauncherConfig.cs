#region
using System.Text.Json;
using System.Text.Json.Serialization;
using Brigid.Data;
#endregion

namespace Brigid.Systems;

/// <summary>
///     A saved server the launcher can connect to. <see cref="Name" /> is an optional friendly label; when blank the
///     launcher shows <c>host:port</c>. <see cref="Key" /> is the stable identity used to remember the selection.
/// </summary>
public sealed class ServerEntry
{
    public string? Name { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = LauncherConfig.DEFAULT_PORT;

    [JsonIgnore]
    public string Key => $"{Host}:{Port}";

    [JsonIgnore]
    public string Display => string.IsNullOrWhiteSpace(Name) ? $"{Host}:{Port}" : $"{Name} ({Host}:{Port})";
}

/// <summary>
///     Launcher configuration: the saved server list, the remembered selection, and the Dark Ages asset (<c>.dat</c>)
///     directory. Unlike <see cref="ClientSettings" /> (retail <c>Darkages.cfg</c>, stored inside the asset path), this
///     is serialized as JSON to the per-user application-data directory — it must live <em>outside</em> the asset path
///     because it is what tells the client where the asset path is. Read on every launch to populate the launcher screen.
/// </summary>
public static class LauncherConfig
{
    public const int DEFAULT_PORT = 2610;
    public const string DEFAULT_HOST = "da0.kru.com";

    //frozen: the config filename in the pre-Erisco home, used only to locate the file to migrate FROM. The live
    //config name is owned by AppPaths.ConfigFile and may diverge from this without affecting migration.
    private const string LEGACY_FILE_NAME = "config.json";
    private const string FILE_DESCRIPTION = "server list";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static List<ServerEntry> Servers { get; private set; } = [];

    /// <summary>The <see cref="ServerEntry.Key" /> of the last-selected server, or null. Persisted across launches.</summary>
    public static string? SelectedServer { get; set; }

    public static string? AssetPath { get; set; }

    /// <summary>
    ///     When true, startup skips the launcher screen and auto-connects using the saved server + asset path (see
    ///     <c>GlobalSettings.ResolveConnectionConfig</c>). Set from the launcher's "skip next time" checkbox; there is
    ///     deliberately no in-app way to clear it — the user re-enables the launcher by editing this flag to
    ///     <c>false</c> (or deleting it) in <see cref="FilePath" />. Persisted across launches.
    /// </summary>
    public static bool SuppressLauncher { get; set; }

    /// <summary>
    ///     Window size as an integer multiple of the 640×480 virtual resolution (e.g. 2 = 1280×960). Null when the user
    ///     has never chosen one, in which case the client falls back to its default multiplier. Persisted across launches.
    /// </summary>
    public static int? WindowMultiplier { get; set; }

    /// <summary>
    ///     <c>%LOCALAPPDATA%\Erisco\Brigid</c> (Windows), <c>~/.config/Erisco/Brigid</c> (Linux/macOS) via
    ///     <see cref="AppPaths" /> — the user-writable, cross-platform home shared by the Erisco toolset. Must live
    ///     <em>outside</em> the asset path because it is what tells the client where the asset path is.
    /// </summary>
    public static string ConfigDirectory => AppPaths.AppRoot;

    public static string FilePath => AppPaths.ConfigFile;

    /// <summary>
    ///     The pre-Erisco config location (<c>%AppData%\Brigid</c> on Windows, <c>~/.config/Brigid</c> on Unix). Read once
    ///     on first launch after the move so an existing user's server list and asset path migrate to the new home.
    /// </summary>
    private static string LegacyFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Brigid", LEGACY_FILE_NAME);

    /// <summary>
    ///     Loads the config file into the static state. A missing file yields a fresh default list. A file that cannot be
    ///     read yields the defaults, and its save is refused, so the defaults do not replace the player's servers; a file
    ///     that does not parse yields the defaults and is kept as a <c>.bad</c> copy (see <see cref="GuardedFile" />).
    ///     Always ensures at least the default <c>da0.kru.com:2610</c> server exists, and migrates the legacy single
    ///     host/port shape (from the first version of this file) into a list entry.
    /// </summary>
    public static void Load()
    {
        TryMigrateLegacyConfig();
        Load(FilePath);
    }

    /// <summary>True when <paramref name="port" /> is a TCP port that a server can listen on (1 to 65535).</summary>
    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;

    /// <summary>Loads the config file at <paramref name="path" />, with no legacy migration. Internal for the tests.</summary>
    internal static void Load(string path)
    {
        var model = GuardedFile.ForSession(path, FILE_DESCRIPTION).ReadJson<Model>(SerializerOptions);

        //a hand edit can leave a null entry, an entry with no host or a port out of range; such an entry has no server to
        //connect to
        Servers = [.. (model?.Servers ?? []).Where(server => (server is not null) && !string.IsNullOrWhiteSpace(server.Host) && IsValidPort(server.Port))];
        SelectedServer = model?.SelectedServer;
        AssetPath = model?.AssetPath;
        WindowMultiplier = model?.WindowMultiplier;
        SuppressLauncher = model?.SuppressLauncher ?? false;

        //migrate the pre-server-list shape: a single Host/Port becomes a list entry
        if (!string.IsNullOrWhiteSpace(model?.Host))
            AddOrSelectServer(model.Host, model.Port is { } port && IsValidPort(port) ? port : DEFAULT_PORT, null);

        //ship with a default server so the dropdown is never empty
        if (Servers.Count == 0)
            Servers.Add(new ServerEntry { Host = DEFAULT_HOST, Port = DEFAULT_PORT });

        if (GetSelectedServer() is null)
            SelectedServer = Servers[0].Key;
    }

    /// <summary>
    ///     One-time, best-effort copy of the pre-Erisco config into the new <see cref="AppPaths.AppRoot" /> home. No-op
    ///     once the new file exists or when there is nothing to migrate. Never throws — a failed migration just falls
    ///     through to default config on first launch.
    /// </summary>
    private static void TryMigrateLegacyConfig() => TryMigrateLegacyConfig(LegacyFilePath, FilePath);

    /// <summary>Copies the legacy config at <paramref name="legacyPath" /> to <paramref name="path" /> when <see cref="TakesLegacyConfig" />. Internal for the tests.</summary>
    internal static void TryMigrateLegacyConfig(string legacyPath, string path)
    {
        if (!TakesLegacyConfig(path))
            return;

        //best effort: a missing or locked legacy file gives the default config
        DataDiagnostics.Try(
            () =>
            {
                if (!File.Exists(legacyPath))
                    return;

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(legacyPath, path, true);
            }, "copy the legacy launcher config");
    }

    /// <summary>
    ///     True when the config at <paramref name="path" /> is not found, or is empty (a crash during the first save of an
    ///     older client). A file that cannot be read is not replaced: <see cref="File.Exists" /> is also false when the check
    ///     fails, so the legacy config was copied over a file that a check could not see.
    /// </summary>
    internal static bool TakesLegacyConfig(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            return stream.Length == 0;
        } catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Saves the config file. Best effort: a failed write is logged, and a file whose read failed is not written.</summary>
    public static void Save() => Save(FilePath);

    /// <summary>Saves the config to the file at <paramref name="path" />. Internal for the tests.</summary>
    internal static SaveResult Save(string path)
    {
        var model = new Model
        {
            Servers = Servers,
            SelectedServer = SelectedServer,
            AssetPath = AssetPath,
            WindowMultiplier = WindowMultiplier,
            SuppressLauncher = SuppressLauncher
        };

        return GuardedFile.ForSession(path, FILE_DESCRIPTION).SaveJson(model, SerializerOptions);
    }

    /// <summary>Returns the currently-selected server, falling back to the first entry (or null if the list is empty).</summary>
    public static ServerEntry? GetSelectedServer()
    {
        if (Servers.Count == 0)
            return null;

        return Servers.FirstOrDefault(s => s.Key == SelectedServer) ?? Servers[0];
    }

    /// <summary>
    ///     Adds a server (or reselects the matching existing one by host:port) and marks it selected. Returns the entry.
    /// </summary>
    public static ServerEntry AddOrSelectServer(string host, int port, string? name)
    {
        var key = $"{host}:{port}";
        var existing = Servers.FirstOrDefault(s => s.Key == key);

        if (existing is null)
        {
            existing = new ServerEntry { Host = host, Port = port, Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim() };
            Servers.Add(existing);
        }

        SelectedServer = existing.Key;

        return existing;
    }

    public static void RemoveServer(ServerEntry entry)
    {
        Servers.Remove(entry);

        if (SelectedServer == entry.Key)
            SelectedServer = Servers.Count > 0 ? Servers[0].Key : null;
    }

    private sealed class Model
    {
        public List<ServerEntry>? Servers { get; set; }
        public string? SelectedServer { get; set; }
        public string? AssetPath { get; set; }
        public int? WindowMultiplier { get; set; }
        public bool SuppressLauncher { get; set; }

        //legacy (pre-server-list) fields, migrated into Servers on load
        public string? Host { get; set; }
        public int? Port { get; set; }
    }
}
