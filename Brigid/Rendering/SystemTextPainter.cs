#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Draws solid rectangles and <see cref="SystemFontText" /> text (the OS typeface) in the virtual 640×480 space of a
///     native pass, with no game assets. A screen that runs before <c>DataContext.Initialize</c> (the launcher) uses it in
///     place of prefab panels and atlas fonts.
///     <para>
///         The pass draws under a <c>Scale(scaleX, scaleY)</c> transform. Text is rasterized at the native pixel size and
///         drawn with the inverse scale, so it keeps native detail. Text and the rectangles that must line up with it snap
///         to the native pixel grid, because a texture drawn between native pixels is resampled soft.
///     </para>
/// </summary>
public sealed class SystemTextPainter : IDisposable
{
    private readonly Dictionary<string, Texture2D> TextCache = new();
    private readonly Dictionary<string, float> WidthCache = new();

    private SpriteBatch Batch = null!;
    private Texture2D Pixel = null!;
    private float ScaleX = 1f;
    private float ScaleY = 1f;

    public void Dispose()
    {
        Pixel?.Dispose();
        Pixel = null!;
        ClearCaches();
    }

    public void LoadContent(GraphicsDevice device)
    {
        Pixel = new Texture2D(device, 1, 1);
        Pixel.SetData(new[] { Color.White });
    }

    /// <summary>
    ///     Call at the start of each native pass, before any draw. A change of scale (for example from the resolution
    ///     selector) clears the text caches, so the text is rasterized again at the new native size.
    /// </summary>
    public void Begin(SpriteBatch batch, float scaleX, float scaleY)
    {
        if ((scaleX != ScaleX) || (scaleY != ScaleY))
        {
            ClearCaches();
            ScaleX = scaleX;
            ScaleY = scaleY;
        }

        Batch = batch;
    }

    private void ClearCaches()
    {
        foreach (var texture in TextCache.Values)
            texture.Dispose();

        TextCache.Clear();
        WidthCache.Clear();
    }

    public void FillRect(Rectangle rect, Color color) => Batch.Draw(Pixel, rect, color);

    /// <summary>
    ///     Fills a rectangle in virtual coordinates, snapped to the native pixel grid, so it lines up with text that
    ///     <see cref="DrawText" /> puts there.
    /// </summary>
    public void FillRectSnapped(float x, float y, float width, float height, Color color)
    {
        var left = Snap(x, ScaleX);
        var top = Snap(y, ScaleY);
        var right = Snap(x + width, ScaleX);
        var bottom = Snap(y + height, ScaleY);

        Batch.Draw(Pixel, new Vector2(left, top), null, color, 0f, Vector2.Zero, new Vector2(right - left, bottom - top), SpriteEffects.None, 0f);
    }

    public void BorderRect(Rectangle rect, Color color)
    {
        Batch.Draw(Pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
        Batch.Draw(Pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
        Batch.Draw(Pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
        Batch.Draw(Pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
    }

    public void DrawText(string text, Vector2 position, Color color, float size)
    {
        if (string.IsNullOrEmpty(text))
            return;

        Batch.Draw(
            GetText(text, color, size),
            new Vector2(Snap(position.X, ScaleX), Snap(position.Y, ScaleY)),
            null,
            Color.White,
            0f,
            Vector2.Zero,
            new Vector2(1f / ScaleX, 1f / ScaleY),
            SpriteEffects.None,
            0f);
    }

    /// <summary>
    ///     The virtual width of <paramref name="text" />. It is measured at the native size that the text is rasterized
    ///     at and scaled back, so it agrees with what <see cref="DrawText" /> puts on the screen.
    /// </summary>
    public float Measure(string text, float size)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var key = $"{size:0.#}|{text}";

        if (WidthCache.TryGetValue(key, out var width))
            return width;

        width = SystemFontText.Measure(text, size * ScaleY) / ScaleX;
        WidthCache[key] = width;

        return width;
    }

    public float TextHeight(float size) => SystemFontText.LineHeight(size * ScaleY) / ScaleY;

    /// <summary>The top of one line of text, centred vertically in <paramref name="box" />.</summary>
    public float TextTop(Rectangle box, float size) => box.Y + (box.Height - TextHeight(size)) / 2f;

    //the cache key holds the virtual size; that is valid because Begin clears the cache when the native scale changes
    private Texture2D GetText(string text, Color color, float size)
    {
        var key = $"{size:0.#}|{color.PackedValue:X8}|{text}";

        if (TextCache.TryGetValue(key, out var cached))
            return cached;

        var texture = SystemFontText.Render(text, size * ScaleY, color);
        TextCache[key] = texture;

        return texture;
    }

    //a virtual coordinate moved to the nearest native pixel
    private static float Snap(float value, float scale) => MathF.Round(value * scale) / scale;
}
