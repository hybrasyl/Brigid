#region
using Brigid.Data;
using Brigid.Data.AssetPacks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Draws the art parts of the active <c>ui_theme</c> pack at native resolution, the same way <see cref="FontEngine" />
///     draws glyphs. The UI pass scales the 640×480 layout to the window with point sampling, so a high-resolution image
///     drawn in a logical rectangle is point-sampled down and looks jagged. Instead, a part is composited with SkiaSharp
///     at the native pixel size of its rectangle, and drawn with the inverse of the pass scale, so one texel lands on one
///     screen pixel.
///     <para>
///         Every draw method returns false when no theme pack is installed or the pack has no usable part for the role.
///         The caller then draws its flat look. Call only from the UI thread.
///     </para>
/// </summary>
public static class ThemeArt
{
    //each role, native size and pass scale is one entry; the bound covers a busy themed screen at two pass scales
    private const int MAX_CACHED_TEXTURES = 256;

    //the live textures stay below this; a full-window part at 3840×2160 is about 33 MB. During a resize, the sizes that
    //the cache evicts wait for dispose too, and EndFrame lets them go when they are over this
    private const long MAX_CACHED_BYTES = 256L * 1024 * 1024;

    private static readonly Dictionary<string, SourcePart?> Sources = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ThemeTextureCache<(string Role, int Width, int Height, float ScaleX, float ScaleY), Texture2D> Textures =
        new(MAX_CACHED_TEXTURES,
            MAX_CACHED_BYTES,
            static texture => (long)texture.Width * texture.Height * 4,
            static () => Environment.TickCount64,
            static texture => texture.Dispose());

    private static UiThemePack? CachedPack;

    //the pack whose fonts EnsureThemeFonts applied, or NoPack when it applied "no pack"; null until the first call
    private static readonly object NoPack = new();
    private static object? FontsAppliedFor;

    /// <summary>Gets the theme colour for <paramref name="role" />, for example <c>text</c>.</summary>
    public static bool TryGetColor(string role, out Color color)
    {
        color = default;

        if (AssetPackRegistry.GetUiThemePack()?.Theme.Colors.TryGetValue(role, out var skColor) != true)
            return false;

        color = new Color(skColor.Red, skColor.Green, skColor.Blue, skColor.Alpha);

        return true;
    }

    /// <summary>
    ///     Gets the widest nine-slice inset of a part in logical pixels, as <see cref="ThemeLayout.LayoutSliceBorder" />
    ///     gives it: clamped to the image, and at most <see cref="ThemeLayout.MAX_SLICE_BORDER" />. A frame's border art
    ///     is that wide, so content inside the frame keeps clear of it. False with no theme, for a part with no slice, and
    ///     for a part whose image is absent or does not decode, because that part draws the flat look.
    /// </summary>
    public static bool TryGetSliceBorder(string role, out int border)
    {
        border = 0;

        if (ActivePack() is not { } pack || GetSource(pack, role) is not { Part.Slice: { } slice } source)
            return false;

        border = ThemeLayout.LayoutSliceBorder(slice, source.Image.Width, source.Image.Height, pack.Theme.Density);

        return true;
    }

    /// <summary>
    ///     True when the installed theme has a part for <paramref name="role" /> whose image entry is present and whose
    ///     header decodes. The pixels decode later, at the first draw.
    /// </summary>
    public static bool HasPart(string role) => ActivePack() is { } pack && GetSource(pack, role) is not null;

    /// <summary>
    ///     Call once at the end of each frame, after the last sprite batch ends. A texture that the cache evicted before
    ///     then is no longer in a batch, so when the textures that wait for dispose are over the memory budget, they go
    ///     at once.
    /// </summary>
    public static void EndFrame() => Textures.EndFrame();

    /// <summary>
    ///     True when the installed theme has a part for <paramref name="role" /> whose image decodes. A part that is listed
    ///     but has no usable image gives false, so that the caller can use its fallback.
    /// </summary>
    public static bool HasUsablePart(string role)
    {
        var pack = AssetPackRegistry.GetUiThemePack();

        if (pack is null)
            return false;

        if (!ReferenceEquals(pack, CachedPack))
            ResetForPack(pack);

        return GetSource(pack, role) is not null;
    }

    /// <summary>
    ///     Draws the part for <paramref name="role" /> into a logical rectangle, clipped to <paramref name="clip" />. When
    ///     the theme has no usable part for the role, it draws <paramref name="fallbackRole" /> instead (for example
    ///     <c>button.normal</c> for a missing <c>button.hover</c>). Call from the native UI pass, where the batch transform
    ///     scales logical space to the window. Returns false, and draws nothing, when neither role is usable.
    /// </summary>
    public static bool TryDraw(
        SpriteBatch spriteBatch,
        string role,
        Rectangle bounds,
        Rectangle clip,
        string? fallbackRole = null,
        Color? tint = null)
    {
        var (scaleX, scaleY) = FontEngine.Instance.NativeScale;
        var native = ThemeLayout.ToNative(bounds, scaleX, scaleY);

        if ((native.Width <= 0) || (native.Height <= 0))
            return false;

        var texture = GetTexture(role, native.Width, native.Height, scaleX, scaleY);

        if (texture is null && fallbackRole is not null)
            texture = GetTexture(fallbackRole, native.Width, native.Height, scaleX, scaleY);

        if (texture is null)
            return false;

        DrawNative(spriteBatch, texture, native, clip, scaleX, scaleY, tint);

        return true;
    }

    /// <summary>
    ///     Draws a texture that was composited at the native size of <paramref name="native" />, clipped to the logical
    ///     <paramref name="clip" />, with the inverse of the pass scale, so one texel lands on one screen pixel.
    /// </summary>
    internal static void DrawNative(
        SpriteBatch spriteBatch,
        Texture2D texture,
        Rectangle native,
        Rectangle clip,
        float scaleX,
        float scaleY,
        Color? tint = null)
    {
        var visible = Rectangle.Intersect(native, ThemeLayout.ToNative(clip, scaleX, scaleY));

        if ((visible.Width <= 0) || (visible.Height <= 0))
            return;

        var source = new Rectangle(visible.X - native.X, visible.Y - native.Y, visible.Width, visible.Height);

        spriteBatch.Draw(
            texture,
            new Vector2(visible.X / scaleX, visible.Y / scaleY),
            source,
            tint ?? Color.White,
            0f,
            Vector2.Zero,
            new Vector2(1f / scaleX, 1f / scaleY),
            SpriteEffects.None,
            0f);
    }

    /// <summary>
    ///     Gives <see cref="FontEngine" /> the theme's <c>title</c> font as the display face, or restores the shipped face
    ///     when the theme has none or its font does not load. Runs once for each theme pack; later calls return at once.
    ///     Call it from an update, not from a draw: a new face bumps <see cref="FontEngine.Generation" />.
    /// </summary>
    public static void EnsureThemeFonts()
    {
        var pack = AssetPackRegistry.GetUiThemePack();
        var applied = (object?)pack ?? NoPack;

        if (ReferenceEquals(FontsAppliedFor, applied))
            return;

        if (pack is not null && pack.TryGetFontBytes(ThemeRoles.TitleFont, out var bytes) && bytes is not null)
        {
            if (!FontEngine.Instance.SetDisplayFont(bytes))
            {
                DataDiagnostics.Log?.Invoke($"[asset-pack] ui_theme '{pack.Manifest.PackId}': font '{ThemeRoles.TitleFont}' does not load; using the shipped face");
                FontEngine.Instance.SetDisplayFont(null);
            }
        } else
            FontEngine.Instance.SetDisplayFont(null);

        FontsAppliedFor = applied;
    }

    private static Texture2D? GetTexture(string role, int width, int height, float scaleX, float scaleY)
    {
        var pack = ActivePack();

        if (pack is null)
            return null;

        return Textures.GetOrCreate(
            (role, width, height, scaleX, scaleY),
            () =>
            {
                var source = GetSource(pack, role);

                if (source is null)
                    return null;

                var theme = pack.Theme;

                using var composite = Composite(
                    source.Part,
                    source.Image,
                    width,
                    height,
                    scaleX / theme.Density,
                    scaleY / theme.Density,
                    source.Part.Filter ?? theme.Filter);

                if (composite is null)
                {
                    DataDiagnostics.Log?.Invoke($"[asset-pack] ui_theme '{pack.Manifest.PackId}': part '{role}' cannot be drawn at {width}x{height}");

                    return null;
                }

                return TextureConverter.ToTexture2D(composite);
            });
    }

    //the active theme pack, or null. A change of theme empties the caches first
    private static UiThemePack? ActivePack()
    {
        var pack = AssetPackRegistry.GetUiThemePack();

        if (pack is not null && !ReferenceEquals(pack, CachedPack))
            ResetForPack(pack);

        return pack;
    }

    private static SourcePart? GetSource(UiThemePack pack, string role)
    {
        if (Sources.TryGetValue(role, out var source))
            return source;

        source = pack.TryGetPartImage(role, out var part, out var image) ? new SourcePart(part!, image!) : null;
        Sources[role] = source;

        return source;
    }

    //sources are only read while a texture is composited, so they can go at once; textures wait out the delay
    private static void ResetForPack(UiThemePack pack)
    {
        Textures.Clear();

        foreach (var source in Sources.Values)
            source?.Image.Dispose();

        Sources.Clear();
        CachedPack = pack;
    }

    /// <summary>
    ///     Composites one part at the output size. <paramref name="artScaleX" /> and <paramref name="artScaleY" /> are
    ///     output pixels for each art pixel; they size the nine-slice insets. Null when SkiaSharp cannot make a surface of
    ///     that size.
    /// </summary>
    internal static SKImage? Composite(
        UiThemePart part,
        SKImage image,
        int width,
        int height,
        float artScaleX,
        float artScaleY,
        UiThemeFilter filter = UiThemeFilter.Linear)
    {
        //the halving steps of Reduce always filter; the filter applies to the last draw, which is where art is enlarged
        var sampling = new SKSamplingOptions(filter == UiThemeFilter.Nearest ? SKFilterMode.Nearest : SKFilterMode.Linear, SKMipmapMode.None);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);

        if (surface is null)
            return null;

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (part.Slice is { } insets)
            DrawNineSlice(canvas, image, insets, part.Tile, width, height, artScaleX, artScaleY, sampling);
        else
            DrawFitted(canvas, image, part.Fit, width, height, sampling);

        return surface.Snapshot();
    }

    private static void DrawNineSlice(
        SKCanvas canvas,
        SKImage image,
        SKRectI insets,
        bool tile,
        int width,
        int height,
        float artScaleX,
        float artScaleY,
        SKSamplingOptions sampling)
    {
        //the layout clamps the insets in the same way (ThemeLayout.LayoutSliceBorder), so the border that it keeps clear
        //is the border that the draw makes
        insets = ThemeLayout.ClampInsets(insets, image.Width, image.Height);

        //reduce the whole image first when it shrinks a lot, so the corners are filtered, not sampled
        using var reduced = Reduce(image, artScaleX, artScaleY, out var reducedScaleX, out var reducedScaleY);
        var sourceImage = reduced ?? image;
        var sourceInsets = reduced is null ? insets : ThemeLayout.ScaleInsets(insets, reducedScaleX, reducedScaleY);
        var destinationInsets = ThemeLayout.ScaleInsets(insets, artScaleX, artScaleY);

        var patches = ThemeLayout.NineSlice(sourceImage.Width, sourceImage.Height, sourceInsets, width, height, destinationInsets);
        using var paint = new SKPaint();
        paint.IsAntialias = false;

        foreach (var patch in patches)
        {
            //each piece is drawn from its own sub-image: a linear sample at the edge of a source rectangle in the full
            //image would blend in the neighbouring piece and show a seam
            using var piece = sourceImage.Subset(SKRectI.Round(patch.Source));

            if (piece is null)
                continue;

            if (!tile || patch.IsCorner)
            {
                canvas.DrawImage(piece, new SKRect(0, 0, piece.Width, piece.Height), patch.Destination, sampling, paint);

                continue;
            }

            //an edge or the centre of a tiled part repeats its piece at the art scale, starting at the piece's top left
            var tileScaleX = reduced is null ? artScaleX : artScaleX / reducedScaleX;
            var tileScaleY = reduced is null ? artScaleY : artScaleY / reducedScaleY;
            var matrix = SKMatrix.CreateScale(tileScaleX, tileScaleY)
                                 .PostConcat(SKMatrix.CreateTranslation(patch.Destination.Left, patch.Destination.Top));

            using var shader = piece.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, sampling, matrix);
            using var tilePaint = new SKPaint();
            tilePaint.Shader = shader;
            canvas.DrawRect(patch.Destination, tilePaint);
        }
    }

    private static void DrawFitted(SKCanvas canvas, SKImage image, UiThemeFit fit, int width, int height, SKSamplingOptions sampling)
    {
        var source = fit == UiThemeFit.Cover
            ? ThemeLayout.CoverSource(image.Width, image.Height, width, height)
            : new SKRect(0, 0, image.Width, image.Height);

        var destination = fit == UiThemeFit.Contain
            ? ThemeLayout.ContainDestination(image.Width, image.Height, width, height)
            : new SKRect(0, 0, width, height);

        using var reduced = Reduce(
            image,
            destination.Width / source.Width,
            destination.Height / source.Height,
            out var reducedScaleX,
            out var reducedScaleY);

        if (reduced is not null)
            source = new SKRect(
                source.Left * reducedScaleX,
                source.Top * reducedScaleY,
                source.Right * reducedScaleX,
                source.Bottom * reducedScaleY);

        using var paint = new SKPaint();
        canvas.DrawImage(reduced ?? image, source, destination, sampling, paint);
    }

    /// <summary>
    ///     Halves the image until the remaining scale on each axis is 0.5 or more. A bilinear sample at exactly half size
    ///     averages each 2×2 block, so the steps filter the image like a box filter, and the last draw does not skip
    ///     source pixels. Returns null when no step is necessary. The out values are the scale of the result relative to
    ///     the input.
    /// </summary>
    internal static SKImage? Reduce(SKImage image, float scaleX, float scaleY, out float reducedScaleX, out float reducedScaleY)
    {
        reducedScaleX = 1f;
        reducedScaleY = 1f;

        var (width, height) = ReducedSize(image.Width, image.Height, scaleX, scaleY);

        if ((width == image.Width) && (height == image.Height))
            return null;

        var current = image;
        SKImage? owned = null;

        while ((current.Width > width) || (current.Height > height))
        {
            var nextWidth = Math.Max(width, (current.Width + 1) / 2);
            var nextHeight = Math.Max(height, (current.Height + 1) / 2);

            var info = new SKImageInfo(nextWidth, nextHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            using var paint = new SKPaint();
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawImage(
                current,
                new SKRect(0, 0, current.Width, current.Height),
                new SKRect(0, 0, nextWidth, nextHeight),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
                paint);

            owned?.Dispose();
            owned = surface.Snapshot();
            current = owned;
        }

        reducedScaleX = (float)current.Width / image.Width;
        reducedScaleY = (float)current.Height / image.Height;

        return owned;
    }

    /// <summary>
    ///     The size that <see cref="Reduce" /> stops at: halve each axis while its remaining scale is below 0.5. Pure, so
    ///     it can be tested without SkiaSharp surfaces.
    /// </summary>
    internal static (int Width, int Height) ReducedSize(int width, int height, float scaleX, float scaleY)
    {
        while ((scaleX < 0.5f) && (width > 1))
        {
            var next = (width + 1) / 2;
            scaleX *= (float)width / next;
            width = next;
        }

        while ((scaleY < 0.5f) && (height > 1))
        {
            var next = (height + 1) / 2;
            scaleY *= (float)height / next;
            height = next;
        }

        return (width, height);
    }

    private sealed record SourcePart(UiThemePart Part, SKImage Image);
}
