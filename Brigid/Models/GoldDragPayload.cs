#region
using Brigid.Rendering;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Models;

/// <summary>
///     The payload of a drag of the gold bag of the Inventory window of the new HUD (BRIG-150). A drop opens the gold
///     prompt, for the ground or for the aisling or the creature under the pointer, as the classic gold bag does. It has its
///     own type, so a drop on a slot of the window is not a swap. The drag shows the gold bag icon role.
/// </summary>
public sealed record GoldDragPayload : IDragIconPayload
{
    public Texture2D? Icon => null;

    public string IconRole => ThemeRoles.IconInventoryGoldBag;
}
