#region
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

    /// <summary>
    ///     True when the 0x08 flag byte's movement-mode bits (high pair) are set. Hybrasyl sets these via
    ///     <c>GameMasterA</c> when collisions are disabled; gates GM wall-clip and pathfinding bypass.
    /// </summary>
    public bool IsGameMaster { get; private set; }

    /// <summary>
    ///     Fired when attributes are updated by the server.
    /// </summary>
    public event ChangedHandler? Changed;

    /// <summary>
    ///     Clears the stored attributes.
    /// </summary>
    public void Clear() => HasData = false;

    /// <summary>
    ///     Overlays the populated sub-records of a server attributes packet onto the accumulated flat
    ///     state and fires <see cref="Changed" />.
    /// </summary>
    public void Update(AttributesPacket pkt)
    {
        HasData = true;

        if (pkt.Primary is { } p)
        {
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
        IsGameMaster = pkt.MovementMode != 0;

        Changed?.Invoke();
    }
}
