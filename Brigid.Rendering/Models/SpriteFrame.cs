#region
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Rendering.Models;

/// <summary>
///     A GPU-resident sprite frame with positioning metadata needed to draw it correctly. CenterX/CenterY define the
///     anchor point that aligns with a tile's center. Left/Top are the original frame offsets from the source format
///     (EPF/MPF/EFA).
/// </summary>
public readonly record struct SpriteFrame(
    Texture2D Texture,
    short CenterX,
    short CenterY,
    short Left,
    short Top) : IDisposable
{
    public void Dispose() => Texture?.Dispose();
}