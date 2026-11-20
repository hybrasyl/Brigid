using Brigid.Collections;
using Brigid.Data;
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

    private static void AddSkill(byte slot, string name) => WorldState.AddSkill(new AddSkillPacket { Slot = slot, Icon = 1, Name = name });

    private static void AddSpell(byte slot, string name)
        => WorldState.AddSpell(new AddSpellPacket { Slot = slot, Icon = 1, Name = name, UseType = default, Prompt = string.Empty, CastLines = 0 });

    [Fact]
    public void AReadAfterALockedSkillFileGivesEverySlotItsChant()
    {
        var path = WriteFile("SkillBook.cfg", "SkillbookUsed\nAssail\nSkill:hya\nKick\nSkill:kick\n");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
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
        AddSkill(1, "Assail");
        AddSpell(1, "Armachd");

        using (new FileStream(skills, FileMode.Open, FileAccess.Read, FileShare.None))
        using (new FileStream(spells, FileMode.Open, FileAccess.Read, FileShare.None))
            WorldState.ReloadChants();

        Assert.Equal("hya", WorldState.SkillBook.GetSlot(1).Chant);
        Assert.Equal("Armachd", WorldState.SpellBook.GetSlot(1).Chants?[0]);
    }

    //a spell name and its ten chant lines
    private static string SpellEntry(string name) => $"{name}\nSpell0:{name}\n" + string.Concat(Enumerable.Range(1, 9).Select(i => $"Spell{i}:\n"));

    [Fact]
    public void AReadAfterALockedSpellFileGivesEverySlotItsChants()
    {
        var path = WriteFile("SpellBook.cfg", "SpellbookUsed\n" + SpellEntry("Armachd") + SpellEntry("Puinsein"));

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            AddSpell(1, "Armachd");
            AddSpell(2, "Puinsein");
        }

        AddSpell(3, "Beag Ioc");

        Assert.Equal("Armachd", WorldState.SpellBook.GetSlot(1).Chants?[0]);
        Assert.Equal("Puinsein", WorldState.SpellBook.GetSlot(2).Chants?[0]);
    }
}
