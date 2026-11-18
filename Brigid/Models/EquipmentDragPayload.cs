#region
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Models;

/// <summary>
///     The payload of a drag of an item from the Equipment window of the new HUD (BRIG-151): the equipment slot, and the
///     icon that the drag shows. A drop on the Inventory window takes the item off; a drop on the world does nothing.
/// </summary>
public sealed record EquipmentDragPayload(EquipmentSlot Slot, Texture2D? Icon) : IDragIconPayload;
