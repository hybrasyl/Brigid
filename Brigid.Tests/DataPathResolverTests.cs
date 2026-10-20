using Brigid.Data;
using Xunit;

namespace Brigid.Tests;

// Dark Ages data directories do not agree on case (the installer writes Legend.dat, an unpacker can write legend.dat),
// and a case-sensitive Linux file system refuses the wrong one. On a file system that ignores case, the resolver keeps
// the path as given; on one that keeps case, it returns the spelling on disk.
public sealed class DataPathResolverTests : IDisposable
{
    private readonly string Root = Path.Combine(Path.GetTempPath(), "brigid-datapath-" + Guid.NewGuid().ToString("N"));
    private readonly bool CaseSensitive;

    public DataPathResolverTests()
    {
        Directory.CreateDirectory(Path.Combine(Root, "npc"));
        Directory.CreateDirectory(Path.Combine(Root, "Music"));
        File.WriteAllBytes(Path.Combine(Root, "Legend.dat"), [1]);
        File.WriteAllBytes(Path.Combine(Root, "cious.dat"), [1]);
        File.WriteAllBytes(Path.Combine(Root, "npc", "npcbase.dat"), [1]);
        File.WriteAllBytes(Path.Combine(Root, "Music", "12.mus"), [1]);

        CaseSensitive = !File.Exists(Path.Combine(Root, "LEGEND.DAT"));
    }

    public void Dispose() => Directory.Delete(Root, true);

    private string Expected(string asGiven, string onDisk) => Path.Combine(Root, CaseSensitive ? onDisk : asGiven);

    [Theory]
    [InlineData("Legend.dat", "Legend.dat")]
    [InlineData("legend.dat", "Legend.dat")]
    [InlineData("Cious.dat", "cious.dat")]
    [InlineData("CIOUS.DAT", "cious.dat")]
    public void Resolve_FindsARootArchiveWhateverItsCase(string asked, string onDisk)
    {
        var resolved = DataPathResolver.Resolve(Root, asked);

        Assert.Equal(Expected(asked, onDisk), resolved);
        Assert.True(File.Exists(resolved));
    }

    [Theory]
    [InlineData("npc", "Npcbase.dat")]
    [InlineData("NPC", "npcbase.dat")]
    public void Resolve_FindsEachSegmentWhateverItsCase(string folder, string file)
    {
        var resolved = DataPathResolver.Resolve(Root, folder, file);

        Assert.Equal(Expected(Path.Combine(folder, file), Path.Combine("npc", "npcbase.dat")), resolved);
        Assert.True(File.Exists(resolved));
    }

    [Fact]
    public void Resolve_FindsAFileInAFolderWhoseCaseDiffers()
        => Assert.True(File.Exists(DataPathResolver.Resolve(Root, "music", "12.mus")));

    [Fact]
    public void Resolve_KeepsAMissingFileNameAsGiven()
        => Assert.Equal("99.MUS", Path.GetFileName(DataPathResolver.Resolve(Root, "music", "99.MUS")));

    [Fact]
    public void Resolve_KeepsTheWholePathAsGivenUnderAMissingRoot()
    {
        var absent = Path.Combine(Root, "absent");

        Assert.Equal(Path.Combine(absent, "npc", "npcbase.dat"), DataPathResolver.Resolve(absent, "npc", "npcbase.dat"));
    }
}
