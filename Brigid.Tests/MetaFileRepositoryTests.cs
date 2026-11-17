using Brigid.Data.Repositories;
using DALib.Data;
using Xunit;

namespace Brigid.Tests;

// BRIG-167: the repository read every file in the metafile folder, and a stale file that the prune could not delete (a
// read-only file, or one that another program holds) stayed in use, for example an old ItemInfoN that gave an item its
// old class or level. While the sync has given it the names of the last checksum list, the repository reads only those.
public sealed class MetaFileRepositoryTests : IDisposable
{
    private readonly string Root = Path.Combine(Path.GetTempPath(), "brigid-metarepo-" + Guid.NewGuid().ToString("N"));
    private readonly MetaFileRepository Repository;

    public MetaFileRepositoryTests()
    {
        Directory.CreateDirectory(Root);
        Repository = new MetaFileRepository(Root);

        //an item in a listed file and one in a stale file; the five properties are level, class, weight, category and
        //description
        WriteMetaFile("ItemInfo0", new MetaFileEntry("Stick", ["1", "0", "1", "Weapon", "A stick."]));
        WriteMetaFile("ItemInfo1", new MetaFileEntry("Old Sword", ["99", "1", "5", "Weapon", "Gone from the server."]));
        WriteMetaFile("SEvent1", new MetaFileEntry("Listed event"));
        WriteMetaFile("SEvent2", new MetaFileEntry("Stale event"));
        WriteMetaFile("NationDesc", new MetaFileEntry("Stale nation"));
    }

    public void Dispose() => Directory.Delete(Root, true);

    private void WriteMetaFile(string name, params MetaFileEntry[] entries)
    {
        var file = new MetaFile();

        foreach (var entry in entries)
            file.Add(entry);

        file.Save(Path.Combine(Root, name));
    }

    private void ListOnly(params string[] names) => Repository.SetListedNames(names);

    [Fact]
    public void WithNoListEveryFileIsRead()
    {
        Repository.BuildItemIndex();

        Assert.Equal(2, Repository.GetAll("SEvent").Count);
        Assert.NotNull(Repository.Get("NationDesc"));
        Assert.Equal(2, Repository.GetItemMetadata("Stick", "Old Sword").Count);
    }

    [Fact]
    public void GetAllReadsOnlyTheListedFiles()
    {
        ListOnly("ItemInfo0", "SEvent1");

        Assert.Equal("Listed event", Repository.GetAll("SEvent").Single().Single().Key);
    }

    [Fact]
    public void GetReadsOnlyAListedFile()
    {
        ListOnly("ItemInfo0", "SEvent1");

        Assert.Null(Repository.Get("NationDesc"));
        Assert.NotNull(Repository.Get("SEvent1"));
    }

    [Fact]
    public void TheItemIndexHoldsOnlyTheListedFiles()
    {
        ListOnly("ItemInfo0", "SEvent1");
        Repository.BuildItemIndex();

        Assert.Equal(["Stick"], Repository.GetItemMetadata("Stick", "Old Sword").Keys);
    }

    //the client asks for fixed names (Light, NPCIllust); a server that lists another case still gives the file, which the
    //sync writes under the listed name
    [Fact]
    public void AFixedNameFindsTheListedFileInAnyCase()
    {
        WriteMetaFile("npcillust", new MetaFileEntry("Listed in lower case"));
        ListOnly("npcillust");

        Assert.NotNull(Repository.Get("NPCIllust"));
    }

    //the prune compares names exactly, so a scan does too: on Linux a stale SEvent1 beside a listed sevent1 is not read
    [Fact]
    public void AScanComparesNamesExactly()
    {
        ListOnly("sevent1");

        Assert.Empty(Repository.GetAll("SEvent"));
    }

    //a world transfer gives a new list before its sync completes and builds the index again
    [Fact]
    public void AnIndexFromAnEarlierListDoesNotReadAFileThatTheNewListDrops()
    {
        ListOnly("ItemInfo0", "ItemInfo1");
        Repository.BuildItemIndex();
        ListOnly("ItemInfo0");

        Assert.Equal(["Stick"], Repository.GetItemMetadata("Stick", "Old Sword").Keys);
    }

    //a server change from Config forgets the list of the old server
    [Fact]
    public void AForgottenListReadsEveryFileAgain()
    {
        ListOnly("SEvent1");
        Repository.ForgetListedNames();

        Assert.Equal(2, Repository.GetAll("SEvent").Count);
    }
}
