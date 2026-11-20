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

    //a null entry in the server list threw at the start
    [Fact]
    public void ALauncherConfigDropsANullServerAndAServerWithNoHost()
    {
        var path = WriteFile("launcher.json", "{ \"Servers\": [ null, { \"Host\": null }, { \"Host\": \"qa.hybrasyl.com\", \"Port\": 2610 } ] }");

        LauncherConfig.Load(path);

        Assert.Equal(["qa.hybrasyl.com:2610"], LauncherConfig.Servers.Select(server => server.Key));
        Assert.Equal("qa.hybrasyl.com:2610", LauncherConfig.GetSelectedServer()?.Key);
    }

    //a port that a hand edit put out of range was selected, and each connect to it failed
    [Fact]
    public void ALauncherConfigDropsAServerWithAPortOutOfRange()
    {
        var path = WriteFile(
            "launcher.json",
            "{ \"Servers\": [ { \"Host\": \"a.example\", \"Port\": 0 }, { \"Host\": \"b.example\", \"Port\": 70000 }, { \"Host\": \"qa.hybrasyl.com\", \"Port\": 2610 } ], \"SelectedServer\": \"b.example:70000\" }");

        LauncherConfig.Load(path);

        Assert.Equal(["qa.hybrasyl.com:2610"], LauncherConfig.Servers.Select(server => server.Key));
        Assert.Equal("qa.hybrasyl.com:2610", LauncherConfig.GetSelectedServer()?.Key);
    }

    //a null record loaded, and each connect to its host threw
    [Fact]
    public void ATrustStoreDropsANullRecord()
    {
        var path = WriteFile("known-servers.json", "{ \"Version\": 2, \"Servers\": { \"self.example\": null } }");

        CertificateTrustStore.Load(path);

        Assert.Equal(CertificateTrustStore.RevocationModeFor("unknown.example"), CertificateTrustStore.RevocationModeFor("self.example"));
        Assert.NotNull(CertificateTrustStore.OptionsFor("self.example"));
    }

    //File.Exists is false when the check fails, and the legacy settings loaded and were saved over brigid.cfg. A folder
    //in the place of brigid.cfg is a path that a check cannot see and a read cannot open
    [Fact]
    public void ASettingsFileThatACheckCannotSeeDoesNotTakeTheLegacySettings()
    {
        var path = Path.Combine(Folder, "brigid.cfg");
        Directory.CreateDirectory(path);
        var legacy = WriteFile("Darkages.cfg", "Sound Volume : 9\n");
        var volume = ClientSettings.SoundVolume;

        try
        {
            ClientSettings.SoundVolume = 4;
            ClientSettings.Load(path, legacy);

            Assert.Equal(4, ClientSettings.SoundVolume);
            Assert.Equal(SaveResult.Unreadable, ClientSettings.Save(path));
        } finally
        {
            ClientSettings.SoundVolume = volume;
        }
    }

    //the first save made brigid.cfg with the defaults, and the legacy file was not tried again
    [Fact]
    public void ALegacySettingsFileThatCannotBeReadIsTriedAgainAtTheNextStart()
    {
        var path = Path.Combine(Folder, "brigid.cfg");
        var legacy = WriteFile("Darkages.cfg", "Sound Volume : 9\n");

        using (new FileStream(legacy, FileMode.Open, FileAccess.Read, FileShare.None))
            ClientSettings.Load(path, legacy);

        Assert.Equal(SaveResult.Unreadable, ClientSettings.Save(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TheFirstStartSavesTheLegacySettingsToTheNewFile()
    {
        var path = Path.Combine(Folder, "brigid.cfg");
        var legacy = WriteFile("Darkages.cfg", "Sound Volume : 9\n");
        var volume = ClientSettings.SoundVolume;

        try
        {
            ClientSettings.Load(path, legacy);

            Assert.Equal(9, ClientSettings.SoundVolume);
            Assert.Contains("Sound Volume : 9", File.ReadAllText(path));
        } finally
        {
            ClientSettings.SoundVolume = volume;
        }
    }

    //the load and the save of each settings file, by its name
    private static (Action<string> Load, Func<string, SaveResult> Save) LoadAndSaveOf(string name)
        => name switch
        {
            "keybinds.json" => (Keybinds.Load, Keybinds.Save),
            "brigid.cfg"    => (p => Assert.False(ClientSettings.Load(p)), ClientSettings.Save),
            "launcher.json" => (LauncherConfig.Load, LauncherConfig.Save),
            _               => (CertificateTrustStore.Load, CertificateTrustStore.Save)
        };

    public static TheoryData<string, string> SettingsFiles => new()
    {
        { "keybinds.json", GOOD_KEYBINDS },
        { "brigid.cfg", GOOD_SETTINGS },
        { "launcher.json", GOOD_LAUNCHER },
        { "known-servers.json", GOOD_TRUST }
    };

    [Theory]
    [MemberData(nameof(SettingsFiles))]
    public void ALockedSettingsFileKeepsItsContents(string name, string contents)
    {
        var path = WriteFile(name, contents);
        var (load, save) = LoadAndSaveOf(name);

        Assert.Equal(SaveResult.Unreadable, LoadLockedThenSave(path, load, save));
        Assert.Equal(contents, File.ReadAllText(path));
    }

    //File.Exists is false when the check fails, and the legacy launcher config was copied over a file that a check could
    //not see. A folder in the place of the file is such a path; a missing or empty file still takes the legacy config
    [Fact]
    public void TheLegacyLauncherConfigReplacesOnlyAMissingOrEmptyFile()
    {
        var unseen = Path.Combine(Folder, "unseen.json");
        Directory.CreateDirectory(unseen);

        Assert.False(LauncherConfig.TakesLegacyConfig(unseen));
        Assert.False(LauncherConfig.TakesLegacyConfig(WriteFile("full.json", GOOD_LAUNCHER)));
        Assert.True(LauncherConfig.TakesLegacyConfig(WriteFile("empty.json", string.Empty)));
        Assert.True(LauncherConfig.TakesLegacyConfig(Path.Combine(Folder, "missing.json")));
    }

    [Fact]
    public void TheLegacyLauncherConfigIsCopiedToAMissingFile()
    {
        var legacy = WriteFile("legacy.json", GOOD_LAUNCHER);
        var path = Path.Combine(Folder, "new", "launcher.json");

        LauncherConfig.TryMigrateLegacyConfig(legacy, path);

        Assert.Equal(GOOD_LAUNCHER, File.ReadAllText(path));
    }

    public static TheoryData<string> JsonFiles => new("keybinds.json", "launcher.json", "known-servers.json");

    //the corrupt file is kept as a .bad copy, and the client saves its defaults
    [Theory]
    [MemberData(nameof(JsonFiles))]
    public void ACorruptJsonFileIsKeptAsABadCopyAndSaves(string name)
    {
        var path = WriteFile(name, "{ broken");

        var (load, save) = LoadAndSaveOf(name);

        load(path);

        Assert.Equal("{ broken", File.ReadAllText(path + ".bad"));
        Assert.Equal(SaveResult.Saved, save(path));
        Assert.NotEqual("{ broken", File.ReadAllText(path));
    }
}
