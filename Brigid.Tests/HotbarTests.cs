#region
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-146 phase 1: the hotbar model. An entry is a kind and a name, so it follows its ability or item to another slot
///     and keeps a level gain; an ability that leaves its book empties its entries after the wait of a swap; an item entry
///     stays dimmed while its item is gone and empties at the end of the session, unless the player wears it; the file reads
///     back what it wrote, and a load does not write it again.
/// </summary>
public class HotbarTests
{
    private long Now;
    private readonly SkillBook Skills = new();
    private readonly SpellBook Spells = new();
    private readonly Inventory Items = new();
    private readonly Equipment Worn = new();

    private Hotbar NewHotbar() => new(Skills, Spells, Items, Worn, () => Now);

    //a hotbar after the world entry has settled, as at the end of a session
    private Hotbar SettledHotbar(params string[] lines)
    {
        var hotbar = NewHotbar();
        hotbar.Load(lines);
        Now += Hotbar.SETTLE_MS;

        return hotbar;
    }

    private static readonly HotbarEntry Assail = new(HotbarKind.Skill, "Assail", 1);
    private static readonly HotbarEntry BeagIoc = new(HotbarKind.Spell, "beag ioc", 2);
    private static readonly HotbarEntry Potion = new(HotbarKind.Item, "Hemloch Potion", 10, DisplayColor.Default);
    private static readonly HotbarEntry Staff = new(HotbarKind.Item, "Holy Staff", 30, DisplayColor.Default);

    private void AddPotions(byte slot, uint count) => Items.SetSlot(slot, 10, DisplayColor.Default, "Hemloch Potion", true, count, 0, 0);

    [Fact]
    public void AnEntryFollowsItsAbilityAndItsItemToAnotherSlot()
    {
        var hotbar = NewHotbar();
        Skills.SetSlot(3, 1, "Assail");
        Spells.SetSlot(5, 2, "beag ioc (Lev:1/50)");
        AddPotions(7, 4);

        Assert.Equal((byte)3, hotbar.Resolve(Assail));
        Assert.Equal((byte)5, hotbar.Resolve(BeagIoc));
        Assert.Equal((byte)7, hotbar.Resolve(Potion));

        //a swap is a removal and an add
        Skills.ClearSlot(3);
        Skills.SetSlot(9, 1, "Assail");
        Items.ClearSlot(7);
        AddPotions(2, 4);

        Assert.Equal((byte)9, hotbar.Resolve(Assail));
        Assert.Equal((byte)2, hotbar.Resolve(Potion));
    }

    //the drops of the windows give an entry from a book or an inventory slot, with the icon that the slot shows
    [Fact]
    public void AnEntryComesFromABookOrAnInventorySlot()
    {
        var hotbar = NewHotbar();
        Skills.SetSlot(3, 1, "Assail");
        Spells.SetSlot(5, 2, "beag ioc (Lev:1/50)");
        AddPotions(7, 4);
        Items.SetSlot(8, 31, DisplayColor.Navy, "Cloak", false, 0, 0, 0);

        Assert.Equal(Assail, hotbar.EntryForSkill(3));
        Assert.Equal(BeagIoc, hotbar.EntryForSpell(5));
        Assert.Equal(Potion, hotbar.EntryForItem(7));
        Assert.Equal(new HotbarEntry(HotbarKind.Item, "Cloak", 31, DisplayColor.Navy), hotbar.EntryForItem(8));
        Assert.Null(hotbar.EntryForSkill(4));
        Assert.Null(hotbar.EntryForItem(9));
    }

    [Fact]
    public void ALevelGainKeepsTheEntry()
    {
        var hotbar = NewHotbar();
        Spells.SetSlot(5, 2, "beag ioc (Lev:1/50)");
        hotbar.Set(0, 0, BeagIoc);

        Spells.SetSlot(5, 2, "beag ioc (Lev:2/50)");
        Now += Hotbar.REMOVAL_WAIT_MS;
        hotbar.Tick(true);

        Assert.Equal(BeagIoc, hotbar.Get(0));
        Assert.Equal((byte)5, hotbar.Resolve(BeagIoc));
    }

    //the inventory keeps the name of the item; the count of a stack is only in the name that a tooltip shows
    [Fact]
    public void TheItemNameHasNoStackCount()
    {
        AddPotions(1, 4);
        Items.SetSlot(3, 12, DisplayColor.Default, "Sword", false, 0, 0, 0);

        Assert.Equal("Hemloch Potion", Items.GetSlot(1).Name);
        Assert.Equal("Hemloch Potion[ 4 ]", Items.GetSlot(1).NameWithCount);
        Assert.Equal("Sword", Items.GetSlot(3).NameWithCount);
    }

    //the slot shows the icon of the book or inventory slot that holds the entry now, else the icon that the entry keeps
    [Fact]
    public void TheStateGivesTheSlotTheIconTheCountAndTheCooldown()
    {
        var hotbar = NewHotbar();
        Skills.SetSlot(3, 7, "Assail");
        Skills.SetCooldown(3, 10);
        Items.SetSlot(4, 12, DisplayColor.Navy, "Hemloch Potion", true, 2, 0, 0);
        AddPotions(5, 3);

        Assert.Equal(new HotbarEntryState(3, 7, DisplayColor.Default, 0, 1f), hotbar.StateOf(Assail));
        Assert.Equal(new HotbarEntryState(4, 12, DisplayColor.Navy, 5, 0), hotbar.StateOf(Potion));
        Assert.Equal(new HotbarEntryState(null, 2, DisplayColor.Default, 0, 0), hotbar.StateOf(BeagIoc));
        Assert.True(hotbar.StateOf(BeagIoc).IsGone);
    }

    [Fact]
    public void TheCountIsTheSumOfTheStacks()
    {
        var hotbar = NewHotbar();
        AddPotions(1, 4);
        AddPotions(8, 6);
        Items.SetSlot(9, 11, DisplayColor.Default, "Hemloch Potion", false, 0, 0, 0);

        Assert.Equal(11u, hotbar.CountOf("hemloch potion"));
        Assert.Equal(0u, hotbar.CountOf("Apple"));
    }

    //the tick ran while the "Connection lost." popup showed, and deleted an ability whose add could not come any more
    [Fact]
    public void WithNoWorldConnectionARemovalEmptiesNothingAndWaitsForTheConnection()
    {
        var hotbar = NewHotbar();
        Skills.SetSlot(3, 1, "assail");
        hotbar.Set(0, 0, Assail);
        var changes = 0;
        hotbar.Changed += () => changes++;

        Skills.ClearSlot(3);
        Now += Hotbar.REMOVAL_WAIT_MS;
        hotbar.Tick(false);

        Assert.Equal(Assail, hotbar.Get(0, 0));
        Assert.Equal(0, changes);

        //the add of the swap arrives after the connection is back
        Skills.SetSlot(5, 1, "assail");
        hotbar.Tick(true);
        Assert.Equal(Assail, hotbar.Get(0, 0));
    }

    //a forgotten ability leaves the book and does not come back
    [Fact]
    public void AnAbilityThatLeavesTheBookEmptiesItsEntriesAfterTheWait()
    {
        var hotbar = NewHotbar();
        Skills.SetSlot(3, 1, "assail");
        hotbar.Set(0, 0, Assail);
        hotbar.Set(4, 11, Assail);
        var changes = 0;
        hotbar.Changed += () => changes++;

        Skills.ClearSlot(3);
        Now += Hotbar.REMOVAL_WAIT_MS - 1;
        hotbar.Tick(true);
        Assert.Equal(Assail, hotbar.Get(0, 0));

        Now += 1;
        hotbar.Tick(true);

        Assert.Null(hotbar.Get(0, 0));
        Assert.Null(hotbar.Get(4, 11));
        Assert.Equal(1, changes);
    }

    //a skill and a spell can have the same name; they are two entries
    [Fact]
    public void ASkillThatLeavesTheBookKeepsTheSpellOfTheSameName()
    {
        var hotbar = NewHotbar();
        var spell = new HotbarEntry(HotbarKind.Spell, "Assail");
        Skills.SetSlot(3, 1, "Assail");
        Spells.SetSlot(3, 1, "Assail");
        hotbar.Set(0, 0, Assail);
        hotbar.Set(0, 1, spell);

        Skills.ClearSlot(3);
        Now += Hotbar.REMOVAL_WAIT_MS;
        hotbar.Tick(true);

        Assert.Null(hotbar.Get(0));
        Assert.Equal(spell, hotbar.Get(1));
    }

    //the server sends a swap in the book as a removal and an add
    [Fact]
    public void ASwapInTheBookEmptiesNothing()
    {
        var hotbar = NewHotbar();
        Skills.SetSlot(3, 1, "Assail");
        hotbar.Set(0, 0, Assail);

        Skills.ClearSlot(3);
        Skills.SetSlot(9, 1, "Assail");
        Now += Hotbar.REMOVAL_WAIT_MS;
        hotbar.Tick(true);

        Assert.Equal(Assail, hotbar.Get(0));
    }

    //the world entry fills the book slot by slot, and a logout clears it: neither is an ability that leaves
    [Fact]
    public void TheWorldEntryAndAClearOfTheBookEmptyNothing()
    {
        var hotbar = NewHotbar();
        hotbar.Load(["0 0 skill 1 0 Assail"]);
        Now += Hotbar.REMOVAL_WAIT_MS;
        hotbar.Tick(true);
        Assert.Equal(Assail, hotbar.Get(0));

        Skills.SetSlot(3, 1, "Assail");
        Skills.Clear();
        Now += Hotbar.REMOVAL_WAIT_MS;
        hotbar.Tick(true);

        Assert.Equal(Assail, hotbar.Get(0));
    }

    //after a clear, the next world entry can put another ability in an old slot of Assail before Assail arrives: that is
    //not Assail leaving the book
    [Fact]
    public void AClearForgetsWhatTheSlotsHeld()
    {
        var hotbar = NewHotbar();
        Skills.SetSlot(3, 1, "Assail");
        hotbar.Set(0, 0, Assail);

        Skills.Clear();
        Skills.SetSlot(3, 2, "Charge");
        Now += Hotbar.REMOVAL_WAIT_MS;
        hotbar.Tick(true);

        Assert.Equal(Assail, hotbar.Get(0));
    }

    [Fact]
    public void AResetForgetsTheAbilitiesThatLeft()
    {
        var hotbar = NewHotbar();
        Skills.SetSlot(3, 1, "Assail");
        Skills.ClearSlot(3);

        hotbar.Reset();
        hotbar.Load(["0 0 skill 1 0 Assail"]);
        Now += Hotbar.REMOVAL_WAIT_MS;
        hotbar.Tick(true);

        Assert.Equal(Assail, hotbar.Get(0));
    }

    //Sabrael, 2026-11-18: the slot of a gone item shows dimmed until the next login, or until another entry goes there
    [Fact]
    public void AnItemEntryStaysWhileItsItemIsGoneAndEmptiesAtTheEndOfTheSession()
    {
        var hotbar = SettledHotbar();
        AddPotions(7, 1);
        hotbar.Set(0, 0, Potion);

        Items.ClearSlot(7);
        Now += Hotbar.REMOVAL_WAIT_MS;
        hotbar.Tick(true);
        Assert.Equal(Potion, hotbar.Get(0));
        Assert.Null(hotbar.Resolve(Potion));

        //a new stack lights the slot again
        AddPotions(4, 3);
        Assert.Equal((byte)4, hotbar.Resolve(Potion));

        Items.ClearSlot(4);
        hotbar.DropGoneEntries();

        Assert.Null(hotbar.Get(0));
    }

    //a weapon swap: the staff that the player wears at the logout is not gone
    [Fact]
    public void TheEndOfTheSessionKeepsAnItemThatThePlayerWears()
    {
        var hotbar = SettledHotbar("0 0 item 30 0 Holy Staff");
        Worn.SetSlot(EquipmentSlot.Weapon, 30, DisplayColor.Default, "Holy Staff", 0, 0);

        hotbar.DropGoneEntries();

        Assert.Equal(Staff, hotbar.Get(0));
    }

    //an ability that left the book while the player was away is not in the book at the world entry
    [Fact]
    public void TheEndOfTheSessionEmptiesAnAbilityThatIsNotInTheBook()
    {
        var hotbar = SettledHotbar("0 0 skill 1 0 Assail", "0 1 spell 2 0 beag ioc");
        Spells.SetSlot(5, 2, "beag ioc (Lev:1/50)");

        hotbar.DropGoneEntries();

        Assert.Null(hotbar.Get(0));
        Assert.Equal(BeagIoc, hotbar.Get(1));
    }

    [Fact]
    public void TheEndOfTheSessionKeepsWhatIsThereAndRaisesNoChange()
    {
        var hotbar = SettledHotbar("0 0 item 10 0 Hemloch Potion");
        AddPotions(1, 2);
        var changes = 0;
        hotbar.Changed += () => changes++;

        hotbar.DropGoneEntries();

        Assert.Equal(Potion, hotbar.Get(0));
        Assert.Equal(0, changes);
    }

    //Sabrael, 2026-11-18: the Hybrasyl servers share the file; an entry that only the other server has stays, dimmed. The
    //file named one server, the last that saved it, so the second session on the other server swept the entry
    [Fact]
    public void AnEntryOfAnotherServerStaysForEverySessionOnThisServer()
    {
        var text = new[] { "version 1", "server prod.example/Hybrasyl", "0 0 skill 1 0 Assail" };

        for (var session = 0; session < 3; session++)
        {
            var hotbar = NewHotbar();
            hotbar.Load(text, "qa.example/Hybrasyl");
            Now += Hotbar.SETTLE_MS;

            hotbar.SetPage(session + 1);
            hotbar.DropGoneEntries();

            Assert.Equal(Assail, hotbar.Get(0, 0));
            Assert.Equal("prod.example/Hybrasyl", hotbar.OwnerOf(0, 0));
            text = [.. hotbar.Format()];
        }

        //back on the first server, the entry is that server's, and its sweep empties it
        var home = NewHotbar();
        home.Load(text, "PROD.example/Hybrasyl");
        Now += Hotbar.SETTLE_MS;
        home.DropGoneEntries();

        Assert.Null(home.Get(0, 0));
    }

    //an entry that the player puts in a slot is this server's, and the sweep of this server empties it
    [Fact]
    public void AnEntryPutInASlotIsOfThisSessionsServer()
    {
        var hotbar = NewHotbar();
        hotbar.Load(["version 2", "server prod.example/Hybrasyl", "0 0 skill 1 0 Assail"], "qa.example/Hybrasyl");
        Now += Hotbar.SETTLE_MS;
        hotbar.Set(0, 1, BeagIoc);
        hotbar.Move(0, 1, 2, 5);

        Assert.Equal("qa.example/Hybrasyl", hotbar.OwnerOf(2, 5));
        Assert.Equal(
            ["version 2", "page 0", "server prod.example/Hybrasyl", "0 0 skill 1 0 Assail", "server qa.example/Hybrasyl", "2 5 spell 2 0 beag ioc"],
            hotbar.Format());

        hotbar.DropGoneEntries();

        Assert.Equal(Assail, hotbar.Get(0, 0));
        Assert.Null(hotbar.Get(2, 5));
    }

    //the removal wait of an ability that leaves the book empties only the entries of this server
    [Fact]
    public void ARemovalOnThisServerLeavesTheEntryOfAnotherServer()
    {
        var hotbar = NewHotbar();
        Skills.SetSlot(1, 1, "Assail");
        hotbar.Load(["version 2", "server prod.example/Hybrasyl", "0 0 skill 1 0 Assail"], "qa.example/Hybrasyl");
        hotbar.Set(0, 1, Assail);

        Skills.ClearSlot(1);
        Now += Hotbar.REMOVAL_WAIT_MS;
        hotbar.Tick(true);

        Assert.Equal(Assail, hotbar.Get(0, 0));
        Assert.Null(hotbar.Get(0, 1));
    }

    //a file of version 1 gave each entry the server that saved it last; the player who puts the same entry in its slot
    //again on this server makes it this server's
    [Fact]
    public void TheSameEntryPutAgainTakesThisServer()
    {
        var hotbar = NewHotbar();
        hotbar.Load(["version 1", "server prod.example/Hybrasyl", "0 0 skill 1 0 Assail"], "qa.example/Hybrasyl");
        var changes = 0;
        hotbar.Changed += () => changes++;

        hotbar.Set(0, 0, Assail);
        hotbar.Set(0, 0, Assail);

        Assert.Equal("qa.example/Hybrasyl", hotbar.OwnerOf(0, 0));
        Assert.Equal(1, changes);
    }

    //a name with spaces at its ends was trimmed at the load, so it did not resolve and was swept
    [Fact]
    public void ANameKeepsItsSpaces()
    {
        var hotbar = NewHotbar();
        var spaced = new HotbarEntry(HotbarKind.Item, " Old  Coat ", 4);
        hotbar.Set(0, 0, spaced);

        var copy = NewHotbar();
        copy.Load(hotbar.Format());

        Assert.Equal(spaced, copy.Get(0, 0));
    }

    //a file that names no server (and a session whose server is not known) sweeps as before
    [Fact]
    public void AFileThatNamesNoServerSweeps()
    {
        var hotbar = NewHotbar();
        hotbar.Load(["0 0 skill 1 0 Assail"], "qa.example/Hybrasyl");
        Now += Hotbar.SETTLE_MS;

        hotbar.DropGoneEntries();

        Assert.Null(hotbar.Get(0));
        Assert.DoesNotContain(NewHotbar().Format(), line => line.StartsWith("server", StringComparison.Ordinal));
    }

    //a session that ends before the world entry settles must not empty the entries whose ability or item has not arrived
    [Fact]
    public void TheEndOfASessionBeforeTheWorldEntrySettlesEmptiesNothing()
    {
        var hotbar = NewHotbar();
        hotbar.Load(["0 0 item 10 0 Hemloch Potion", "0 1 skill 1 0 Assail"]);
        Now += Hotbar.SETTLE_MS - 1;

        hotbar.DropGoneEntries();

        Assert.Equal(Potion, hotbar.Get(0));
        Assert.Equal(Assail, hotbar.Get(1));

        //after a reset, the hotbar is not loaded until the next world entry
        Now += Hotbar.SETTLE_MS;
        hotbar.Reset();
        hotbar.Set(0, 0, Potion);
        hotbar.DropGoneEntries();
        Assert.Equal(Potion, hotbar.Get(0));
    }

    [Fact]
    public void ASetAMoveAndAClearUseTheirPage()
    {
        var hotbar = NewHotbar();
        hotbar.Set(2, 0, Assail);
        hotbar.Set(2, 5, Potion);

        hotbar.Move(2, 0, 2, 5);
        Assert.Equal((Potion, Assail), (hotbar.Get(2, 0), hotbar.Get(2, 5)));

        //a drag can end on another page, when the wheel turns the page during the drag
        hotbar.Move(2, 0, 7, 3);
        Assert.Equal((null, Potion), (hotbar.Get(2, 0), hotbar.Get(7, 3)));

        hotbar.Clear(2, 5);
        Assert.Null(hotbar.Get(2, 5));

        //a drop on a filled slot replaces its entry
        hotbar.Set(7, 3, BeagIoc);
        Assert.Equal(BeagIoc, hotbar.Get(7, 3));
        Assert.Equal(0, hotbar.Page);
    }

    [Fact]
    public void AnEntryWithNoNameIsRefused()
    {
        var hotbar = NewHotbar();

        hotbar.Set(0, 0, new HotbarEntry(HotbarKind.Item, " "));

        Assert.Null(hotbar.Get(0));
    }

    [Fact]
    public void ThePageWraps()
    {
        var hotbar = NewHotbar();

        hotbar.SetPage(-1);
        Assert.Equal(Hotbar.PAGES - 1, hotbar.Page);

        hotbar.SetPage(Hotbar.PAGES);
        Assert.Equal(0, hotbar.Page);
    }

    [Fact]
    public void TheFileReadsBackWhatItWrote()
    {
        var hotbar = NewHotbar();
        var cloak = new HotbarEntry(HotbarKind.Item, "Cloak of the Fae", 31, DisplayColor.Navy);
        hotbar.Set(3, 0, Assail);
        hotbar.Set(3, 11, cloak);
        hotbar.Set(9, 4, BeagIoc);
        hotbar.SetPage(9);

        var lines = hotbar.Format().ToArray();
        var copy = NewHotbar();
        copy.Load(lines);

        Assert.Equal($"version {Hotbar.FILE_VERSION}", lines[0]);
        Assert.Equal(9, copy.Page);
        Assert.Equal(Assail, copy.Get(3, 0));
        Assert.Equal(cloak, copy.Get(3, 11));
        Assert.Equal(BeagIoc, copy.Get(9, 4));
        Assert.Equal(lines, copy.Format());
    }

    //a load at the world entry must not write the file again: a file that cannot be read would become an empty hotbar
    [Fact]
    public void ALoadRaisesNoChange()
    {
        var hotbar = NewHotbar();
        var changes = 0;
        hotbar.Changed += () => changes++;

        hotbar.Load(["page 3", "0 0 skill 1 0 Assail"]);
        hotbar.Load([]);

        Assert.Equal(0, changes);
    }

    //a file that a newer client wrote (emote entries, for example) loads nothing, and the caller does not write over it
    [Fact]
    public void AFileOfANewerVersionLoadsNothing()
    {
        var hotbar = NewHotbar();

        Assert.False(hotbar.Load([$"version {Hotbar.FILE_VERSION + 1}", "page 3", "0 0 skill 1 0 Assail"]));
        Assert.Null(hotbar.Get(0, 0));
        Assert.Equal(0, hotbar.Page);

        Assert.False(hotbar.Load(["version x", "0 0 skill 1 0 Assail"]));
        Assert.True(hotbar.Load(["version 1", "0 0 skill 1 0 Assail"]));
        Assert.True(hotbar.Load(["0 0 skill 1 0 Assail"]));
        Assert.Equal(Assail, hotbar.Get(0, 0));
    }

    [Fact]
    public void ABadLineIsSkipped()
    {
        var hotbar = NewHotbar();

        hotbar.Load(
        [
            "version 1", "0 0 skill 1 0 Assail", "10 0 skill 1 0 Assail", "0 12 skill 1 0 Assail", "0 1 emote 1 0 Wave", "0 2 1 1 0 Assail",
            "0 3 Skill 1 0 Assail", "0 4 skill,item 1 0 Assail", "0 5 skill 70000 0 Assail", "0 6 skill 1 256 Assail", "-1 7 skill 1 0 Assail",
            "0 8 skill 1 0", "page 10", "garbage"
        ]);

        Assert.Equal(Assail, hotbar.Get(0, 0));
        Assert.Equal(0, hotbar.Page);
        Assert.Equal([$"version {Hotbar.FILE_VERSION}", "page 0", "0 0 skill 1 0 Assail"], hotbar.Format());
    }

    [Fact]
    public void AResetEmptiesEverything()
    {
        var hotbar = NewHotbar();
        hotbar.Set(3, 0, Assail);
        hotbar.SetPage(3);

        hotbar.Reset();

        Assert.Equal(0, hotbar.Page);
        Assert.Null(hotbar.Get(3, 0));
    }
}
