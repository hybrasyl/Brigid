#region
using Chaos.DarkAges.Definitions;
#endregion

namespace Brigid.ViewModel;

/// <summary>
///     Authoritative spell book state. Owns spell slot data and cooldown timers. Fires change events for UI
///     reconciliation.
/// </summary>
public sealed class SpellBook
{
    public const int MAX_SLOTS = 90;
    private readonly float[] CooldownDuration = new float[MAX_SLOTS];
    private readonly float[] CooldownRemaining = new float[MAX_SLOTS];

    private readonly SpellSlotData[] Slots = new SpellSlotData[MAX_SLOTS];

    /// <summary>
    ///     Clears all slots and cooldowns. Fires <see cref="Cleared" />.
    /// </summary>
    public void Clear()
    {
        Array.Clear(Slots);
        Array.Clear(CooldownRemaining);
        Array.Clear(CooldownDuration);
        Cleared?.Invoke();
    }

    /// <summary>
    ///     Fired when all slots are cleared (e.g., on logout or full re-entry).
    /// </summary>
    public event ClearedHandler? Cleared;

    /// <summary>
    ///     Clears a spell slot and its cooldown. Fires <see cref="SlotChanged" />.
    /// </summary>
    public void ClearSlot(byte slot)
    {
        var index = slot - 1;

        if (index is < 0 or >= MAX_SLOTS)
            return;

        Slots[index] = default;
        CooldownRemaining[index] = 0;
        CooldownDuration[index] = 0;
        SlotChanged?.Invoke(slot);
    }

    /// <summary>
    ///     Returns the cooldown progress for a 1-based slot (0 = ready, 1 = just started).
    /// </summary>
    public float GetCooldownPercent(byte slot)
    {
        var index = slot - 1;

        if (index is < 0 or >= MAX_SLOTS)
            return 0;

        return CooldownDuration[index] > 0 ? CooldownRemaining[index] / CooldownDuration[index] : 0;
    }

    /// <summary>
    ///     Returns the cooldown remaining for a 1-based slot, in milliseconds (0 = ready).
    /// </summary>
    public float GetCooldownRemainingMs(byte slot)
    {
        var index = slot - 1;

        return index is < 0 or >= MAX_SLOTS ? 0 : CooldownRemaining[index];
    }

    /// <summary>
    ///     Returns the data for a 1-based slot number. Returns default if slot is out of range.
    /// </summary>
    public ref readonly SpellSlotData GetSlot(byte slot)
    {
        var index = slot - 1;

        if (index is < 0 or >= MAX_SLOTS)
            return ref SpellSlotData.Empty;

        return ref Slots[index];
    }

    /// <summary>
    ///     Returns true if the 1-based slot has an active cooldown.
    /// </summary>
    public bool IsOnCooldown(byte slot)
    {
        var index = slot - 1;

        if (index is < 0 or >= MAX_SLOTS)
            return false;

        return CooldownRemaining[index] > 0;
    }

    /// <summary>
    ///     Starts or resets a cooldown on a 1-based slot. No event — controls poll cooldown directly each frame.
    /// </summary>
    public void SetCooldown(byte slot, uint durationSecs)
    {
        var index = slot - 1;

        if (index is < 0 or >= MAX_SLOTS)
            return;

        var durationMs = durationSecs * 1000f;
        CooldownRemaining[index] = durationMs;
        CooldownDuration[index] = durationMs;
    }

    /// <summary>
    ///     Sets or updates a spell slot. Fires <see cref="SlotChanged" />.
    /// </summary>
    public void SetSlot(
        byte slot,
        ushort sprite,
        string? name,
        SpellType spellType = SpellType.None,
        string? prompt = null,
        byte castLines = 0,
        string[]? chants = null)
    {
        var index = slot - 1;

        if (index is < 0 or >= MAX_SLOTS)
            return;

        Slots[index] = new SpellSlotData(
            sprite,
            name,
            spellType,
            prompt,
            castLines,
            chants);
        SlotChanged?.Invoke(slot);
    }

    /// <summary>
    ///     Fired when a specific slot's data changes (added, updated, or cleared). Argument is the 1-based slot number.
    /// </summary>
    public event SlotChangedHandler? SlotChanged;

    /// <summary>
    ///     Ticks all active cooldown timers. Called once per frame by WorldState.Update().
    /// </summary>
    public void Update(float elapsedMs)
    {
        for (var i = 0; i < MAX_SLOTS; i++)
        {
            if (CooldownRemaining[i] <= 0)
                continue;

            CooldownRemaining[i] -= elapsedMs;

            if (CooldownRemaining[i] <= 0)
            {
                CooldownRemaining[i] = 0;
                CooldownDuration[i] = 0;
            }
        }
    }

    /// <summary>
    ///     Immutable data for a single spell slot.
    /// </summary>
    public readonly record struct SpellSlotData(
        ushort Sprite,
        string? Name,
        SpellType SpellType,
        string? Prompt,
        byte CastLines,
        string[]? Chants)
    {
        internal static readonly SpellSlotData Empty = default;

        /// <summary>
        ///     Parsed ability name without the level suffix (e.g. "beag ioc" from "beag ioc (Lev:25/50)").
        /// </summary>
        public string? AbilityName { get; } = ParseAbilityName(Name);

        /// <summary>
        ///     Parsed current level from the name suffix (e.g. 25 from "beag ioc (Lev:25/50)"). Zero if absent.
        /// </summary>
        public byte CurrentLevel { get; } = ParseLevel(Name);

        /// <summary>
        ///     Parsed max level from the name suffix (e.g. 50 from "beag ioc (Lev:25/50)"). Zero if absent.
        /// </summary>
        public byte MaxLevel { get; } = ParseMaxLevel(Name);

        /// <summary>
        ///     True if this slot contains a spell. Sprite 0 is a valid spell icon (frame 0 of spell001.epf), so
        ///     occupation is determined by the presence of a name instead — cleared slots have <c>Name == null</c>.
        /// </summary>
        public bool IsOccupied => Name is not null;

        private static string? ParseAbilityName(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            var levIndex = name.LastIndexOf("(Lev:", StringComparison.Ordinal);

            return levIndex > 0 ? name[..levIndex].TrimEnd() : name;
        }

        private static byte ParseLevel(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;

            var levIndex = name.LastIndexOf("(Lev:", StringComparison.Ordinal);

            if (levIndex < 0)
                return 0;

            var start = levIndex + 5;
            var slashIndex = name.IndexOf('/', start);

            if (slashIndex <= start)
                return 0;

            return byte.TryParse(name.AsSpan(start, slashIndex - start), out var level) ? level : (byte)0;
        }

        private static byte ParseMaxLevel(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;

            var levIndex = name.LastIndexOf("(Lev:", StringComparison.Ordinal);

            if (levIndex < 0)
                return 0;

            var slashIndex = name.IndexOf('/', levIndex + 5);

            if (slashIndex < 0)
                return 0;

            var start = slashIndex + 1;
            var endIndex = name.IndexOf(')', start);

            if (endIndex <= start)
                return 0;

            return byte.TryParse(name.AsSpan(start, endIndex - start), out var level) ? level : (byte)0;
        }
    }
}