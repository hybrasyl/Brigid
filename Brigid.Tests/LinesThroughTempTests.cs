using Brigid.Data.Repositories;
using Xunit;

namespace Brigid.Tests;

// Hotbar.cfg was written in place at each change, also at each wheel notch, so a crash or a write that failed part way
// left an empty or cut file that loaded as valid, and the bindings were lost. The new HUD files are written to a file
// beside the target, which then replaces it.
public sealed class LinesThroughTempTests : IDisposable
{
    private readonly string Folder = Directory.CreateTempSubdirectory("brigid-linestemp-").FullName;

    public void Dispose() => Directory.Delete(Folder, true);

    private static IEnumerable<string> FailsAfterOneLine()
    {
        yield return "version 2";

        throw new IOException("the disk is full");
    }

    [Fact]
    public void AWriteThatFailsPartWayKeepsTheOldFile()
    {
        var path = Path.Combine(Folder, "Hotbar.cfg");
        File.WriteAllLines(path, ["version 2", "page 3"]);

        Assert.False(LocalPlayerSettingsRepository.WriteLinesThroughTemp(path, FailsAfterOneLine(), "SaveHotbar"));

        Assert.Equal(["version 2", "page 3"], File.ReadAllLines(path));
        Assert.False(File.Exists(path + LocalPlayerSettingsRepository.TEMP_SUFFIX));
    }

    [Fact]
    public void TheHotbarAndThePinsSaveThroughATemporaryFile()
    {
        var settings = new LocalPlayerSettingsRepository();
        settings.UseFolderForTests(Folder);
        File.WriteAllLines(settings.HotbarPath, ["version 2", "page 3"]);
        File.WriteAllLines(settings.PersonalViewPath, ["Dex"]);

        Assert.False(settings.SaveHotbar(FailsAfterOneLine()));
        Assert.False(settings.SavePersonalViewPins(FailsAfterOneLine()));

        Assert.Equal(["version 2", "page 3"], File.ReadAllLines(settings.HotbarPath));
        Assert.Equal(["Dex"], File.ReadAllLines(settings.PersonalViewPath));
    }

    //a pin file that could not be read loaded as no pins, and the next toggle wrote over it
    [Fact]
    public void AFileThatCannotBeReadLoadsAsNullAndAnAbsentFileAsNoLines()
    {
        var settings = new LocalPlayerSettingsRepository();
        settings.UseFolderForTests(Folder);

        Assert.Equal([], settings.LoadPersonalViewPins());
        Assert.Equal([], settings.LoadHotbar());

        //a folder in the place of the file: the read fails on every platform
        Directory.CreateDirectory(settings.PersonalViewPath);
        Directory.CreateDirectory(settings.HotbarPath);

        Assert.Null(settings.LoadPersonalViewPins());
        Assert.Null(settings.LoadHotbar());
    }

    [Fact]
    public void AFileInAFolderThatIsAbsentLoadsAsNoLines()
        => Assert.Equal([], LocalPlayerSettingsRepository.ReadLinesOrNull(Path.Combine(Folder, "gone", "Hotbar.cfg"), "LoadHotbar"));

    [Fact]
    public void AWriteReplacesTheFile()
    {
        var path = Path.Combine(Folder, "Hotbar.cfg");
        File.WriteAllLines(path, ["old"]);

        Assert.True(LocalPlayerSettingsRepository.WriteLinesThroughTemp(path, ["new"], "SaveHotbar"));

        Assert.Equal(["new"], File.ReadAllLines(path));
        Assert.False(File.Exists(path + LocalPlayerSettingsRepository.TEMP_SUFFIX));
    }
}
