using Brigid.Data;
using Brigid.Data.Models;
using Brigid.Data.Repositories;
using Xunit;

namespace Brigid.Tests;

// A per-character file that another program holds with no read sharing threw from a raw read: world entry stopped, a
// relog crashed, and an empty load was then saved over the good file. Each read is guarded now: it loads as empty, and
// the save of that file is refused until a read of it succeeds (BRIG-177). The client does not support a hand edit while
// it runs, so a save does not check the file's time.
public sealed class CharacterFileSafetyTests : IDisposable
{
    private readonly string Folder = Path.Combine(Path.GetTempPath(), "brigid-charfile-" + Guid.NewGuid().ToString("N"));
    private readonly LocalPlayerSettingsRepository Settings = new();

    public CharacterFileSafetyTests() => Settings.OpenForTests(Folder);

    public void Dispose() => Directory.Delete(Folder, true);

    private static readonly Dictionary<CharacterFile, string> FileNames = new()
    {
        [CharacterFile.FamilyList] = "Familylist.cfg",
        [CharacterFile.FriendList] = "Friendlist.cfg",
        [CharacterFile.Macros] = "Macro.cfg",
        [CharacterFile.SkillChants] = "SkillBook.cfg",
        [CharacterFile.SpellChants] = "SpellBook.cfg",
        [CharacterFile.ProfileText] = "profile.txt"
    };

    //good contents for each file, in the format that its loader reads
    private static readonly Dictionary<CharacterFile, string> GoodContents = new()
    {
        [CharacterFile.FamilyList] = "Mother\nFather\n",
        [CharacterFile.FriendList] = "Sabrael\nKedian\n",
        [CharacterFile.Macros] = "Macro1: \"hello\"\n",
        [CharacterFile.SkillChants] = "SkillbookUsed\nAssail\nSkill:Hya!\n",
        [CharacterFile.SpellChants] = "SpellbookUsed\nArmachd\nSpell0:Armachd\n",
        [CharacterFile.ProfileText] = "A priest of Glioca."
    };

    private string PathOf(CharacterFile file) => Path.Combine(Folder, FileNames[file]);

    private string WriteGood(CharacterFile file)
    {
        var path = PathOf(file);
        File.WriteAllText(path, GoodContents[file]);

        return path;
    }

    //loads the file and says whether the load found any of its contents
    private bool LoadFindsContents(CharacterFile file)
        => file switch
        {
            CharacterFile.FamilyList  => Settings.LoadFamilyList().Mother == "Mother",
            CharacterFile.FriendList  => Settings.LoadFriendList().Count > 0,
            CharacterFile.Macros      => Settings.LoadMacros()[0] == "hello",
            CharacterFile.SkillChants => Settings.LoadSkillChants().Count > 0,
            CharacterFile.SpellChants => Settings.LoadSpellChants().Count > 0,
            CharacterFile.ProfileText => Settings.LoadProfileText().Length > 0,
            _                         => throw new ArgumentOutOfRangeException(nameof(file))
        };

    //saves empty data, as a client that loaded nothing would
    private SaveResult SaveEmpty(CharacterFile file)
        => file switch
        {
            CharacterFile.FamilyList  => Settings.SaveFamilyList(new FamilyList()),
            CharacterFile.FriendList  => Settings.SaveFriendList([]),
            CharacterFile.Macros      => Settings.SaveMacros(new string[10]),
            CharacterFile.SkillChants => Settings.SaveSkillChants([]),
            CharacterFile.SpellChants => Settings.SaveSpellChants([]),
            CharacterFile.ProfileText => Settings.SaveProfileText(string.Empty),
            _                         => throw new ArgumentOutOfRangeException(nameof(file))
        };

    public static TheoryData<CharacterFile> AllFiles => new(Enum.GetValues<CharacterFile>());

    [Theory]
    [MemberData(nameof(AllFiles))]
    public void ALockedFileLoadsAsEmptyAndItsSaveIsRefused(CharacterFile file)
    {
        var path = WriteGood(file);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.False(LoadFindsContents(file));

        Assert.Equal(SaveResult.Unreadable, SaveEmpty(file));
        Assert.Equal(GoodContents[file], File.ReadAllText(path));
    }

    [Theory]
    [MemberData(nameof(AllFiles))]
    public void AReadThatSucceedsLaterAllowsTheSave(CharacterFile file)
    {
        var path = WriteGood(file);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            LoadFindsContents(file);

        Assert.True(LoadFindsContents(file));
        Assert.Equal(SaveResult.Saved, SaveEmpty(file));
    }

    [Theory]
    [MemberData(nameof(AllFiles))]
    public void ANewSessionReadsAFileThatWasLockedAgain(CharacterFile file)
    {
        var path = WriteGood(file);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            LoadFindsContents(file);

        Settings.OpenForTests(Folder);

        Assert.True(LoadFindsContents(file));
        Assert.Equal(SaveResult.Saved, SaveEmpty(file));
    }

    //the next character has its own files, so a file of the last character that could not be read does not block them
    [Theory]
    [MemberData(nameof(AllFiles))]
    public void ANewCharacterDoesNotTakeTheReadStateOfTheLastOne(CharacterFile file)
    {
        var path = WriteGood(file);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            LoadFindsContents(file);

        var otherFolder = Folder + "-other";

        try
        {
            Settings.OpenForTests(otherFolder);

            Assert.Equal(SaveResult.Saved, SaveEmpty(file));
        } finally
        {
            Directory.Delete(otherFolder, true);
        }
    }

    //a hand edit made while the client runs is written over at the next save
    [Theory]
    [MemberData(nameof(AllFiles))]
    public void AHandEditAfterTheReadDoesNotBlockTheSave(CharacterFile file)
    {
        var path = WriteGood(file);
        Assert.True(LoadFindsContents(file));

        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));

        Assert.Equal(SaveResult.Saved, SaveEmpty(file));
    }

    [Theory]
    [MemberData(nameof(AllFiles))]
    public void AMissingFileLoadsAsEmptyAndSaves(CharacterFile file)
    {
        Assert.False(LoadFindsContents(file));
        Assert.Equal(SaveResult.Saved, SaveEmpty(file));
        Assert.True(File.Exists(PathOf(file)));
    }

    //the caller keeps the portrait that it has when the file cannot be read
    [Fact]
    public void ALockedPortraitIsAFailedRead()
    {
        var path = Path.Combine(Folder, "Angelique.jpg");
        File.WriteAllBytes(path, [1, 2, 3]);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.False(Settings.TryLoadPortrait("Angelique", out _));

        Assert.True(Settings.TryLoadPortrait("Angelique", out var portrait));
        Assert.Equal([1, 2, 3], portrait);
    }

    [Fact]
    public void NoPortraitIsASuccessWithNoBytes()
    {
        Assert.True(Settings.TryLoadPortrait("Angelique", out var portrait));
        Assert.Empty(portrait);
    }
}
