#region
using Chaos.DarkAges.Definitions;
using Chaos.Extensions.Common;
#endregion

namespace Brigid.ViewModel;

/// <summary>
///     Authoritative inventory state. Owns item slot data and gold amount. Fires change events for UI reconciliation.
/// </summary>
public sealed class Inventory
{
    public const int MAX_SLOTS = 59;

    private readonly InventorySlotData[] Slots = new InventorySlotData[MAX_SLOTS];

    /// <summary>
    ///     The player's current gold amount.
    /// </summary>
    public uint Gold { get; private set; }

    /// <summary>
    ///     Clears all slots and gold. Fires <see cref="Cleared" />.
    /// </summary>
    public void Clear()
    {
        Array.Clear(Slots);
        Gold = 0;
        Cleared?.Invoke();
    }

    /// <summary>
    ///     Fired when all slots are cleared.
    /// </summary>
    public event ClearedHandler? Cleared;

    /// <summary>
    ///     Clears an inventory slot. Fires <see cref="SlotChanged" />.
    /// </summary>
    public void ClearSlot(byte slot)
    {
        var index = slot - 1;

        if (index is < 0 or >= MAX_SLOTS)
            return;

        Slots[index] = default;
        SlotChanged?.Invoke(slot);
    }

    /// <summary>
    ///     Returns the first empty inventory slot (1-based), or 0 if inventory is full.
    /// </summary>
    public byte GetFirstEmptySlot()
    {
        for (var i = 0; i < MAX_SLOTS; i++)
            if (!Slots[i].IsOccupied)
                return (byte)(i + 1);

        return 0;
    }

    /// <summary>
    ///     The first 1-based slot of an item named <paramref name="name" /> (case-insensitive, an exact match), or null when
    ///     the inventory does not have it.
    /// </summary>
    public byte? FindSlot(string name)
    {
        for (var i = 0; i < MAX_SLOTS; i++)
            if (Slots[i].IsOccupied && (Slots[i].Name?.EqualsI(name) == true))
                return (byte)(i + 1);

        return null;
    }

    /// <summary>
    ///     Returns the data for a 1-based slot number. Returns default if slot is out of range.
    /// </summary>
    public ref readonly InventorySlotData GetSlot(byte slot)
    {
        var index = slot - 1;

        if (index is < 0 or >= MAX_SLOTS)
            return ref InventorySlotData.Empty;

        return ref Slots[index];
    }

    /// <summary>
    ///     Fired when the gold amount changes.
    /// </summary>
    public event GoldChangedHandler? GoldChanged;

    /// <summary>
    ///     Updates the gold amount. Fires <see cref="GoldChanged" />.
    /// </summary>
    public void SetGold(uint gold)
    {
        if (Gold == gold)
            return;

        Gold = gold;
        GoldChanged?.Invoke();
    }

    /// <summary>
    ///     Sets or updates an inventory slot. Fires <see cref="SlotChanged" />.
    /// </summary>
    public void SetSlot(
        byte slot,
        ushort sprite,
        DisplayColor color,
        string? name,
        bool stackable,
        uint count,
        int maxDurability,
        int currentDurability)
    {
        var index = slot - 1;

        if (index is < 0 or >= MAX_SLOTS)
            return;

        Slots[index] = new InventorySlotData(
            sprite,
            color,
            name,
            stackable,
            count,
            maxDurability,
            currentDurability);
        SlotChanged?.Invoke(slot);
    }

    /// <summary>
    ///     Fired when a specific slot's data changes (added, updated, or cleared). Argument is the 1-based slot number.
    /// </summary>
    public event SlotChangedHandler? SlotChanged;

    /// <summary>
    ///     Immutable data for a single inventory slot.
    /// </summary>
    public readonly record struct InventorySlotData(
        ushort Sprite,
        DisplayColor Color,
        string? Name,
        bool Stackable,
        uint Count,
        int MaxDurability,
        int CurrentDurability)
    {
        internal static readonly InventorySlotData Empty = default;

        /// <summary>
        ///     The name with the count of a stack, as retail shows a stack in its tooltip and description (e.g. "Apple[ 3 ]").
        ///     <see cref="Name" /> is the item's name only, which the hotbar keeps an item by (BRIG-146).
        /// </summary>
        public string? NameWithCount { get; } = Stackable && (Count > 0) ? $"{Name}[ {Count} ]" : Name;

        /// <summary>
        ///     True if this slot contains an item (has a sprite assigned).
        /// </summary>
        public bool IsOccupied => Sprite > 0;
    }
}