#region
using System.Globalization;
using Brigid.Extensions;
using Brigid.ViewModel;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>A stat of Personal View (BRIG-151, D31). The saved pins name these members, so a rename breaks the saved pins.</summary>
internal enum PersonalStat
{
    Health,
    Mana,
    Str,
    Int,
    Wis,
    Con,
    Dex,
    Armor,
    Hit,
    AttackPower,
    MagicResistance,
    OffenseElement,
    DefenseElement
}

/// <summary>
///     The rules of Personal View (BRIG-151, D31): the read-only stats of <see cref="PlayerAttributes" />, each with its
///     label and its value. The labels are the player names of the stat list of the design (loures, Reference - Ability
///     Toolkit, 4.1; Sabrael, 2026-11-18); Armor is <c>100 - AC</c>, higher is better, as the dialect's armor field is.
///     The pinned stats are at the top in the order of pinning, then the others in the order of
///     <see cref="PersonalStat" />. The weight is not a stat here (Sabrael, 2026-11-18). The extended stats
///     of the <c>0x0008</c> attributes (BRIG-53) come later as more rows.
/// </summary>
internal static class PersonalStats
{
    public static readonly PersonalStat[] All = Enum.GetValues<PersonalStat>();

    public static string LabelOf(PersonalStat stat)
        => stat switch
        {
            PersonalStat.Health          => "Health",
            PersonalStat.Mana            => "Mana",
            PersonalStat.Str             => "Strength",
            PersonalStat.Int             => "Intelligence",
            PersonalStat.Wis             => "Wisdom",
            PersonalStat.Con             => "Constitution",
            PersonalStat.Dex             => "Dexterity",
            PersonalStat.Armor           => "Armor",
            PersonalStat.Hit             => "Hit",
            PersonalStat.AttackPower     => "Attack Power",
            PersonalStat.MagicResistance => "Magic Resist",
            PersonalStat.OffenseElement  => "Offense Element",
            PersonalStat.DefenseElement  => "Defense Element",
            _                            => stat.ToString()
        };

    /// <summary>The value of <paramref name="stat" />; the maximum for health and mana, as the stats of the classic panel.</summary>
    public static string ValueOf(PersonalStat stat, PlayerAttributes attrs)
        => stat switch
        {
            PersonalStat.Health          => Number(attrs.MaxHp),
            PersonalStat.Mana            => Number(attrs.MaxMp),
            PersonalStat.Str             => Number(attrs.Str),
            PersonalStat.Int             => Number(attrs.Int),
            PersonalStat.Wis             => Number(attrs.Wis),
            PersonalStat.Con             => Number(attrs.Con),
            PersonalStat.Dex             => Number(attrs.Dex),

            //HS-1705 6.3: Armor = 100 - AC is exact for today's mitigation curve
            PersonalStat.Armor           => Number(100 - attrs.Ac),
            PersonalStat.Hit             => Number(attrs.Hit),
            PersonalStat.AttackPower     => Number(attrs.Dmg),

            PersonalStat.MagicResistance => Number(attrs.MagicResistancePercent),
            PersonalStat.OffenseElement  => attrs.OffenseElement.Name,
            PersonalStat.DefenseElement  => attrs.DefenseElement.Name,
            _                            => string.Empty
        };

    /// <summary>The rows: the pinned stats in the order of pinning, then the others in their order.</summary>
    public static IReadOnlyList<PersonalStat> Order(IReadOnlyList<PersonalStat> pins)
        => pins.Concat(All.Where(stat => !pins.Contains(stat))).ToArray();

    /// <summary>The pins after a toggle of <paramref name="stat" />: a pinned stat is unpinned, else pinned last.</summary>
    public static IReadOnlyList<PersonalStat> Toggle(IReadOnlyList<PersonalStat> pins, PersonalStat stat)
        => pins.Contains(stat) ? pins.Where(pin => pin != stat).ToArray() : [.. pins, stat];

    /// <summary>The pins from saved names: a name that is not a stat and a second copy of a stat are skipped.</summary>
    public static IReadOnlyList<PersonalStat> Parse(IEnumerable<string> names)
    {
        var pins = new List<PersonalStat>();

        //by name only: Enum.TryParse also takes a number
        foreach (var name in names)
            if (Enum.TryParse<PersonalStat>(name.Trim(), out var stat) && (Enum.GetName(stat) == name.Trim()) && !pins.Contains(stat))
                pins.Add(stat);

        return pins;
    }

    /// <summary>The names to save; <see cref="Parse" /> reads them back.</summary>
    public static string[] Format(IReadOnlyList<PersonalStat> pins) => pins.Select(pin => pin.ToString()).ToArray();

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
