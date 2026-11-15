using Brigid.Data;
using Xunit;

namespace Brigid.Tests;

// A settings file whose read failed was saved over with defaults (BRIG-177). GuardedFile refuses that save, keeps a file
// that does not parse in a .bad copy, and raises one notice for each file in a session, also for a write that fails.
// NotSaved is static, so each test counts only the events of its own folder.
[Collection("GuardedFileNotices")]
public sealed class GuardedFileTests : IDisposable
{
    private readonly string Folder = Path.Combine(Path.GetTempPath(), "brigid-guarded-" + Guid.NewGuid().ToString("N"));

    public GuardedFileTests() => Directory.CreateDirectory(Folder);

    public void Dispose() => Directory.Delete(Folder, true);

    private GuardedFile FileNamed(string name) => new(Path.Combine(Folder, name), "settings");

    private static string ReadText(GuardedFile file) => file.Read(File.ReadAllText, "empty");

    private static SaveResult SaveText(GuardedFile file, string text) => file.Save(path => File.WriteAllText(path, text));

    //runs the action and gives the saves of this test's folder that did not go through, as the events raised them
    private List<(GuardedFile File, SaveResult Result)> RefusalsDuring(Action action)
    {
        var refusals = new List<(GuardedFile, SaveResult)>();

        void OnNotSaved(GuardedFile file, SaveResult result)
        {
            if (file.Path.StartsWith(Folder, StringComparison.Ordinal))
                lock (refusals)
                    refusals.Add((file, result));
        }

        GuardedFile.NotSaved += OnNotSaved;

        try
        {
            action();
        } finally
        {
            GuardedFile.NotSaved -= OnNotSaved;
        }

        return refusals;
    }

    [Fact]
    public void ALockedFileReadsAsEmptyAndItsSaveIsRefused()
    {
        var file = FileNamed("keybinds.json");
        File.WriteAllText(file.Path, "good");

        using (new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal("empty", ReadText(file));

        Assert.True(file.IsUnreadable);
        Assert.Equal(SaveResult.Unreadable, SaveText(file, "defaults"));
        Assert.Equal("good", File.ReadAllText(file.Path));
    }

    [Fact]
    public void AMissingFileReadsAsEmptyAndSaves()
    {
        var file = FileNamed("brigid.cfg");

        Assert.Equal("empty", ReadText(file));
        Assert.False(file.IsUnreadable);
        Assert.Equal(SaveResult.Saved, SaveText(file, "defaults"));
        Assert.Equal("defaults", File.ReadAllText(file.Path));
    }

    //a file that was never read has no read state: a save of new settings goes through
    [Fact]
    public void AFileThatWasNotReadSaves()
    {
        var file = FileNamed("brigid.cfg");
        File.WriteAllText(file.Path, "good");

        Assert.Equal(SaveResult.Saved, SaveText(file, "new"));
    }

    [Fact]
    public void AReadThatSucceedsLaterAllowsTheSave()
    {
        var file = FileNamed("keybinds.json");
        File.WriteAllText(file.Path, "good");

        using (new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.None))
            ReadText(file);

        Assert.Equal("good", ReadText(file));
        Assert.False(file.IsUnreadable);
        Assert.Equal(SaveResult.Saved, SaveText(file, "new"));
    }

    //a lock that starts after the read: the client does not know that the file cannot be read, and each save fails
    [Fact]
    public void AWriteThatFailsIsFailedAndRaisesANotice()
    {
        var file = FileNamed("brigid.cfg");
        File.WriteAllText(file.Path, "good");

        SaveResult result = SaveResult.Saved;
        var refusals = RefusalsDuring(
            () =>
            {
                using (new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.None))
                    result = SaveText(file, "new");
            });

        Assert.Equal(SaveResult.Failed, result);
        Assert.Equal([(file.Path, SaveResult.Failed)], refusals.Select(r => (r.File.Path, r.Result)));
    }

    [Fact]
    public void ACorruptFileIsCopiedToBadAndItsSaveGoesThrough()
    {
        var file = FileNamed("keybinds.json");
        File.WriteAllText(file.Path, "{ broken");

        ReadText(file);
        file.MarkCorrupt("test");

        Assert.False(file.IsUnreadable);
        Assert.Equal("{ broken", File.ReadAllText(file.Path + ".bad"));
        Assert.Equal(SaveResult.Saved, SaveText(file, "defaults"));
        Assert.Equal("defaults", File.ReadAllText(file.Path));
        Assert.Equal("{ broken", File.ReadAllText(file.Path + ".bad"));
    }

    [Fact]
    public void TheBadCopyHoldsTheNewestCorruptFile()
    {
        var file = FileNamed("keybinds.json");
        File.WriteAllText(file.Path + ".bad", "older");
        File.WriteAllText(file.Path, "newer");

        file.MarkCorrupt("test");

        Assert.Equal("newer", File.ReadAllText(file.Path + ".bad"));
    }

    //with no copy, the file is the one copy of the player's contents, so its save is refused
    [Fact]
    public void ACorruptFileThatCannotBeCopiedRefusesItsSave()
    {
        var file = FileNamed("keybinds.json");
        File.WriteAllText(file.Path, "{ broken");
        Directory.CreateDirectory(file.Path + ".bad");

        file.MarkCorrupt("test");

        Assert.True(file.IsUnreadable);
        Assert.Equal(SaveResult.Unreadable, SaveText(file, "defaults"));
        Assert.Equal("{ broken", File.ReadAllText(file.Path));
    }

    //a refusal before the client listens would use up the one notice of the file
    [Fact]
    public void ARefusalWithNoListenerIsNotCounted()
    {
        var file = FileNamed("Macro.cfg");
        File.WriteAllText(file.Path, "good");

        using (new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ReadText(file);

            //the listeners of NotSaved are in the GuardedFileNotices collection, so nothing listens here
            SaveText(file, "x");

            var refusals = RefusalsDuring(() => SaveText(file, "x"));

            Assert.Single(refusals);
        }
    }

    [Fact]
    public void ARefusedSaveRaisesOneNoticeForEachFileInASession()
    {
        var first = FileNamed("Macro.cfg");
        var other = FileNamed("Friendlist.cfg");
        File.WriteAllText(first.Path, "good");
        File.WriteAllText(other.Path, "good");

        var refusals = RefusalsDuring(
            () =>
            {
                using (new FileStream(first.Path, FileMode.Open, FileAccess.Read, FileShare.None))
                using (new FileStream(other.Path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    ReadText(first);
                    ReadText(other);

                    //a new login makes a new GuardedFile for the same path
                    var again = FileNamed("Macro.cfg");
                    ReadText(again);

                    SaveText(first, "x");
                    SaveText(first, "x");
                    SaveText(again, "x");
                    SaveText(other, "x");
                }
            });

        Assert.Equal([(first.Path, SaveResult.Unreadable), (other.Path, SaveResult.Unreadable)], refusals.Select(r => (r.File.Path, r.Result)));
    }
}
