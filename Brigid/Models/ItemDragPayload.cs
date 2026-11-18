#region
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Models;

/// <summary>
///     The payload of a drag of an item from the Inventory window of the new HUD (BRIG-150): the inventory slot, and the
///     icon that the drag shows. A drop on another slot of the window swaps the two slots; a drop outside the window goes
///     where a drop from the classic inventory goes (the exchange, an equipment slot, an aisling or a creature, the
///     ground). The icon is for the drag only: a receiver reads the slot.
/// </summary>
public sealed record ItemDragPayload(byte Slot, Texture2D? Icon) : IDragIconPayload;
