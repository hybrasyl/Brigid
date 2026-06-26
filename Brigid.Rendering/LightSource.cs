#region
using Brigid.Data.Models;
using Chaos.Geometry.Abstractions.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Rendering;

public readonly record struct LightSource(
    Vector2 ScreenPosition,
    int TileX,
    int TileY,
    Direction Direction,
    LightMask PixelMask,
    (int Dx, int Dy)[] TileOffsets);
