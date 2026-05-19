#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.Components;

public abstract class UIElement : IDisposable
{
    private static Texture2D? SharedPixel;

    /// <summary>
    ///     Solid color fill drawn behind this element. Null = no fill.
    /// </summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>
    ///     1px border drawn around this element's bounds. Null = no border.
    /// </summary>
    public Color? BorderColor { get; set; }

    public bool Enabled { get; set; } = true;
    public int Height { get; set; }

    /// <summary>
    ///     When false, the element is skipped during hit-testing. Mouse events pass through
    ///     to whatever is behind it. Default true. Set false for decorative overlays.
    /// </summary>
    public bool IsHitTestVisible { get; set; } = true;

    /// <summary>
    ///     When true on a UIPanel, children are still hit-tested but the panel itself is never
    ///     returned as a hit target. Clicks that miss all children pass through to siblings
    ///     behind this panel. Used for full-screen HUD panels with large transparent areas.
    /// </summary>
    public bool IsPassThrough { get; set; }

    public string Name { get; init; } = string.Empty;
    public int PaddingBottom { get; set; }
    public int PaddingLeft { get; set; }
    public int PaddingRight { get; set; }
    public int PaddingTop { get; set; }
    public UIPanel? Parent { get; internal set; }

    public bool Visible
    {
        get;

        set
        {
            if (field == value)
                return;

            field = value;
            VisibilityChanged?.Invoke(value);
        }
    } = true;

    public int Width { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>
    ///     Draw order within the parent panel. Higher values draw on top. Default 0. Elements with the same ZIndex draw in the
    ///     order they were added.
    /// </summary>
    public int ZIndex
    {
        get;

        set
        {
            if (field == value)
                return;

            field = value;

            Parent?.ChildOrderDirty = true;
        }
    }

    /// <summary>
    ///     Screen-space rectangle this element is allowed to draw within. Intersection of own ScreenBounds with
    ///     Parent.ClipRect. Recomputed at the start of each Draw call.
    /// </summary>
    protected Rectangle ClipRect;

    public Rectangle Bounds
        => new(
            X,
            Y,
            Width,
            Height);

    public Rectangle ScreenBounds
        => new(
            ScreenX,
            ScreenY,
            Width,
            Height);

    // ReSharper disable once FunctionRecursiveOnAllPaths
    public int ScreenX => (Parent?.ScreenX ?? 0) + X;

    // ReSharper disable once FunctionRecursiveOnAllPaths
    public int ScreenY => (Parent?.ScreenY ?? 0) + Y;

    public virtual void Dispose() => GC.SuppressFinalize(this);

    public bool ContainsPoint(int screenX, int screenY) =>
        (screenX >= ClipRect.X) && (screenX < ClipRect.Right) &&
        (screenY >= ClipRect.Y) && (screenY < ClipRect.Bottom);

    /// <summary>
    ///     Draws the element's background fill and border if set. Subclasses should call base.Draw() before drawing their own
    ///     content so the background appears behind everything.
    /// </summary>
    public virtual void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        UpdateClipRect();

        //fully clipped by ancestor — skip all drawing
        if ((ClipRect.Width <= 0) || (ClipRect.Height <= 0))
            return;

        if (BackgroundColor.HasValue || BorderColor.HasValue)
        {
            var bounds = new Rectangle(ScreenX, ScreenY, Width, Height);

            if (BackgroundColor.HasValue)
                DrawRectClipped(spriteBatch, bounds, BackgroundColor.Value);

            if (BorderColor.HasValue)
            {
                DrawRectClipped(spriteBatch, new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), BorderColor.Value);
                DrawRectClipped(spriteBatch, new Rectangle(bounds.X, bounds.Y + bounds.Height - 1, bounds.Width, 1), BorderColor.Value);
                DrawRectClipped(spriteBatch, new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), BorderColor.Value);
                DrawRectClipped(spriteBatch, new Rectangle(bounds.X + bounds.Width - 1, bounds.Y, 1, bounds.Height), BorderColor.Value);
            }
        }
    }

    /// <summary>
    ///     Draws a 1px border rectangle (no fill). Utility for ad-hoc drawing outside the element tree.
    /// </summary>
    public static void DrawBorder(SpriteBatch spriteBatch, Rectangle bounds, Color color)
    {
        var pixel = GetPixel();

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                bounds.X,
                bounds.Y,
                bounds.Width,
                1),
            color);

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                bounds.X,
                bounds.Y + bounds.Height - 1,
                bounds.Width,
                1),
            color);

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                bounds.X,
                bounds.Y,
                1,
                bounds.Height),
            color);

        spriteBatch.Draw(
            pixel,
            new Rectangle(
                bounds.X + bounds.Width - 1,
                bounds.Y,
                1,
                bounds.Height),
            color);
    }

    /// <summary>
    ///     Draws a filled rectangle with a 1px border. Utility for ad-hoc drawing outside the element tree.
    /// </summary>
    public static void DrawBorderedRect(
        SpriteBatch spriteBatch,
        Rectangle bounds,
        Color fillColor,
        Color borderColor)
    {
        DrawRect(spriteBatch, bounds, fillColor);

        DrawBorder(spriteBatch, bounds, borderColor);
    }

    /// <summary>
    ///     Draws a filled rectangle with the given color. Utility for ad-hoc drawing outside the element tree.
    /// </summary>
    public static void DrawRect(SpriteBatch spriteBatch, Rectangle bounds, Color color) => spriteBatch.Draw(GetPixel(), bounds, color);

    /// <summary>
    ///     Returns the shared 1x1 white pixel texture, creating it on first use. Shared across all UI elements — do not
    ///     dispose individually.
    /// </summary>
    public static Texture2D GetPixel()
    {
        if (SharedPixel is null || SharedPixel.IsDisposed)
        {
            SharedPixel = new Texture2D(ChaosGame.Device, 1, 1);
            SharedPixel.SetData([Color.White]);
        }

        return SharedPixel;
    }

    /// <summary>
    ///     Resets transient interaction state (hover, press, drag) so the element appears idle. Called recursively when a
    ///     parent panel is hidden.
    /// </summary>
    public virtual void ResetInteractionState() { }

    /// <summary>
    ///     Advances time-based state: animations, timers, slide positions.
    ///     Called every frame for all visible elements regardless of input focus.
    /// </summary>
    public virtual void Update(GameTime gameTime) { }

    //── clipping ──

    /// <summary>
    ///     Recomputes ClipRect from ScreenBounds intersected with parent's ClipRect. Call at the start of Draw.
    /// </summary>
    protected void UpdateClipRect()
    {
        ClipRect = Parent is not null
            ? Rectangle.Intersect(ScreenBounds, Parent.ClipRect)
            : ScreenBounds;
    }

    /// <summary>
    ///     Clips a texture draw against a clip rectangle. Returns false if fully clipped (nothing to draw).
    ///     On return, position and sourceRect are adjusted to the visible portion.
    /// </summary>
    private static bool ClipTextureRect(
        ref Vector2 position,
        ref Rectangle sourceRect,
        in Rectangle clipRect)
    {
        var destX = (int)position.X;
        var destY = (int)position.Y;
        var destRight = destX + sourceRect.Width;
        var destBottom = destY + sourceRect.Height;

        //fully outside
        if ((destX >= clipRect.Right) || (destRight <= clipRect.X) ||
            (destY >= clipRect.Bottom) || (destBottom <= clipRect.Y))
            return false;

        //fully inside — no adjustment needed
        if ((destX >= clipRect.X) && (destRight <= clipRect.Right) &&
            (destY >= clipRect.Y) && (destBottom <= clipRect.Bottom))
            return true;

        //partially clipped
        var leftClip = Math.Max(0, clipRect.X - destX);
        var topClip = Math.Max(0, clipRect.Y - destY);
        var rightClip = Math.Max(0, destRight - clipRect.Right);
        var bottomClip = Math.Max(0, destBottom - clipRect.Bottom);

        sourceRect = new Rectangle(
            sourceRect.X + leftClip,
            sourceRect.Y + topClip,
            sourceRect.Width - leftClip - rightClip,
            sourceRect.Height - topClip - bottomClip);

        position = new Vector2(destX + leftClip, destY + topClip);

        return (sourceRect.Width > 0) && (sourceRect.Height > 0);
    }

    /// <summary>
    ///     Draws a texture clipped to this element's ClipRect.
    /// </summary>
    protected void DrawTexture(SpriteBatch spriteBatch, Texture2D? texture, Vector2 position, Color color)
    {
        if (texture is null)
            return;

        Texture2D actualTexture;
        var sourceRect = new Rectangle(0, 0, texture.Width, texture.Height);

        if (texture is CachedTexture2D { AtlasRegion: { } region })
        {
            actualTexture = region.Atlas;
            sourceRect = region.SourceRect;
        } else
            actualTexture = texture;

        if (ClipTextureRect(ref position, ref sourceRect, in ClipRect))
            spriteBatch.Draw(actualTexture, position, sourceRect, color);
    }

    /// <summary>
    ///     Draws a texture with a source rectangle, clipped to this element's ClipRect.
    /// </summary>
    protected void DrawTexture(
        SpriteBatch spriteBatch,
        Texture2D? texture,
        Vector2 position,
        Rectangle? sourceRect,
        Color color)
    {
        if (texture is null)
            return;

        Texture2D actualTexture;
        Rectangle resolvedSrc;

        if (texture is CachedTexture2D { AtlasRegion: { } region })
        {
            actualTexture = region.Atlas;

            resolvedSrc = sourceRect.HasValue
                ? new Rectangle(
                    region.SourceRect.X + sourceRect.Value.X,
                    region.SourceRect.Y + sourceRect.Value.Y,
                    sourceRect.Value.Width,
                    sourceRect.Value.Height)
                : region.SourceRect;
        } else
        {
            actualTexture = texture;
            resolvedSrc = sourceRect ?? new Rectangle(0, 0, texture.Width, texture.Height);
        }

        if (ClipTextureRect(ref position, ref resolvedSrc, in ClipRect))
            spriteBatch.Draw(actualTexture, position, resolvedSrc, color);
    }

    /// <summary>
    ///     Draws a filled rectangle clipped to this element's ClipRect.
    /// </summary>
    protected void DrawRectClipped(SpriteBatch spriteBatch, Rectangle bounds, Color color)
    {
        var clipped = Rectangle.Intersect(bounds, ClipRect);

        if ((clipped.Width > 0) && (clipped.Height > 0))
            spriteBatch.Draw(GetPixel(), clipped, color);
    }

    /// <summary>
    ///     Draws single-line text clipped to this element's ClipRect.
    /// </summary>
    protected void DrawTextClipped(
        SpriteBatch spriteBatch,
        Vector2 position,
        string text,
        Color color,
        bool colorCodesEnabled = true,
        float opacity = 1f)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var textWidth = TextRenderer.MeasureWidth(text);
        var textBounds = new Rectangle((int)position.X, (int)position.Y, textWidth, TextRenderer.CHAR_HEIGHT);

        //fully outside
        if (!ClipRect.Intersects(textBounds))
            return;

        //fully inside — fast path
        if (ClipRect.Contains(textBounds))
        {
            TextRenderer.DrawText(spriteBatch, position, text, color, colorCodesEnabled, opacity);

            return;
        }

        //partially clipped — per-glyph clipping
        TextRenderer.DrawTextClipped(spriteBatch, position, text, color, ClipRect, colorCodesEnabled, opacity);
    }

    //── event handlers (dispatched by inputdispatcher) ──

    public virtual void OnMouseDown(MouseDownEvent e) { }
    public virtual void OnMouseUp(MouseUpEvent e) { }
    public virtual void OnClick(ClickEvent e) { }
    public virtual void OnDoubleClick(DoubleClickEvent e) { }
    public virtual void OnMouseMove(MouseMoveEvent e) { }
    public virtual void OnMouseScroll(MouseScrollEvent e) { }
    public virtual void OnMouseEnter() { }
    public virtual void OnMouseLeave() { }
    public virtual void OnKeyDown(KeyDownEvent e) { }
    public virtual void OnKeyUp(KeyUpEvent e) { }
    public virtual void OnTextInput(TextInputEvent e) { }
    public virtual void OnDragStart(DragStartEvent e) { }
    public virtual void OnDragMove(DragMoveEvent e) { }
    public virtual void OnDragDrop(DragDropEvent e) { }

    public event VisibilityChangedHandler? VisibilityChanged;
}