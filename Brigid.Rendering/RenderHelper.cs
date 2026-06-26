#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Provides primitive drawing helpers (filled rectangles) using a shared 1x1 white pixel texture.
/// </summary>
public static class RenderHelper
{
    private static Texture2D? SharedPixel;

    /// <summary>
    ///     Draws a filled rectangle using a shared 1x1 white pixel texture.
    /// </summary>
    public static void DrawRect(SpriteBatch spriteBatch, Rectangle bounds, Color color)
        => spriteBatch.Draw(GetPixel(spriteBatch.GraphicsDevice), bounds, color);

    private static Texture2D GetPixel(GraphicsDevice device)
    {
        if (SharedPixel is null || SharedPixel.IsDisposed)
        {
            SharedPixel = new Texture2D(device, 1, 1);
            SharedPixel.SetData([Color.White]);
        }

        return SharedPixel;
    }
}