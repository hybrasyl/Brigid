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

    //the sync completion builds the index from the game loop, outside the packet handler's catch: a folder that cannot
    //be listed gives an empty index and does not throw
    [Fact]
    public void AFolderThatCannotBeListedGivesAnEmptyIndex()
    {
        using var denied = DenyListing(Root);

        if (!denied.Works)
            return;

        Repository.BuildItemIndex();

        Assert.Empty(Repository.GetItemMetadata("Stick"));
    }

    private static ListingDenied DenyListing(string directory) => new(directory);

    //refuses the listing of a folder to this process; Works is false when the OS still lists it (a root user)
    private sealed class ListingDenied : IDisposable
    {
        private readonly string Directory;
        private readonly System.Security.AccessControl.FileSystemAccessRule? Rule;

        public bool Works { get; }

        public ListingDenied(string directory)
        {
            Directory = directory;

            if (OperatingSystem.IsWindows())
            {
                var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
                Rule = new System.Security.AccessControl.FileSystemAccessRule(
                    user,
                    System.Security.AccessControl.FileSystemRights.ListDirectory,
                    System.Security.AccessControl.AccessControlType.Deny);
                var info = new DirectoryInfo(directory);
                var security = info.GetAccessControl();
                security.AddAccessRule(Rule);
                info.SetAccessControl(security);
            } else
                File.SetUnixFileMode(directory, UnixFileMode.UserExecute);

            Works = !CanList(directory);
        }

        private static bool CanList(string directory)
        {
            try
            {
                System.IO.Directory.GetFiles(directory);

                return true;
            } catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (OperatingSystem.IsWindows())
            {
                var info = new DirectoryInfo(Directory);
                var security = info.GetAccessControl();
                security.RemoveAccessRule(Rule!);
                info.SetAccessControl(security);
            } else
                File.SetUnixFileMode(Directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
