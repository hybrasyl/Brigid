using Brigid.Collections;
using Brigid.Data;
using Brigid.Data.Models;
using Brigid.Data.Repositories;
using DALib.Networking.Packets.Server;
using Xunit;

namespace Brigid.Tests;

// While SkillBook.cfg was locked, the server filled the slots with no chant. A later read succeeded, but only the slot
// that started the read got its chant, and the next save wrote the other slots over the file with no chant. The first
// read that succeeds after a failed read now gives every occupied slot its chant.
[Collection("WorldState")]
public sealed class ChantReadAgainTests : IDisposable
{
    private readonly string Folder = Path.Combine(Path.GetTempPath(), "brigid-chantread-" + Guid.NewGuid().ToString("N"));
    private readonly LocalPlayerSettingsRepository Settings = new();

    public ChantReadAgainTests()
    {
        Settings.OpenForTests(Folder);
        DataContext.UseLocalPlayerSettingsForTests(Settings);
        WorldState.ResetAll();
    }

    public void Dispose()
    {
        WorldState.ResetAll();
        Directory.Delete(Folder, true);
    }

    private string WriteFile(string name, string contents)
    {
        var path = Path.Combine(Folder, name);
        File.WriteAllText(path, contents);

        return path;
    }

    //the world screen opens the character folder at the first display of the player
    private static void EnterWorld() => WorldState.ReloadChants();

    private static void AddSkill(byte slot, string name) => WorldState.AddSkill(new AddSkillPacket { Slot = slot, Icon = 1, Name = name });

    private static void AddSpell(byte slot, string name)
        => WorldState.AddSpell(new AddSpellPacket { Slot = slot, Icon = 1, Name = name, UseType = default, Prompt = string.Empty, CastLines = 0 });

    [Fact]
    public void AReadAfterALockedSkillFileGivesEverySlotItsChant()
    {
        var path = WriteFile("SkillBook.cfg", "SkillbookUsed\nAssail\nSkill:hya\nKick\nSkill:kick\n");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            EnterWorld();
            AddSkill(1, "Assail");
            AddSkill(2, "Kick");
        }

        AddSkill(3, "Cleave");

        Assert.Equal("hya", WorldState.SkillBook.GetSlot(1).Chant);
        Assert.Equal("kick", WorldState.SkillBook.GetSlot(2).Chant);
    }

    //the world screen reads the chants again at world entry; a file that cannot be read then gave no chants, and every
    //slot lost its chant
    [Fact]
    public void AReloadOfAFileThatCannotBeReadKeepsTheChants()
    {
        var skills = WriteFile("SkillBook.cfg", "SkillbookUsed\nAssail\nSkill:hya\n");
        var spells = WriteFile("SpellBook.cfg", "SpellbookUsed\n" + SpellEntry("Armachd"));
        EnterWorld();
        AddSkill(1, "Assail");
        AddSpell(1, "Armachd");

        using (new FileStream(skills, FileMode.Open, FileAccess.Read, FileShare.None))
        using (new FileStream(spells, FileMode.Open, FileAccess.Read, FileShare.None))
            WorldState.ReloadChants();

        Assert.Equal("hya", WorldState.SkillBook.GetSlot(1).Chant);
        Assert.Equal("Armachd", WorldState.SpellBook.GetSlot(1).Chants?[0]);
    }

    //at a relog to another character the add packets come before the new character's folder opens; a read then gave the
    //last character's chants, and a new character whose file could not be read kept them
    [Fact]
    public void ASlotGetsNoChantBeforeTheFolderOpens()
    {
        WriteFile("SkillBook.cfg", "SkillbookUsed\nAssail\nSkill:hya\n");

        AddSkill(1, "Assail");

        Assert.True(string.IsNullOrEmpty(WorldState.SkillBook.GetSlot(1).Chant));

        EnterWorld();

        Assert.Equal("hya", WorldState.SkillBook.GetSlot(1).Chant);
    }

    //a save whose write failed left the old file in the cache, so the next add of the ability put the old chant back, and
    //the next save wrote it over the typed one
    [Fact]
    public void AFailedSaveKeepsTheTypedChantForTheNextAdd()
    {
        WriteFile("SkillBook.cfg", "SkillbookUsed\nAssail\nSkill:hya\n");
        EnterWorld();

        WorldState.SkillChantsSaved([new SkillChantEntry { Name = "Assail", Chant = "typed" }], SaveResult.Failed);
        AddSkill(2, "Assail");

        Assert.Equal("typed", WorldState.SkillBook.GetSlot(2).Chant);
    }

    [Fact]
    public void AFailedSpellSaveKeepsTheTypedChantsForTheNextAdd()
    {
        WriteFile("SpellBook.cfg", "SpellbookUsed\n" + SpellEntry("Armachd"));
        EnterWorld();
        var typed = new SpellChantEntry { Name = "Armachd" };
        typed.Chants[0] = "typed";

        WorldState.SpellChantsSaved([typed], SaveResult.Failed);
        AddSpell(2, "Armachd");

        Assert.Equal("typed", WorldState.SpellBook.GetSlot(2).Chants?[0]);
    }

    //a save that was refused because the file could not be read changes no cache: the file wins at its next read
    [Fact]
    public void ARefusedSaveLeavesTheFileToWin()
    {
        WriteFile("SkillBook.cfg", "SkillbookUsed\nAssail\nSkill:hya\n");
        EnterWorld();

        WorldState.SkillChantsSaved([new SkillChantEntry { Name = "Assail", Chant = "typed" }], SaveResult.Unreadable);
        AddSkill(2, "Assail");

        Assert.Equal("hya", WorldState.SkillBook.GetSlot(2).Chant);
    }

    //a spell name and its ten chant lines
    private static string SpellEntry(string name) => $"{name}\nSpell0:{name}\n" + string.Concat(Enumerable.Range(1, 9).Select(i => $"Spell{i}:\n"));

    [Fact]
    public void AReadAfterALockedSpellFileGivesEverySlotItsChants()
    {
        var path = WriteFile("SpellBook.cfg", "SpellbookUsed\n" + SpellEntry("Armachd") + SpellEntry("Puinsein"));

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            EnterWorld();
            AddSpell(1, "Armachd");
            AddSpell(2, "Puinsein");
        }

        AddSpell(3, "Beag Ioc");

        Assert.Equal("Armachd", WorldState.SpellBook.GetSlot(1).Chants?[0]);
        Assert.Equal("Puinsein", WorldState.SpellBook.GetSlot(2).Chants?[0]);
    }
}
