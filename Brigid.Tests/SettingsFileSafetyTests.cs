using Brigid.Data;
using Brigid.Systems;
using Brigid.Systems.Keybinds;
using Xunit;

namespace Brigid.Tests;

// The four settings files read and save through GuardedFile, so a locked file never loses the player's contents: it loads
// as defaults and its save leaves the file as it was, and a file that does not parse is kept as a .bad copy. Before, a
// locked keybinds.json lost every saved key at the next rebind (BRIG-177). A locked or corrupt load returns before it changes Keybinds or ClientSettings; the launcher
// config and the trust store load their defaults (the SettingsFiles collection).
[Collection("SettingsFiles")]
public sealed class SettingsFileSafetyTests : IDisposable
{
    private const string GOOD_KEYBINDS = "{ \"Editor_Paste\": { \"Primary\": { \"Key\": \"F12\", \"Mods\": \"None\" } } }";
    private const string GOOD_SETTINGS = "Sound Volume : 3\n";
    private const string GOOD_LAUNCHER = "{ \"Servers\": [ { \"Host\": \"qa.hybrasyl.com\", \"Port\": 2610 } ] }";
    private const string GOOD_TRUST = "{ \"Version\": 2, \"Servers\": {} }";

    private readonly string Folder = Path.Combine(Path.GetTempPath(), "brigid-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsFileSafetyTests() => Directory.CreateDirectory(Folder);

    public void Dispose() => Directory.Delete(Folder, true);

    private string WriteFile(string name, string contents)
    {
        var path = Path.Combine(Folder, name);
        File.WriteAllText(path, contents);

        return path;
    }

    //loads the file while another program holds it with no read sharing, then saves
    private static SaveResult LoadLockedThenSave(string path, Action<string> load, Func<string, SaveResult> save)
    {
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            load(path);

        return save(path);
    }

    [Fact]
    public void ALockedKeybindsFileKeepsTheSavedKeys()
    {
        var path = WriteFile("keybinds.json", GOOD_KEYBINDS);

        Assert.Equal(SaveResult.Unreadable, LoadLockedThenSave(path, Keybinds.Load, Keybinds.Save));
        Assert.Equal(GOOD_KEYBINDS, File.ReadAllText(path));
    }

    [Fact]
    public void ALockedSettingsFileKeepsTheSavedSettings()
    {
        var path = WriteFile("brigid.cfg", GOOD_SETTINGS);

        Assert.Equal(SaveResult.Unreadable, LoadLockedThenSave(path, p => Assert.False(ClientSettings.Load(p)), ClientSettings.Save));
        Assert.Equal(GOOD_SETTINGS, File.ReadAllText(path));
    }

    [Fact]
    public void ALockedLauncherConfigKeepsTheSavedServers()
    {
        var path = WriteFile("launcher.json", GOOD_LAUNCHER);

        Assert.Equal(SaveResult.Unreadable, LoadLockedThenSave(path, LauncherConfig.Load, LauncherConfig.Save));
        Assert.Equal(GOOD_LAUNCHER, File.ReadAllText(path));
    }

    //a null entry in the server list threw at the start
    [Fact]
    public void ALauncherConfigDropsANullServerAndAServerWithNoHost()
    {
        var path = WriteFile("launcher.json", "{ \"Servers\": [ null, { \"Host\": null }, { \"Host\": \"qa.hybrasyl.com\", \"Port\": 2610 } ] }");

        LauncherConfig.Load(path);

        Assert.Equal(["qa.hybrasyl.com:2610"], LauncherConfig.Servers.Select(server => server.Key));
        Assert.Equal("qa.hybrasyl.com:2610", LauncherConfig.GetSelectedServer()?.Key);
    }

    [Fact]
    public void ALockedTrustStoreKeepsTheAcceptedServers()
    {
        var path = WriteFile("known-servers.json", GOOD_TRUST);

        Assert.Equal(SaveResult.Unreadable, LoadLockedThenSave(path, CertificateTrustStore.Load, CertificateTrustStore.Save));
        Assert.Equal(GOOD_TRUST, File.ReadAllText(path));
    }

    public static TheoryData<string> JsonFiles => new("keybinds.json", "launcher.json", "known-servers.json");

    //the corrupt file is kept as a .bad copy, and the client saves its defaults
    [Theory]
    [MemberData(nameof(JsonFiles))]
    public void ACorruptJsonFileIsKeptAsABadCopyAndSaves(string name)
    {
        var path = WriteFile(name, "{ broken");

        (Action<string> load, Func<string, SaveResult> save) = name switch
        {
            "keybinds.json" => ((Action<string>)Keybinds.Load, (Func<string, SaveResult>)Keybinds.Save),
            "launcher.json" => (LauncherConfig.Load, LauncherConfig.Save),
            _               => (CertificateTrustStore.Load, CertificateTrustStore.Save)
        };

        load(path);

        Assert.Equal("{ broken", File.ReadAllText(path + ".bad"));
        Assert.Equal(SaveResult.Saved, save(path));
        Assert.NotEqual("{ broken", File.ReadAllText(path));
    }
}
