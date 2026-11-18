#region
using Brigid.Data.Models;
using Brigid.Definitions;
using Brigid.ViewModel;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>A row of the Abilities window: a skill or a spell, learned (with its book slot) or not.</summary>
/// <param name="IsSpell">True for a spell, false for a skill.</param>
/// <param name="Slot">The book slot of a learned ability, or null for one that the player can learn.</param>
/// <param name="Name">The name, with no level.</param>
/// <param name="Level">The level text ("3/100"), or empty for an ability with no levels.</param>
/// <param name="IconSprite">The icon of the ability.</param>
/// <param name="Entry">The class ability metadata of the ability, when the metafile has it.</param>
/// <param name="State">
///     Known for a learned ability; for one that the player can learn, Learnable or Locked, which picks its blue or grey
///     icon. A change of the state makes another row, so the window reads the icon again.
/// </param>
internal sealed record AbilityRow(
    bool IsSpell,
    byte? Slot,
    string Name,
    string Level,
    ushort IconSprite,
    AbilityMetadataEntry? Entry,
    AbilityIconState State = AbilityIconState.Known)
{
    public bool IsLearned => Slot is not null;
}

/// <summary>
///     The rows of the Abilities window (BRIG-149, BRIG-127 D21, D35). The skills come first, then the spells. In each kind
///     the learned abilities come in the order of their book slots, so the primary, secondary and utility pages of D21
///     stay together; then the abilities of the class ability metadata that the book does not have (the server sends
///     part of that list in the metafile today), in the order of the metadata. A page holds 16 rows, down the left column and then
///     down the right one.
/// </summary>
internal static class AbilityList
{
    public const int ROWS = 8;
    public const int COLUMNS = 2;
    public const int PAGE = ROWS * COLUMNS;

    /// <param name="stateOf">The state of an ability that the player can learn (Learnable or Locked).</param>
    public static IReadOnlyList<AbilityRow> Build(
        SkillBook skills,
        SpellBook spells,
        AbilityMetadata? metadata,
        bool learned,
        bool unlearned,
        Func<AbilityMetadataEntry, AbilityIconState> stateOf)
    {
        var rows = new List<AbilityRow>();
        AddKind(rows, SkillRows(skills, metadata), metadata?.Skills, false, learned, unlearned, stateOf);
        AddKind(rows, SpellRows(spells, metadata), metadata?.Spells, true, learned, unlearned, stateOf);

        return rows;
    }

    /// <summary>The number of pages of <paramref name="count" /> rows: one at least, so an empty list has a page.</summary>
    public static int PageCount(int count) => Math.Max(1, (count + PAGE - 1) / PAGE);

    /// <summary><paramref name="page" /> held to the pages of <paramref name="count" /> rows.</summary>
    public static int ClampPage(int page, int count) => Math.Clamp(page, 0, PageCount(count) - 1);

    /// <summary>The column and the row on its page of the row at <paramref name="index" /> of the page.</summary>
    public static (int Column, int Row) PlaceOf(int index) => (index / ROWS, index % ROWS);

    /// <summary>The level text of a level and a maximum: empty for an ability with no levels.</summary>
    public static string LevelText(byte level, byte max) => max == 0 ? string.Empty : $"{level}/{max}";

    private static void AddKind(
        List<AbilityRow> rows,
        List<AbilityRow> known,
        IReadOnlyList<AbilityMetadataEntry>? entries,
        bool isSpell,
        bool learned,
        bool unlearned,
        Func<AbilityMetadataEntry, AbilityIconState> stateOf)
    {
        if (learned)
            rows.AddRange(known);

        if (!unlearned || entries is null)
            return;

        var names = known.Select(row => row.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
            if (!names.Contains(entry.Name))
                rows.Add(new AbilityRow(isSpell, null, entry.Name, string.Empty, entry.IconSprite, entry, stateOf(entry)));
    }

    private static List<AbilityRow> SkillRows(SkillBook book, AbilityMetadata? metadata)
    {
        var rows = new List<AbilityRow>();

        for (byte slot = 1; slot <= SkillBook.MAX_SLOTS; slot++)
        {
            ref readonly var data = ref book.GetSlot(slot);

            if (data is { IsOccupied: true, AbilityName: { } name })
                rows.Add(new AbilityRow(false, slot, name, LevelText(data.CurrentLevel, data.MaxLevel), data.Sprite, EntryOf(metadata, name, false)));
        }

        return rows;
    }

    private static List<AbilityRow> SpellRows(SpellBook book, AbilityMetadata? metadata)
    {
        var rows = new List<AbilityRow>();

        for (byte slot = 1; slot <= SpellBook.MAX_SLOTS; slot++)
        {
            ref readonly var data = ref book.GetSlot(slot);

            if (data is { IsOccupied: true, AbilityName: { } name })
                rows.Add(new AbilityRow(true, slot, name, LevelText(data.CurrentLevel, data.MaxLevel), data.Sprite, EntryOf(metadata, name, true)));
        }

        return rows;
    }

    private static AbilityMetadataEntry? EntryOf(AbilityMetadata? metadata, string name, bool isSpell)
        => metadata is not null && metadata.TryGet(name, isSpell, out var entry) ? entry : null;
}
