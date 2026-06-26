#region
using System.Text.Json;
using System.Text.Json.Serialization;
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

    private const string FILE_NAME = "config.json";

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
    ///     <c>%AppData%\Brigid</c> (Windows), <c>~/.config/Brigid</c> (Linux/macOS via .NET's
    ///     <see cref="Environment.SpecialFolder.ApplicationData" /> mapping). The obvious, user-writable, cross-platform
    ///     home for the config file.
    /// </summary>
    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Brigid");

    public static string FilePath => Path.Combine(ConfigDirectory, FILE_NAME);

    /// <summary>
    ///     Loads the config file into the static state. Missing/corrupt files yield a fresh default list. Always ensures at
    ///     least the default <c>da0.kru.com:2610</c> server exists, and migrates the legacy single host/port shape (from the
    ///     first version of this file) into a list entry.
    /// </summary>
    public static void Load()
    {
        Model? model = null;

        if (File.Exists(FilePath))
            try
            {
                model = JsonSerializer.Deserialize<Model>(File.ReadAllText(FilePath), SerializerOptions);
            } catch
            {
                //corrupt config — fall through to defaults
            }

        Servers = model?.Servers ?? [];
        SelectedServer = model?.SelectedServer;
        AssetPath = model?.AssetPath;

        //migrate the pre-server-list shape: a single Host/Port becomes a list entry
        if (!string.IsNullOrWhiteSpace(model?.Host))
            AddOrSelectServer(model.Host, model.Port is >= 1 and <= 65535 ? model.Port.Value : DEFAULT_PORT, null);

        //ship with a default server so the dropdown is never empty
        if (Servers.Count == 0)
            Servers.Add(new ServerEntry { Host = DEFAULT_HOST, Port = DEFAULT_PORT });

        if (GetSelectedServer() is null)
            SelectedServer = Servers[0].Key;
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);

            var model = new Model
            {
                Servers = Servers,
                SelectedServer = SelectedServer,
                AssetPath = AssetPath
            };

            File.WriteAllText(FilePath, JsonSerializer.Serialize(model, SerializerOptions));
        } catch
        {
            //best effort — don't crash on save failure
        }
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

    /// <summary>
    ///     A directory is a usable asset path when it exists and contains the core archives the client always loads
    ///     (<c>khanpal.dat</c> for palettes, <c>legend.dat</c>). Matched case-insensitively because Dark Ages data
    ///     directories vary in filename casing across platforms.
    /// </summary>
    public static bool IsValidAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        var present = Directory.EnumerateFiles(path)
                               .Select(Path.GetFileName)
                               .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return present.Contains("khanpal.dat") && present.Contains("legend.dat");
    }

    private sealed class Model
    {
        public List<ServerEntry>? Servers { get; set; }
        public string? SelectedServer { get; set; }
        public string? AssetPath { get; set; }

        //legacy (pre-server-list) fields, migrated into Servers on load
        public string? Host { get; set; }
        public int? Port { get; set; }
    }
}
