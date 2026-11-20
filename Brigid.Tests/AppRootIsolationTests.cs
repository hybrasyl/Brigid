using Brigid.Data;
using Brigid.Systems;
using Brigid.Systems.Keybinds;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     BRIG-184: a client with <c>BRIGID_APP_ROOT</c> set (a smoke test) starts with only what the test put in its folder.
///     It does not move <c>keybinds.json</c> out of the data folder, and it does not read the legacy <c>Darkages.cfg</c>
///     there, so a smoke run never takes or reads the developer's own files.
/// </summary>
[Collection("AppRoot")]
public sealed class AppRootIsolationTests : IDisposable
{
    private readonly string Root = Directory.CreateTempSubdirectory("brigid-approot-").FullName;
    private readonly string OriginalDataPath = GlobalSettings.DataPath;
    private readonly int OriginalSoundVolume = ClientSettings.SoundVolume;

    private string AppRoot => Path.Combine(Root, "app");
    private string DataFolder => Path.Combine(Root, "data");

    public AppRootIsolationTests()
    {
        Directory.CreateDirectory(AppRoot);
        Directory.CreateDirectory(DataFolder);
        AppPaths.SetAppRootOverrideForTests(AppRoot);
        GlobalSettings.DataPath = DataFolder;
    }

    public void Dispose()
    {
        AppPaths.ResetAppRootOverrideForTests();
        GlobalSettings.DataPath = OriginalDataPath;
        ClientSettings.SoundVolume = OriginalSoundVolume;
        Keybinds.ResetAll();
        Directory.Delete(Root, true);
    }

    //a test that only asserts a file is absent passes when AppRoot ignores the override and reads the developer's own
    //folder; this one sees it
    [Fact]
    public void TheAppFolderIsTheFolderOfTheOverride()
    {
        Assert.Equal(AppRoot, AppPaths.AppRoot);
        Assert.StartsWith(AppRoot, AppPaths.KeybindsFile);
        Assert.StartsWith(AppRoot, AppPaths.SettingsFile);
    }

    //the vendor-folder migration moves files; with the override it does not run, so a smoke run on Linux does not move a
    //developer's legacy lowercase erisco folder
    [Fact]
    public void TheVendorFolderMigrationDoesNotRun()
    {
        var lines = new List<string>();
        var original = DataDiagnostics.Log;
        DataDiagnostics.Log = lines.Add;

        try
        {
            AppPaths.MigrateLegacyVendorFolder();
        } finally
        {
            DataDiagnostics.Log = original;
        }

        Assert.Equal([$"[paths] {AppPaths.APP_ROOT_VARIABLE} is set: the app folder is {AppRoot}, and no folder migration runs"], lines);
    }

    [Fact]
    public void TheKeybindsOfTheDataFolderStayThere()
    {
        var legacy = Path.Combine(DataFolder, "keybinds.json");
        File.WriteAllText(legacy, "{}");

        Keybinds.Load();

        Assert.True(File.Exists(legacy));
        Assert.StartsWith(AppRoot, AppPaths.KeybindsFile);
        Assert.False(File.Exists(AppPaths.KeybindsFile));
    }

    [Fact]
    public void TheLegacySettingsOfTheDataFolderAreNotRead()
    {
        File.WriteAllText(Path.Combine(DataFolder, "Darkages.cfg"), "Sound Volume : 9\n");
        ClientSettings.SoundVolume = 5;

        ClientSettings.Load();

        Assert.Equal(5, ClientSettings.SoundVolume);
    }
}
