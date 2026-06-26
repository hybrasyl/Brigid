#region
using Brigid.Data;
using SkiaSharp;
#endregion

namespace Brigid.Utilities;

/// <summary>
///     Shared dlgframe.epf 8-piece border compositing utility. Loads the border frames once on first use and provides
///     methods to draw them onto an SKCanvas or produce a fully composited background+border SKImage.
/// </summary>
public static class DialogFrame
{
    //dlgframe.epf frame indices (16×16 each)
    private const int FRAME_TL = 0;
    private const int FRAME_TOP = 1;
    private const int FRAME_TR = 2;
    private const int FRAME_LEFT = 3;
    private const int FRAME_RIGHT = 4;
    private const int FRAME_BL = 5;
    private const int FRAME_BOTTOM = 6;
    private const int FRAME_BR = 7;
    private const int FRAME_COUNT = 8;

    /// <summary>
    ///     The border tile size (16px). Corners are BorderSize×BorderSize, edges are BorderSize wide/tall.
    /// </summary>
    public const int BORDER_SIZE = 16;

    private static SKImage[]? CachedFrames;

    /// <summary>
    ///     Composites a tiled background with the 8-piece border into a single SKImage.
    /// </summary>
    public static SKImage? Composite(SKImage bgTile, int totalWidth, int totalHeight)
    {
        var info = new SKImageInfo(
            totalWidth,
            totalHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        //tile background across the full surface — border draws on top
        for (var tx = 0; tx < totalWidth; tx += bgTile.Width)
            for (var ty = 0; ty < totalHeight; ty += bgTile.Height)
                canvas.DrawImage(bgTile, tx, ty);

        DrawBorder(canvas, totalWidth, totalHeight);

        return surface.Snapshot();
    }

    /// <summary>
    ///     Composites a solid color background with the 8-piece border into a single SKImage.
    /// </summary>
    public static SKImage? Composite(SKColor bgColor, int totalWidth, int totalHeight)
    {
        var info = new SKImageInfo(
            totalWidth,
            totalHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);

        var canvas = surface.Canvas;
        canvas.Clear(bgColor);

        DrawBorder(canvas, totalWidth, totalHeight);

        return surface.Snapshot();
    }

    /// <summary>
    ///     Draws the 8-piece border onto an existing canvas. Tiles edges between corners.
    /// </summary>
    public static void DrawBorder(SKCanvas canvas, int totalWidth, int totalHeight)
    {
        var frames = GetFrames();

        if (frames is null)
            return;

        //top edge (tiled)
        for (var x = BORDER_SIZE; x < (totalWidth - BORDER_SIZE); x += frames[FRAME_TOP].Width)
            canvas.DrawImage(frames[FRAME_TOP], x, 0);

        //bottom edge (tiled)
        for (var x = BORDER_SIZE; x < (totalWidth - BORDER_SIZE); x += frames[FRAME_BOTTOM].Width)
            canvas.DrawImage(frames[FRAME_BOTTOM], x, totalHeight - BORDER_SIZE);

        //left edge (tiled)
        for (var y = BORDER_SIZE; y < (totalHeight - BORDER_SIZE); y += frames[FRAME_LEFT].Height)
            canvas.DrawImage(frames[FRAME_LEFT], 0, y);

        //right edge (tiled)
        for (var y = BORDER_SIZE; y < (totalHeight - BORDER_SIZE); y += frames[FRAME_RIGHT].Height)
            canvas.DrawImage(frames[FRAME_RIGHT], totalWidth - BORDER_SIZE, y);

        //corners (drawn last to cover edge overlap)
        canvas.DrawImage(frames[FRAME_TL], 0, 0);
        canvas.DrawImage(frames[FRAME_TR], totalWidth - BORDER_SIZE, 0);
        canvas.DrawImage(frames[FRAME_BL], 0, totalHeight - BORDER_SIZE);
        canvas.DrawImage(frames[FRAME_BR], totalWidth - BORDER_SIZE, totalHeight - BORDER_SIZE);
    }

    private static SKImage[]? GetFrames()
    {
        if (CachedFrames is not null)
            return CachedFrames;

        var allFrames = DataContext.UserControls.GetEpfImages("dlgframe.epf");

        if (allFrames.Length < FRAME_COUNT)
        {
            foreach (var img in allFrames)
                img.Dispose();

            return null;
        }

        CachedFrames = new SKImage[FRAME_COUNT];
        Array.Copy(allFrames, CachedFrames, FRAME_COUNT);

        for (var i = FRAME_COUNT; i < allFrames.Length; i++)
            allFrames[i]
                .Dispose();

        return CachedFrames;
    }
}