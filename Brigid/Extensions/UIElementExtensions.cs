#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Extensions;

public static class UIElementExtensions
{
    private static readonly Rectangle ScreenRect = new(
        0,
        0,
        ChaosGame.VIRTUAL_WIDTH,
        ChaosGame.VIRTUAL_HEIGHT);

    extension(UIElement element)
    {
        public void AlignBottomIn(Rectangle rect, int padding = 0) => element.Y = rect.AlignBottom(element.Height, padding);

        public void AlignLeftIn(Rectangle rect, int padding = 0) => element.X = rect.AlignLeft(padding);

        public void AlignRightIn(Rectangle rect, int padding = 0) => element.X = rect.AlignRight(element.Width, padding);

        public void AlignTopIn(Rectangle rect, int padding = 0) => element.Y = rect.AlignTop(padding);

        public void CenterHorizontallyIn(Rectangle rect) => element.X = rect.CenterX(element.Width);

        public void CenterHorizontallyOnScreen() => element.X = ScreenRect.CenterX(element.Width);

        public void CenterIn(Rectangle rect)
        {
            element.X = rect.CenterX(element.Width);
            element.Y = rect.CenterY(element.Height);
        }

        public void CenterOnScreen() => element.CenterIn(ScreenRect);

        public void CenterVerticallyIn(Rectangle rect) => element.Y = rect.CenterY(element.Height);

        public void CenterVerticallyOnScreen() => element.Y = ScreenRect.CenterY(element.Height);
    }
}