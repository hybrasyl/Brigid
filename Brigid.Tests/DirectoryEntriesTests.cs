using Brigid.Data;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     BRIG-111: one match of a directory entry by name, with the case rule and the entry kind of each caller.
/// </summary>
public sealed class DirectoryEntriesTests : IDisposable
{
    private readonly string Root = Path.Combine(Path.GetTempPath(), "brigid-entries-" + Guid.NewGuid().ToString("N"));

    public DirectoryEntriesTests()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllBytes(Path.Combine(Root, "Legend.dat"), [1]);
        Directory.CreateDirectory(Path.Combine(Root, "Erisco"));
    }

    public void Dispose() => Directory.Delete(Root, true);

    [Fact]
    public void NoLetterCase_FindsTheNameOnDisk()
    {
        Assert.Equal("Legend.dat", DirectoryEntries.FindName(Root, "legend.dat", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Erisco", DirectoryEntries.FindName(Root, "ERISCO", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExactCase_FindsOnlyTheSameName()
    {
        Assert.Equal("Erisco", DirectoryEntries.FindName(Root, "Erisco", StringComparison.Ordinal));
        Assert.Null(DirectoryEntries.FindName(Root, "erisco", StringComparison.Ordinal));
    }

    [Fact]
    public void TheKindLimitsTheEntries()
    {
        Assert.Equal(
            "Legend.dat",
            DirectoryEntries.FindName(Root, "legend.dat", StringComparison.OrdinalIgnoreCase, DirectoryEntryKind.Files));
        Assert.Null(
            DirectoryEntries.FindName(Root, "legend.dat", StringComparison.OrdinalIgnoreCase, DirectoryEntryKind.Directories));
        Assert.Equal(
            "Erisco",
            DirectoryEntries.FindName(Root, "erisco", StringComparison.OrdinalIgnoreCase, DirectoryEntryKind.Directories));
        Assert.Null(DirectoryEntries.FindName(Root, "Erisco", StringComparison.Ordinal, DirectoryEntryKind.Files));
    }

    [Fact]
    public void AMissingDirectoryOrNameGivesNull()
    {
        Assert.Null(DirectoryEntries.FindName(Path.Combine(Root, "absent"), "x", StringComparison.OrdinalIgnoreCase));
        Assert.Null(DirectoryEntries.FindName(Root, "khanpal.dat", StringComparison.OrdinalIgnoreCase));
    }

    //a case-sensitive file system can hold both; the first in ordinal order wins, whatever the order of the listing
    [Fact]
    public void TwoMatchesGiveTheFirstInOrdinalOrder()
    {
        File.WriteAllBytes(Path.Combine(Root, "legend.dat"), [2]);

        var names = Directory.GetFiles(Root).Select(Path.GetFileName).ToArray();

        //Windows and macOS (by default) do not tell the two names apart, so there is one file
        if (names.Count(n => string.Equals(n, "legend.dat", StringComparison.OrdinalIgnoreCase)) < 2)
            return;

        Assert.Equal("Legend.dat", DirectoryEntries.FindName(Root, "LEGEND.DAT", StringComparison.OrdinalIgnoreCase));
    }
}
