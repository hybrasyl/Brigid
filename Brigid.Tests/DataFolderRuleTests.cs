using Brigid.Data;
using Brigid.Data.Repositories;
using Xunit;

namespace Brigid.Tests;

// Two data-folder rules that had no test: a data folder needs both khanpal.dat and legend.dat, in any letter case, and
// the legacy character import does not follow a server-sent name out of the game folder (BRIG-181).
public sealed class DataFolderRuleTests : IDisposable
{
    private readonly string Folder = Path.Combine(Path.GetTempPath(), "brigid-datafolder-" + Guid.NewGuid().ToString("N"));

    public DataFolderRuleTests() => Directory.CreateDirectory(Folder);

    public void Dispose() => Directory.Delete(Folder, true);

    private void Touch(string name) => File.WriteAllBytes(Path.Combine(Folder, name), []);

    [Theory]
    [InlineData("khanpal.dat", "legend.dat", true)]
    [InlineData("KhanPal.DAT", "Legend.dat", true)]
    [InlineData("khanpal.dat", null, false)]
    [InlineData(null, "legend.dat", false)]
    [InlineData(null, null, false)]
    public void ADataFolderNeedsBothArchives(string? first, string? second, bool expected)
    {
        if (first is not null)
            Touch(first);

        if (second is not null)
            Touch(second);

        Assert.Equal(expected, DataContext.IsValidDataDirectory(Folder));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoPathIsNotADataFolder(string? path) => Assert.False(DataContext.IsValidDataDirectory(path));

    [Fact]
    public void AMissingFolderIsNotADataFolder() => Assert.False(DataContext.IsValidDataDirectory(Path.Combine(Folder, "missing")));

    [Fact]
    public void TheLegacyFolderIsADirectChildOfTheDataFolder()
        => Assert.Equal(Path.Combine(Folder, "Angelique"), LocalPlayerSettingsRepository.LegacyFolderFor(Folder, "Angelique"));

    [Theory]
    [InlineData("..")]
    [InlineData("../Angelique")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("")]
    public void ANameThatLeavesTheDataFolderDoesNotImport(string name)
        => Assert.Null(LocalPlayerSettingsRepository.LegacyFolderFor(Folder, name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoDataFolderDoesNotImport(string? dataPath)
        => Assert.Null(LocalPlayerSettingsRepository.LegacyFolderFor(dataPath, "Angelique"));
}
