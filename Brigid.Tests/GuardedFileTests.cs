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

    //a save wrote the file in place, so a write that failed part way left a cut file that loaded as valid
    [Fact]
    public void AWriteThatFailsPartWayKeepsTheOldFile()
    {
        var file = FileNamed("Hotbar.cfg");
        File.WriteAllText(file.Path, "good");

        var result = file.Save(
            path =>
            {
                File.WriteAllText(path, "go");

                throw new IOException("the disk is full");
            });

        Assert.Equal(SaveResult.Failed, result);
        Assert.Equal("good", File.ReadAllText(file.Path));
        Assert.False(File.Exists(file.Path + GuardedFile.TEMP_SUFFIX));
    }

    //the trust store saves on the game loop and on the connect task; two saves shared the temporary file, and one could
    //delete the other's file before its move
    [Fact]
    public void TwoThreadsThatSaveOneFileBothSave()
    {
        var file = FileNamed("known-servers.json");
        var results = new List<SaveResult>();

        void SaveMany(string text)
        {
            for (var i = 0; i < 100; i++)
            {
                var result = SaveText(file, text);

                lock (results)
                    results.Add(result);
            }
        }

        Parallel.Invoke(() => SaveMany("a"), () => SaveMany("b"));

        Assert.All(results, result => Assert.Equal(SaveResult.Saved, result));
        Assert.Contains(File.ReadAllText(file.Path), new[] { "a", "b" });
        Assert.False(File.Exists(file.Path + GuardedFile.TEMP_SUFFIX));
    }

    //a virus scanner or the search indexer can open the new file for a moment just after the write
    [Fact]
    public void AMoveThatAnotherProgramBlocksForAMomentIsTriedAgain()
    {
        var file = FileNamed("Macro.cfg");
        File.WriteAllText(file.Path, "old");
        var held = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Delay(30).ContinueWith(_ => held.Dispose());

        var result = SaveText(file, "new");
        release.Wait();

        Assert.Equal(SaveResult.Saved, result);
        Assert.Equal("new", File.ReadAllText(file.Path));
    }

    //the move that a held file blocks for a moment fails only on Windows, so a move that fails two times stands in for it
    [Fact]
    public void AMoveThatFailsTwoTimesIsTriedAgainOnEveryPlatform()
    {
        var file = FileNamed("Macro.cfg");
        File.WriteAllText(file.Path, "old");
        var moves = 0;

        var result = file.Save(
            path => File.WriteAllText(path, "new"),
            (temp, target) =>
            {
                if (++moves < GuardedFile.MOVE_TRIES)
                    throw new IOException("The process cannot access the file because it is being used by another process.");

                File.Move(temp, target, true);
            });

        Assert.Equal(SaveResult.Saved, result);
        Assert.Equal(GuardedFile.MOVE_TRIES, moves);
        Assert.Equal("new", File.ReadAllText(file.Path));
    }

    [Fact]
    public void AMoveThatFailsAtEveryTryFailsTheSave()
    {
        var file = FileNamed("Macro.cfg");
        File.WriteAllText(file.Path, "old");
        var moves = 0;

        var result = file.Save(path => File.WriteAllText(path, "new"), (_, _) => throw new IOException($"held ({++moves})"));

        Assert.Equal(SaveResult.Failed, result);
        Assert.Equal(GuardedFile.MOVE_TRIES, moves);
        Assert.Equal("old", File.ReadAllText(file.Path));
        Assert.False(File.Exists(file.Path + GuardedFile.TEMP_SUFFIX));
    }

    //File.Exists is false when the check fails (a denied folder), and the read went on with the defaults: the next save
    //wrote them over the file. A folder in the place of the file is one such path: File.Exists does not see it, and the
    //read cannot open it
    [Fact]
    public void APathThatACheckCannotSeeIsUnreadableAndItsSaveIsRefused()
    {
        var file = FileNamed("keybinds.json");
        Directory.CreateDirectory(file.Path);

        Assert.Equal("empty", ReadText(file));

        Assert.True(file.IsUnreadable);
        Assert.False(file.WasMissing);
        Assert.Equal(SaveResult.Unreadable, SaveText(file, "defaults"));
    }

    //the read opens the file itself, so a reader that fails gives an unreadable file, also when no check saw the file
    [Fact]
    public void AReadThatIsDeniedIsUnreadable()
    {
        var file = FileNamed("keybinds.json");

        var text = file.Read<string>(_ => throw new UnauthorizedAccessException("denied"), "empty");

        Assert.Equal("empty", text);
        Assert.True(file.IsUnreadable);
        Assert.False(file.WasMissing);
    }

    [Fact]
    public void AMissingFolderIsMissing()
    {
        var file = new GuardedFile(Path.Combine(Folder, "gone", "Macro.cfg"), "macros");

        Assert.Equal("empty", ReadText(file));
        Assert.True(file.WasMissing);
        Assert.False(file.IsUnreadable);
    }

    [Fact]
    public void ARefusedFileRefusesItsSaves()
    {
        var file = FileNamed("brigid.cfg");
        ReadText(file);

        file.RefuseSavesUntilRead("the legacy file could not be read");

        Assert.Equal(SaveResult.Unreadable, SaveText(file, "defaults"));
        Assert.False(File.Exists(file.Path));
    }

    [Fact]
    public void ASaveReplacesTheFileAndLeavesNoTemporaryFile()
    {
        var file = FileNamed("Macro.cfg");
        File.WriteAllText(file.Path, "old");

        Assert.Equal(SaveResult.Saved, SaveText(file, "new"));
        Assert.Equal("new", File.ReadAllText(file.Path));
        Assert.False(File.Exists(file.Path + GuardedFile.TEMP_SUFFIX));
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
        Assert.True(file.WasMissing);
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

    //a save that fails after the read: the client does not know that the file cannot be written, and each save fails. A
    //lock does not block the move on Linux or macOS, so a folder in the place of the file makes the write fail there too
    [Fact]
    public void AWriteThatFailsIsFailedAndRaisesANotice()
    {
        var file = FileNamed("brigid.cfg");
        Directory.CreateDirectory(file.Path);

        SaveResult result = SaveResult.Saved;
        var refusals = RefusalsDuring(() => result = SaveText(file, "new"));

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
