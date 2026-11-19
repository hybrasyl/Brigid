#region
using Chaos.DarkAges.Definitions;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>A slot of the Equipment window: its name, the slot of the wire that it shows, and its empty-slot art.</summary>
/// <param name="Id">The name of the control and the end of its icon role.</param>
/// <param name="Name">The name that the tooltip of an empty slot shows.</param>
/// <param name="Wire">The equipment slot of the wire, or null for a slot that the wire does not have yet.</param>
internal sealed record EquipmentSlotSpec(string Id, string Name, EquipmentSlot? Wire)
{
    /// <summary>The empty-slot art: an outline of the item, drawn dim. Made once: an empty slot reads it at each frame.</summary>
    public string IconRole { get; } = $"icon.equipment.{Id}";
}

/// <summary>
///     The slots of the Equipment window (BRIG-151, D31), in the order of <see cref="MainHudLayout.EquipmentIn" />: three
///     above the model, five at its left, five at its right, three below it, and the bottom row of five. The wire has 18
///     slots; Cape, Mount and Trinket have none yet, and show as disabled slots (Sabrael, 2026-11-18).
/// </summary>
internal static class EquipmentSlots
{
    public static readonly EquipmentSlotSpec[] All =
    [
        new("earrings", "Earrings", EquipmentSlot.Earrings),
        new("helm", "Helm", EquipmentSlot.Helmet),
        new("necklace", "Necklace", EquipmentSlot.Necklace),
        new("armor", "Armor", EquipmentSlot.Armor),
        new("left_ring", "Left Ring", EquipmentSlot.LeftRing),
        new("left_glove", "Left Glove", EquipmentSlot.LeftGaunt),
        new("belt", "Belt", EquipmentSlot.Belt),
        new("boots", "Boots", EquipmentSlot.Boots),
        new("cape", "Cape", null),
        new("right_ring", "Right Ring", EquipmentSlot.RightRing),
        new("right_glove", "Right Glove", EquipmentSlot.RightGaunt),
        new("greaves", "Greaves", EquipmentSlot.Greaves),
        new("mount", "Mount", null),
        new("weapon", "Weapon", EquipmentSlot.Weapon),
        new("offhand", "Offhand", EquipmentSlot.Shield),
        new("trinket", "Trinket", null),
        new("vanity_helm", "Vanity Helm", EquipmentSlot.OverHelm),
        new("vanity_armor", "Vanity Armor", EquipmentSlot.Overcoat),
        new("accessory_1", "Accessory 1", EquipmentSlot.Accessory1),
        new("accessory_2", "Accessory 2", EquipmentSlot.Accessory2),
        new("accessory_3", "Accessory 3", EquipmentSlot.Accessory3)
    ];

    /// <summary>The index in <see cref="All" /> of the slot that shows <paramref name="wire" />, or -1.</summary>
    public static int IndexOf(EquipmentSlot wire) => Array.FindIndex(All, spec => spec.Wire == wire);
}
