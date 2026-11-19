#region
using Brigid.Data.Models;
using Chaos.DarkAges.Definitions;
using DALib.Networking.Packets.Server;
#endregion

namespace Brigid.ViewModel;

/// <summary>
///     Authoritative player attributes state. Accumulates partial server updates (0x08
///     <see cref="AttributesPacket" />) by overlaying whichever sub-records are present onto a flat
///     surface. Fires a change event for UI reconciliation.
/// </summary>
public sealed class PlayerAttributes
{
    /// <summary>
    ///     Whether any attributes have been received from the server yet.
    /// </summary>
    public bool HasData { get; private set; }

    /// <summary>
    ///     Self-reference sentinel: returns this instance once data has been received, else null. Lets
    ///     consumers keep the <c>Attributes.Current is { } attrs</c> pattern while reading flat fields.
    /// </summary>
    public PlayerAttributes? Current => HasData ? this : null;

    //primary
    public byte Level { get; private set; }
    public byte Ability { get; private set; }
    public uint MaxHp { get; private set; }
    public uint MaxMp { get; private set; }
    public byte Str { get; private set; }
    public byte Int { get; private set; }
    public byte Wis { get; private set; }
    public byte Con { get; private set; }
    public byte Dex { get; private set; }
    public byte UnspentPoints { get; private set; }
    public ushort MaxWeight { get; private set; }
    public ushort CurrentWeight { get; private set; }

    //current
    public uint CurrentHp { get; private set; }
    public uint CurrentMp { get; private set; }

    //experience
    public uint TotalExp { get; private set; }
    public uint ToNextLevel { get; private set; }
    public uint TotalAbility { get; private set; }
    public uint ToNextAbility { get; private set; }
    public uint GamePoints { get; private set; }
    public uint Gold { get; private set; }

    //secondary
    public sbyte Ac { get; private set; }
    public byte Dmg { get; private set; }
    public byte Hit { get; private set; }
    public byte MagicResistance { get; private set; }
    public Element OffenseElement { get; private set; }
    public Element DefenseElement { get; private set; }
    public bool Blind { get; private set; }

    public bool HasUnreadMail { get; private set; }

    /// <summary>A letter is unread (the high nibble of the mail status). The new HUD shows its mail icon from it.</summary>
    public bool HasMail { get; private set; }

    /// <summary>A parcel waits (the low nibble of the mail status). The new HUD shows its parcel icon from it.</summary>
    public bool HasParcel { get; private set; }

    /// <summary>
    ///     True when the 0x08 flag byte's movement-mode bits (high pair) are set. Hybrasyl sets these via
    ///     <c>GameMasterA</c> when collisions are disabled; gates GM wall-clip and pathfinding bypass.
    /// </summary>
    public bool IsGameMaster { get; private set; }

    /// <summary>
    ///     Fired when attributes are updated by the server.
    /// </summary>
    public event ChangedHandler? Changed;

    /// <summary>The start of the current level, when the client knows it (BRIG-148).</summary>
    public ExperienceSpan LevelSpan { get; } = ExperienceSpan.ForLevel();

    /// <summary>The start of the current ability level of a retail master, when the client knows it (BRIG-148).</summary>
    public ExperienceSpan AbilitySpan { get; } = ExperienceSpan.ForAbility();

    /// <summary>
    ///     Takes the starts that the client saved for the character (<see cref="ExperienceSpan.Restore" />), and raises
    ///     <see cref="Changed" />, so the experience bar shows them.
    /// </summary>
    public void RestoreStarts(SavedExperience saved)
    {
        if (saved.Level is { } level)
            LevelSpan.Restore(level.Level, level.Start);

        if (saved.Ability is { } ability)
            AbilitySpan.Restore(ability.Level, ability.Start);

        Changed?.Invoke();
    }

    /// <summary>The starts that the client knows, to save for the character.</summary>
    public SavedExperience SavedStarts() => new(StartOf(LevelSpan), StartOf(AbilitySpan));

    private static SavedLevelStart? StartOf(ExperienceSpan span) => span.Start is { } start ? new SavedLevelStart(span.Level, start) : null;

    /// <summary>A packet taught <see cref="LevelSpan" /> or <see cref="AbilitySpan" /> a new start, so the caller saves them.</summary>
    public event ChangedHandler? SpanLearned;

    /// <summary>
    ///     True after the first primary block of the character. The spans and the activity log read the level from the
    ///     primary block, so they wait for it: a clear keeps the level of the character before.
    /// </summary>
    public bool HasPrimary { get; private set; }

    /// <summary>
    ///     Clears the stored attributes. The mail and parcel state clears too, because the flag alone keeps it (see
    ///     <see cref="DecodeMail" />), and the next character must not show the state of the one before.
    /// </summary>
    public void Clear()
    {
        HasData = false;
        HasMail = false;
        HasParcel = false;
        HasPrimary = false;

        //the next character's first level block must not read this character's experience
        TotalExp = 0;
        ToNextLevel = 0;
        TotalAbility = 0;
        ToNextAbility = 0;
        LevelSpan.Reset();
        AbilitySpan.Reset();
    }

    /// <summary>
    ///     The mail and parcel state after a packet. Retail decodes the two nibbles of the mail status only when the unread
    ///     flag (0x01) and the secondary section (0x04) are both present. With no flag, nothing is unread. Hybrasyl sends the
    ///     flag alone when a letter arrives (with no secondary section), so the flag alone keeps what is known, and shows
    ///     mail when nothing is known.
    /// </summary>
    internal static (bool Mail, bool Parcel) DecodeMail(bool unreadMail, byte? mailStatus, bool mail, bool parcel)
    {
        if (!unreadMail)
            return (false, false);

        if (mailStatus is { } status)
            return ((status & 0xF0) != 0, (status & 0x0F) != 0);

        return mail || parcel ? (mail, parcel) : (true, false);
    }

    /// <summary>
    ///     Overlays the populated sub-records of a server attributes packet onto the accumulated flat
    ///     state and fires <see cref="Changed" />.
    /// </summary>
    public void Update(AttributesPacket pkt)
    {
        HasData = true;

        if (pkt.Primary is { } p)
        {
            HasPrimary = true;
            Level = p.Level;
            Ability = p.Ability;
            MaxHp = p.MaxHp;
            MaxMp = p.MaxMp;
            Str = p.Str;
            Int = p.Int;
            Wis = p.Wis;
            Con = p.Con;
            Dex = p.Dex;
            UnspentPoints = p.UnspentPoints;
            MaxWeight = p.MaxWeight;
            CurrentWeight = p.CurrentWeight;
        }

        if (pkt.Current is { } c)
        {
            CurrentHp = c.Hp;
            CurrentMp = c.Mp;
        }

        if (pkt.Experience is { } e)
        {
            TotalExp = e.Experience;
            ToNextLevel = e.ExpToLevel;
            TotalAbility = e.AbilityExp;
            ToNextAbility = e.NextAB;
            GamePoints = e.Gp;
            Gold = e.Gold;
        }

        if (pkt.Secondary is { } s)
        {
            Ac = s.Ac;
            Dmg = s.DmgRating;
            Hit = s.HitRating;
            MagicResistance = s.MrRating;
            OffenseElement = (Element)s.OffensiveElement;
            DefenseElement = (Element)s.DefensiveElement;
            Blind = s.Blinded == SecondaryAttributes.BlindedActive;
        }

        HasUnreadMail = pkt.UnreadMail;
        (HasMail, HasParcel) = DecodeMail(pkt.UnreadMail, pkt.Secondary?.MailStatus, HasMail, HasParcel);
        IsGameMaster = pkt.MovementMode != 0;

        //a level-up of one level teaches the start of the new level (ExperienceSpan)
        var learned = false;

        if (HasPrimary && (pkt.Primary is not null || pkt.Experience is not null))
        {
            var experienceIsNew = pkt.Experience is not null;
            learned |= LevelSpan.Observe(Level, TotalExp, ToNextLevel, experienceIsNew);
            learned |= AbilitySpan.Observe(Ability, TotalAbility, ToNextAbility, experienceIsNew);
        }

        Changed?.Invoke();

        if (learned)
            SpanLearned?.Invoke();
    }
}
