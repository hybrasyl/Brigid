#region
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Extensions;

public static class RectangleExtensions
{
    //horizontal — return x position

    extension(Rectangle rect)
    {
        public int AlignBottom(int itemHeight, int padding = 0) => rect.Bottom - itemHeight - padding;
        public int AlignLeft(int padding = 0) => rect.X + padding;
        public int AlignRight(int itemWidth, int padding = 0) => rect.Right - itemWidth - padding;
        public int AlignTop(int padding = 0) => rect.Y + padding;

        public Rectangle Center(int itemWidth, int itemHeight)
            => new(
                rect.CenterX(itemWidth),
                rect.CenterY(itemHeight),
                itemWidth,
                itemHeight);

        public int CenterX(int itemWidth) => rect.X + (rect.Width - itemWidth) / 2;
        public int CenterY(int itemHeight) => rect.Y + (rect.Height - itemHeight) / 2;
    }
}