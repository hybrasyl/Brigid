#region
using Chaos.Geometry.Abstractions.Definitions;
#endregion

namespace Brigid.Extensions;

public static class DirectionExtensions
{
    extension(Direction direction)
    {
        public (int DX, int DY) ToTileOffset()
            => direction switch
            {
                Direction.Up    => (0, -1),
                Direction.Right => (1, 0),
                Direction.Down  => (0, 1),
                Direction.Left  => (-1, 0),
                _               => (0, 0)
            };
    }
}