#region
using System.Globalization;
using Chaos.DarkAges.Definitions;
using Chaos.Extensions.Common;
#endregion

namespace Brigid.ViewModel;

/// <summary>What a hotbar entry uses: a skill, a spell or an item.</summary>
public enum HotbarKind
{
    Skill,
    Spell,
    Item
}

/// <summary>
///     An entry of the hotbar (BRIG-146): a kind and a name, which are its key, and the icon that the slot shows also while
///     the ability or the item is gone. The name of an ability is its name without the level text, so a level gain keeps
///     the entry; the name of an item is its name without the stack count, so two stacks of one item are one entry.
/// </summary>
public sealed record HotbarEntry(HotbarKind Kind, string Name, ushort Sprite = 0, DisplayColor Color = DisplayColor.Default)
{
    /// <summary>True when <paramref name="other" /> has the same key: the same kind, and the same name in any case.</summary>
    public bool SameKey(HotbarEntry other) => (Kind == other.Kind) && Name.EqualsI(other.Name);
}

/// <summary>What the slot of an entry shows now (<see cref="Hotbar.StateOf" />).</summary>
/// <param name="Slot">The book or inventory slot that holds the entry, or null while it is gone.</param>
/// <param name="Sprite">The icon sprite.</param>
/// <param name="Color">The colour of an item icon.</param>
/// <param name="Count">The number of an item; 0 for an ability.</param>
/// <param name="Cooldown">The cooldown of an ability, 0 = ready and 1 = just started; 0 for an item.</param>
public readonly record struct HotbarEntryState(byte? Slot, ushort Sprite, DisplayColor Color, uint Count, float Cooldown)
{
    /// <summary>True while the book or the inventory does not have the entry: its slot shows dimmed.</summary>
    public bool IsGone => Slot is null;
}

/// <summary>
///     The hotbar of the new HUD (BRIG-146, D4, D14): <see cref="PAGES" /> pages of <see cref="SLOTS" /> entries, and the
///     page that shows. An entry keeps a kind and a name, not a slot, because the server moves an ability or an item to
///     another slot on a swap; <see cref="Resolve" /> gives the slot that holds it now.
/// </summary>
/// <remarks>
///     <para>
///         An ability that leaves its book empties its entries. The server sends a swap as a removal and an add, so the
///         hotbar waits <see cref="REMOVAL_WAIT_MS" /> after a removal, and empties the entries only when the ability is
///         still gone (<see cref="Tick" />). A clear of the book (a logout) empties nothing.
///     </para>
///     <para>
///         An item entry stays while the inventory has none of the item, and its slot shows dimmed (Sabrael, 2026-11-18).
///         At the end of the session, <see cref="DropGoneEntries" /> empties such entries, so a dimmed slot lasts until the
///         next login; an item that the player wears is not gone. The same sweep empties an ability entry whose ability is
///         not in the book, for an ability that left the book while the player was away. A session that ends without a
///         clean unload (a crash) skips the sweep, and the dimmed slot lasts one more session.
///     </para>
///     <para>
///         The Hybrasyl servers (QA, production) share one file for a character name, because the per-character files
///         have one folder for every Hybrasyl host. So each entry keeps the server of the session that put it in its slot,
///         and the sweep and the removal wait empty only the entries of this session's server: an entry that only the
///         other server has stays, dimmed, for as many sessions as the player has there (Sabrael, 2026-11-18).
///     </para>
/// </remarks>
public sealed class Hotbar
{
    public const int PAGES = 10;
    public const int SLOTS = 12;
    public const int REMOVAL_WAIT_MS = 1000;

    /// <summary>
    ///     The time after the load of the hotbar, at the world entry, after which the books and the inventory are settled:
    ///     the server sends them slot by slot at the world entry, and sends no slot for an empty inventory.
    /// </summary>
    public const int SETTLE_MS = 5000;

    /// <summary>
    ///     The version of the hotbar file. Version 2 gives a <c>server</c> line before the entries of each server; version 1
    ///     had one <c>server</c> line, the server that saved the file, and it reads as the server of each entry. A client of
    ///     version 1 does not read a file of version 2, so it cannot sweep the entries of another server.
    /// </summary>
    public const int FILE_VERSION = 2;

    private const string VERSION_KEY = "version";
    private const string PAGE_KEY = "page";
    private const string SERVER_KEY = "server";

    //each entry, with the server of the session that put it in its slot (null when that server was not known)
    private readonly HotbarCell?[,] Cells = new HotbarCell?[PAGES, SLOTS];
    private readonly SkillBook Skills;
    private readonly SpellBook Spells;
    private readonly Inventory Items;
    private readonly Equipment Worn;
    private readonly Func<long> Clock;

    //the ability name of each book slot as the hotbar last saw it, so a cleared slot gives the name that left
    private readonly string?[] SkillNames = new string?[SkillBook.MAX_SLOTS];
    private readonly string?[] SpellNames = new string?[SpellBook.MAX_SLOTS];

    //the abilities that left a slot, and when; the entries empty when the ability is still gone after the wait
    private readonly Dictionary<(HotbarKind Kind, string Name), long> Removals = [];

    //the slot and the count of each entry key, as a lookup found them; a change of a book or the inventory clears it, so
    //the hotbar does not search the books by name at each frame
    private readonly Dictionary<(HotbarKind Kind, string Name), (byte? Slot, uint Count)> Found = new(KeyComparer.Instance);

    private long? LoadedAt;

    //the server of this session (its host and name), or null when it is not known
    private string? Server;

    /// <param name="clock">The time in milliseconds; null for <see cref="Environment.TickCount64" />.</param>
    public Hotbar(SkillBook skills, SpellBook spells, Inventory items, Equipment worn, Func<long>? clock = null)
    {
        Skills = skills;
        Spells = spells;
        Items = items;
        Worn = worn;
        Clock = clock ?? (() => Environment.TickCount64);

        Skills.SlotChanged += slot =>
        {
            Found.Clear();
            ObserveAbility(HotbarKind.Skill, SkillNames, slot, Skills.GetSlot(slot).AbilityName);
        };

        Spells.SlotChanged += slot =>
        {
            Found.Clear();
            ObserveAbility(HotbarKind.Spell, SpellNames, slot, Spells.GetSlot(slot).AbilityName);
        };

        Skills.Cleared += () =>
        {
            Found.Clear();
            ForgetAbilities(HotbarKind.Skill, SkillNames);
        };

        Spells.Cleared += () =>
        {
            Found.Clear();
            ForgetAbilities(HotbarKind.Spell, SpellNames);
        };

        Items.SlotChanged += _ => Found.Clear();
        Items.Cleared += Found.Clear;
    }

    /// <summary>The page that shows, from 0.</summary>
    public int Page { get; private set; }

    /// <summary>Raised after the player changes an entry or the page, or an entry empties; not after a load or a reset.</summary>
    public event Action? Changed;

    /// <summary>The entry at <paramref name="index" /> of <paramref name="page" />, or null.</summary>
    public HotbarEntry? Get(int page, int index) => InRange(page, index) ? Cells[page, index]?.Entry : null;

    /// <summary>The entry at <paramref name="index" /> of the page that shows, or null.</summary>
    public HotbarEntry? Get(int index) => Get(Page, index);

    /// <summary>Puts <paramref name="entry" /> at <paramref name="index" /> of <paramref name="page" />, in place of the entry there.</summary>
    public void Set(int page, int index, HotbarEntry entry)
    {
        //the same entry again takes this server: a file of version 1 gave every entry the server that saved it last
        if (!InRange(page, index)
            || string.IsNullOrWhiteSpace(entry.Name)
            || ((Cells[page, index] is { } cell) && (cell.Entry == entry) && string.Equals(cell.Owner, Server, StringComparison.OrdinalIgnoreCase)))
            return;

        Cells[page, index] = new HotbarCell(entry, Server);
        Changed?.Invoke();
    }

    /// <summary>Empties <paramref name="index" /> of <paramref name="page" />.</summary>
    public void Clear(int page, int index)
    {
        if (!InRange(page, index) || (Cells[page, index] is null))
            return;

        Cells[page, index] = null;
        Changed?.Invoke();
    }

    /// <summary>
    ///     Swaps two entries. The positions are absolute, so a drag keeps its source when the page changes during the drag.
    /// </summary>
    public void Move(int fromPage, int from, int toPage, int to)
    {
        if (!InRange(fromPage, from) || !InRange(toPage, to) || ((fromPage == toPage) && (from == to)))
            return;

        (Cells[fromPage, from], Cells[toPage, to]) = (Cells[toPage, to], Cells[fromPage, from]);
        Changed?.Invoke();
    }

    /// <summary>Shows <paramref name="page" />; a page past the last wraps to the first, and before the first to the last.</summary>
    public void SetPage(int page)
    {
        var wrapped = ((page % PAGES) + PAGES) % PAGES;

        if (wrapped == Page)
            return;

        Page = wrapped;
        Changed?.Invoke();
    }

    /// <summary>The entry for the skill in book slot <paramref name="slot" />, or null for an empty slot.</summary>
    public HotbarEntry? EntryForSkill(byte slot)
    {
        ref readonly var skill = ref Skills.GetSlot(slot);

        return string.IsNullOrWhiteSpace(skill.AbilityName) ? null : new HotbarEntry(HotbarKind.Skill, skill.AbilityName, skill.Sprite);
    }

    /// <summary>The entry for the spell in book slot <paramref name="slot" />, or null for an empty slot.</summary>
    public HotbarEntry? EntryForSpell(byte slot)
    {
        ref readonly var spell = ref Spells.GetSlot(slot);

        return string.IsNullOrWhiteSpace(spell.AbilityName) ? null : new HotbarEntry(HotbarKind.Spell, spell.AbilityName, spell.Sprite);
    }

    /// <summary>The entry for the item in inventory slot <paramref name="slot" />, or null for an empty slot.</summary>
    public HotbarEntry? EntryForItem(byte slot)
    {
        ref readonly var item = ref Items.GetSlot(slot);

        return !item.IsOccupied || string.IsNullOrWhiteSpace(item.Name)
            ? null
            : new HotbarEntry(HotbarKind.Item, item.Name, item.Sprite, item.Color);
    }

    /// <summary>
    ///     The book or inventory slot that holds <paramref name="entry" /> now, or null when the book or the inventory does
    ///     not have it. For an item it is the first slot of the item.
    /// </summary>
    public byte? Resolve(HotbarEntry entry) => Find(entry).Slot;

    //the slot and the count of an entry: from the lookups since the last change of a book or the inventory, else a search
    private (byte? Slot, uint Count) Find(HotbarEntry entry)
    {
        var key = (entry.Kind, entry.Name);

        if (Found.TryGetValue(key, out var found))
            return found;

        found = entry.Kind switch
        {
            HotbarKind.Skill => (Skills.FindSlot(entry.Name), 0),
            HotbarKind.Spell => (Spells.FindSlot(entry.Name), 0),
            _                => (Items.FindSlot(entry.Name), CountOf(entry.Name))
        };

        Found[key] = found;

        return found;
    }

    /// <summary>The number of an item in the inventory: the sum of its stacks, and 1 for each item that does not stack.</summary>
    public uint CountOf(string itemName)
    {
        uint count = 0;

        for (byte slot = 1; slot <= Inventory.MAX_SLOTS; slot++)
        {
            ref readonly var item = ref Items.GetSlot(slot);

            if (item.IsOccupied && (item.Name?.EqualsI(itemName) == true))
                count += item.Stackable ? item.Count : 1;
        }

        return count;
    }

    /// <summary>
    ///     What the slot of <paramref name="entry" /> shows now: the slot that holds it (null while it is gone), the icon
    ///     (the icon of that slot, else the icon that the entry keeps), the count of an item, and the cooldown of an ability
    ///     (0 = ready, 1 = just started).
    /// </summary>
    public HotbarEntryState StateOf(HotbarEntry entry)
    {
        var (found, count) = Find(entry);

        if (found is not { } slot)
            return new HotbarEntryState(null, entry.Sprite, entry.Color, 0, 0);

        switch (entry.Kind)
        {
            case HotbarKind.Skill:
                return new HotbarEntryState(slot, Skills.GetSlot(slot).Sprite, DisplayColor.Default, 0, Skills.GetCooldownPercent(slot));

            case HotbarKind.Spell:
                return new HotbarEntryState(slot, Spells.GetSlot(slot).Sprite, DisplayColor.Default, 0, Spells.GetCooldownPercent(slot));

            default:
                ref readonly var item = ref Items.GetSlot(slot);

                return new HotbarEntryState(slot, item.Sprite, item.Color, count, 0);
        }
    }

    //true when the player wears an item of this name
    private bool IsWorn(string itemName) => Worn.GetAll().Values.Any(item => item.Name.EqualsI(itemName));

    /// <summary>
    ///     Empties the entries of each ability that left its book <see cref="REMOVAL_WAIT_MS" /> ago and did not come back.
    ///     It empties nothing while <paramref name="inWorld" /> is false: with no world connection the add of a swap cannot
    ///     come, and a removal waits until the connection is back.
    /// </summary>
    public void Tick(bool inWorld)
    {
        if ((Removals.Count == 0) || !inWorld)
            return;

        var now = Clock();

        //most ticks come before the wait ends: they copy nothing
        var due = false;

        foreach (var removedAt in Removals.Values)
            if (now - removedAt >= REMOVAL_WAIT_MS)
            {
                due = true;

                break;
            }

        if (!due)
            return;

        var changed = false;

        foreach (var ((kind, name), removedAt) in Removals.ToArray())
        {
            if (now - removedAt < REMOVAL_WAIT_MS)
                continue;

            Removals.Remove((kind, name));

            var key = new HotbarEntry(kind, name);

            if (Resolve(key) is null)
                changed |= EmptyWhere(entry => entry.SameKey(key));
        }

        if (changed)
            Changed?.Invoke();
    }

    /// <summary>
    ///     At the end of a session: empties each item entry whose item the player neither carries nor wears, and each ability
    ///     entry whose ability is not in its book, of the entries of this session's server. It does nothing until
    ///     <see cref="SETTLE_MS" /> after the load, so a session that ends during the world entry keeps the entries whose
    ///     ability or item has not arrived yet.
    /// </summary>
    public void DropGoneEntries()
    {
        if (LoadedAt is not { } loadedAt || (Clock() - loadedAt < SETTLE_MS))
            return;

        if (EmptyWhere(entry => (Resolve(entry) is null) && ((entry.Kind != HotbarKind.Item) || !IsWorn(entry.Name))))
            Changed?.Invoke();
    }

    /// <summary>The server of the session that put the entry at <paramref name="index" /> of <paramref name="page" />, or null.</summary>
    internal string? OwnerOf(int page, int index) => InRange(page, index) ? Cells[page, index]?.Owner : null;

    /// <summary>Empties every entry and shows the first page, for the next character; raises no change, because nothing saves it.</summary>
    public void Reset()
    {
        Array.Clear(Cells);
        Page = 0;
        Removals.Clear();
        Found.Clear();
        Array.Clear(SkillNames);
        Array.Clear(SpellNames);
        LoadedAt = null;
        Server = null;
    }

    /// <summary>
    ///     The lines of the hotbar file: <c>version N</c>, <c>page N</c>, the entries of no known server, then for each
    ///     server <c>server S</c> and its entries. An entry is one line, <c>page index kind sprite color name</c>, with the
    ///     page and the index from 0.
    /// </summary>
    public IEnumerable<string> Format()
    {
        yield return FormattableString.Invariant($"{VERSION_KEY} {FILE_VERSION}");
        yield return FormattableString.Invariant($"{PAGE_KEY} {Page}");

        List<string?> owners = [null];

        foreach (var cell in Cells)
            if ((cell?.Owner is { } owner) && !owners.Contains(owner))
                owners.Add(owner);

        foreach (var owner in owners)
        {
            var lines = EntryLinesOf(owner).ToList();

            if (lines.Count == 0)
                continue;

            if (owner is not null)
                yield return $"{SERVER_KEY} {owner}";

            foreach (var line in lines)
                yield return line;
        }
    }

    private IEnumerable<string> EntryLinesOf(string? owner)
    {
        for (var page = 0; page < PAGES; page++)
            for (var index = 0; index < SLOTS; index++)
                if ((Cells[page, index] is { } cell) && (cell.Owner == owner))
                    yield return FormattableString.Invariant(
                        $"{page} {index} {cell.Entry.Kind.ToString().ToLowerInvariant()} {cell.Entry.Sprite} {(byte)cell.Entry.Color} {cell.Entry.Name}");
    }

    /// <summary>
    ///     Reads the lines of the hotbar file in place of the entries, at the world entry; a line that does not parse is
    ///     skipped. It raises no change, so a load does not write the file again. A file of a version after
    ///     <see cref="FILE_VERSION" /> (a newer client wrote it) loads no entry, and the result is false: the caller must not
    ///     write over it. A <c>server</c> line gives the server of the entries after it. A name keeps its spaces.
    /// </summary>
    /// <param name="lines">The lines of the file.</param>
    /// <param name="server">The server of this session (its host and name), or null when it is not known.</param>
    public bool Load(IEnumerable<string> lines, string? server = null)
    {
        Array.Clear(Cells);
        Page = 0;
        LoadedAt = Clock();
        Server = string.IsNullOrWhiteSpace(server) ? null : server.Trim();
        string? owner = null;

        var all = lines.ToArray();

        if (all.Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
               .Any(parts => (parts.Length == 2) && (parts[0] == VERSION_KEY) && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var version) || (version > FILE_VERSION))))
            return false;

        foreach (var line in all)
        {
            if (line.Trim().StartsWith(SERVER_KEY + " ", StringComparison.Ordinal))
            {
                var named = line.Trim()[(SERVER_KEY.Length + 1)..].Trim();
                owner = named.Length == 0 ? null : named;

                continue;
            }

            var setting = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if ((setting.Length == 2) && (setting[0] == PAGE_KEY) && TryNumber(setting[1], PAGES, out var shown))
            {
                Page = shown;

                continue;
            }

            //Format writes one space between the fields, so the name is the rest of the line, with its own spaces
            var parts = line.Split(' ', 6);

            if ((parts.Length == 6)
                && TryNumber(parts[0], PAGES, out var page)
                && TryNumber(parts[1], SLOTS, out var index)
                && TryKind(parts[2], out var kind)
                && TryNumber(parts[3], ushort.MaxValue + 1, out var sprite)
                && TryNumber(parts[4], byte.MaxValue + 1, out var color)
                && !string.IsNullOrWhiteSpace(parts[5]))
                Cells[page, index] = new HotbarCell(new HotbarEntry(kind, parts[5], (ushort)sprite, (DisplayColor)color), owner);
        }

        return true;
    }

    private static bool TryNumber(string text, int count, out int value)
        => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && (value < count);

    //only the names that Format writes; Enum.TryParse would also take a number or a list
    private static bool TryKind(string text, out HotbarKind kind)
    {
        foreach (var candidate in Enum.GetValues<HotbarKind>())
            if (candidate.ToString().ToLowerInvariant() == text)
            {
                kind = candidate;

                return true;
            }

        kind = default;

        return false;
    }

    private static bool InRange(int page, int index) => page is >= 0 and < PAGES && index is >= 0 and < SLOTS;

    //empties the matching entries of this session's server; an entry of no known server counts as this server's
    private bool EmptyWhere(Func<HotbarEntry, bool> match)
    {
        var emptied = false;

        for (var page = 0; page < PAGES; page++)
            for (var index = 0; index < SLOTS; index++)
                if ((Cells[page, index] is { } cell) && IsOfThisServer(cell.Owner) && match(cell.Entry))
                {
                    Cells[page, index] = null;
                    emptied = true;
                }

        return emptied;
    }

    private bool IsOfThisServer(string? owner) => (owner is null) || owner.EqualsI(Server ?? string.Empty);

    private void ObserveAbility(HotbarKind kind, string?[] names, byte slot, string? name)
    {
        var index = slot - 1;

        if (index < 0 || index >= names.Length)
            return;

        var old = names[index];
        names[index] = name;

        if ((old is not null) && !old.EqualsI(name ?? string.Empty))
            Removals[(kind, old.ToLowerInvariant())] = Clock();
    }

    private void ForgetAbilities(HotbarKind kind, string?[] names)
    {
        Array.Clear(names);

        foreach (var key in Removals.Keys.Where(key => key.Kind == kind).ToArray())
            Removals.Remove(key);
    }

    //an entry in a slot, and the server of the session that put it there, or null
    private readonly record struct HotbarCell(HotbarEntry Entry, string? Owner);

    //an entry key: the kind, and the name in any case, as HotbarEntry.SameKey compares them
    private sealed class KeyComparer : IEqualityComparer<(HotbarKind Kind, string Name)>
    {
        public static readonly KeyComparer Instance = new();

        public bool Equals((HotbarKind Kind, string Name) x, (HotbarKind Kind, string Name) y)
            => (x.Kind == y.Kind) && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);

        public int GetHashCode((HotbarKind Kind, string Name) key)
            => HashCode.Combine(key.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(key.Name));
    }
}
