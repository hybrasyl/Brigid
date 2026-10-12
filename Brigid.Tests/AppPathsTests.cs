using Brigid.Data;
using Xunit;

namespace Brigid.Tests;

// The Erisco Electron apps write their data under a folder named "Erisco". Brigid wrote "erisco". Windows and macOS
// ignore the difference, but on Linux the two names are separate folders, so a path that one app gives to another
// resolves to two places. The migration moves an existing Linux user's data. The move cases run only where the temp
// file system keeps case, as on Linux.
public sealed class AppPathsTests : IDisposable
{
    private readonly string Root = Path.Combine(Path.GetTempPath(), "brigid-vendor-" + Guid.NewGuid().ToString("N"));
    private readonly bool CaseSensitive;

    public AppPathsTests()
    {
        Directory.CreateDirectory(Root);

        var probe = Path.Combine(Root, "case-probe");
        File.WriteAllBytes(probe, []);
        CaseSensitive = !File.Exists(Path.Combine(Root, "CASE-PROBE"));
        File.Delete(probe);
    }

    public void Dispose() => Directory.Delete(Root, true);

    private string Legacy(params string[] parts) => Path.Combine([Root, "erisco", "Brigid", .. parts]);

    private string Current(params string[] parts) => Path.Combine([Root, "Erisco", "Brigid", .. parts]);

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void VendorRoot_UsesTheHouseStandardCase() => Assert.Equal("Erisco", Path.GetFileName(AppPaths.VendorRoot));

    [Fact]
    public void MigrateLegacyVendorFolder_LeavesTheConfigReadableAtTheStandardPath()
    {
        Write(Legacy("config.json"), "old");
        Write(Legacy("profiles", "kru", "Angelique", "macros.txt"), "m");

        var result = AppPaths.MigrateLegacyVendorFolder(Root);

        Assert.Equal("old", File.ReadAllText(Current("config.json")));
        Assert.Equal("m", File.ReadAllText(Current("profiles", "kru", "Angelique", "macros.txt")));

        if (CaseSensitive)
        {
            Assert.Equal(["config.json", "profiles"], result.Moved);
            Assert.Empty(result.LeftInPlace);
            Assert.False(Directory.Exists(Path.Combine(Root, "erisco")));
        } else
        {
            //one folder under two names: nothing moves
            Assert.Empty(result.Moved);
            Assert.Empty(result.LeftInPlace);
        }
    }

    [Fact]
    public void MigrateLegacyVendorFolder_MovesTheDataBesideAnInstalledApp()
    {
        if (!CaseSensitive)
            return;

        //elatha installs the binaries in Erisco/Brigid/app before Brigid first runs
        Write(Current("app", "Brigid"), "binary");
        Write(Legacy("config.json"), "old");

        var result = AppPaths.MigrateLegacyVendorFolder(Root);

        Assert.Equal(["config.json"], result.Moved);
        Assert.Equal("old", File.ReadAllText(Current("config.json")));
        Assert.Equal("binary", File.ReadAllText(Current("app", "Brigid")));
        Assert.False(Directory.Exists(Path.Combine(Root, "erisco")));
    }

    [Fact]
    public void MigrateLegacyVendorFolder_LeavesAnEntryTheNewFolderAlreadyHas()
    {
        if (!CaseSensitive)
            return;

        Write(Legacy("config.json"), "old");
        Write(Legacy("brigid.cfg"), "settings");
        Write(Current("config.json"), "new");

        var result = AppPaths.MigrateLegacyVendorFolder(Root);

        Assert.Equal(["brigid.cfg"], result.Moved);
        Assert.Equal(["config.json"], result.LeftInPlace);
        Assert.Equal("new", File.ReadAllText(Current("config.json")));
        Assert.Equal("old", File.ReadAllText(Legacy("config.json")));
        Assert.Equal("settings", File.ReadAllText(Current("brigid.cfg")));
    }

    [Fact]
    public void MigrateLegacyVendorFolder_DoesNothingWithoutALegacyFolder()
    {
        Directory.CreateDirectory(Current());

        var result = AppPaths.MigrateLegacyVendorFolder(Root);

        Assert.Empty(result.Moved);
        Assert.Empty(result.LeftInPlace);
        Assert.True(Directory.Exists(Current()));
    }
}
