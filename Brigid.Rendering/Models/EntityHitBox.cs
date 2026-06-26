#region
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Rendering.Models;

/// <summary>
///     Screen-space hitbox entry registered during the entity draw pass. Hitboxes are 28px wide (centered on the entity's
///     tile) and 60px tall (bottom-aligned to the entity's current rendered texture). Entries are stored in draw order;
///     later entries (closer to camera) take priority.
/// </summary>
public readonly record struct EntityHitBox(uint EntityId, Rectangle ScreenRect);