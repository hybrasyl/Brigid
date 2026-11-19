#region
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Models;

/// <summary>
///     The payload of a drag of a hotbar slot (BRIG-146): the page and the index of the slot, which do not change when the
///     page that shows changes during the drag, and the icon that the drag shows. A drop on a hotbar slot swaps the two
///     entries, and a drop on the hotbar but not on a slot keeps the slot; a drag that ends anywhere else clears the slot,
///     and does nothing more (no cast at an aisling, no drop to the ground).
/// </summary>
public sealed record HotbarDragPayload(int Page, int Index, Texture2D? Icon) : IDragIconPayload;
