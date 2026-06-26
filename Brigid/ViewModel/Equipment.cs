#region
using Chaos.DarkAges.Definitions;
#endregion

namespace Brigid.ViewModel;

/// <summary>
///     Authoritative equipment state. Owns equipment slot data keyed by <see cref="EquipmentSlot" />. Fires change events
///     for UI reconciliation.
/// </summary>
public sealed class Equipment
{
    private readonly Dictionary<EquipmentSlot, EquipmentSlotData> Slots = [];

    /// <summary>
    ///     Clears all equipment. Fires <see cref="Cleared" />.
    /// </summary>
    public void Clear()
    {
        Slots.Clear();
        Cleared?.Invoke();
    }

    /// <summary>
    ///     Fired when all equipment is cleared.
    /// </summary>
    public event ClearedHandler? Cleared;

    /// <summary>
    ///     Clears an equipment slot. Fires <see cref="SlotCleared" />.
    /// </summary>
    public void ClearSlot(EquipmentSlot slot)
    {
        Slots.Remove(slot);
        SlotCleared?.Invoke(slot);
    }

    /// <summary>
    ///     Returns all currently equipped slots as a read-only view.
    /// </summary>
    public IReadOnlyDictionary<EquipmentSlot, EquipmentSlotData> GetAll() => Slots;

    /// <summary>
    ///     Returns the data for an equipment slot, or null if nothing is equipped there.
    /// </summary>
    public EquipmentSlotData? GetSlot(EquipmentSlot slot) => Slots.GetValueOrDefault(slot);

    /// <summary>
    ///     Sets or updates an equipment slot. Fires <see cref="SlotChanged" />.
    /// </summary>
    public void SetSlot(
        EquipmentSlot slot,
        ushort sprite,
        DisplayColor color,
        string name,
        int maxDurability,
        int currentDurability)
    {
        Slots[slot] = new EquipmentSlotData(
            sprite,
            color,
            name,
            maxDurability,
            currentDurability);
        SlotChanged?.Invoke(slot);
    }

    /// <summary>
    ///     Fired when a specific equipment slot changes (equipped or updated). Argument is the slot that changed.
    /// </summary>
    public event EquipmentSlotChangedHandler? SlotChanged;

    /// <summary>
    ///     Fired when a specific equipment slot is cleared (unequipped). Argument is the slot that was cleared.
    /// </summary>
    public event EquipmentSlotClearedHandler? SlotCleared;

    /// <summary>
    ///     Immutable data for a single equipment slot.
    /// </summary>
    public readonly record struct EquipmentSlotData(
        ushort Sprite,
        DisplayColor Color,
        string Name,
        int MaxDurability,
        int CurrentDurability);
}