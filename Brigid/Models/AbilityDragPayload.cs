#region
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Models;

/// <summary>
///     The payload of a drag of a learned ability from the Abilities window of the new HUD (BRIG-149): the kind and the
///     book slot, and the icon that the drag shows. A spell dropped on an aisling or a creature is cast at it, as retail
///     does with a dragged spell; the hotbar (BRIG-146) takes the same payload. The icon is for the drag only: a receiver
///     reads the kind and the slot.
/// </summary>
public sealed record AbilityDragPayload(bool IsSpell, byte Slot, Texture2D? Icon) : IDragIconPayload;
