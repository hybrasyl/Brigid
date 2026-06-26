#region
using DALib.Definitions;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using SkiaSharp;
#endregion

namespace Brigid.Data;

/// <summary>
///     Wraps DALib's <see cref="Graphics.RenderImage(SpfFrame, Palette)" /> so that palettized SPF rendering matches the
///     "palette color equals pure black -> transparent" rule that the Colorized path already applies via
///     <see cref="Graphics.RenderImage(SpfFrame)" />. DALib's palettized <c>SimpleRender</c> keys only on palette index 0;
///     SPFs whose background pixels land on non-zero indices that map to <c>(0, 0, 0)</c> (e.g. full-art NPC
///     illustrations in <c>npcbase.dat</c> such as <c>enchant.spf</c>) would otherwise render with an opaque black
///     background. Routing every SPF render in the client through this helper unifies the two paths on the same
///     transparency convention.
/// </summary>
public static class SpfRenderer
{
    public static SKImage RenderFrame(SpfFile spf, int frameIndex)
        => RenderFrameCore(spf[frameIndex], spf.Format, spf.PrimaryColors);

    public static SKImage RenderFrame(SpfView spf, int frameIndex)
        => RenderFrameCore(spf[frameIndex], spf.Format, spf.PrimaryColors);

    private static SKImage RenderFrameCore(SpfFrame frame, SpfFormatType format, Palette? primaryColors)
    {
        if (format == SpfFormatType.Palettized && primaryColors is not null)
            return Graphics.RenderImage(frame, ChromaKeyBlack(primaryColors));

        return Graphics.RenderImage(frame);
    }

    private static Palette ChromaKeyBlack(Palette source)
    {
        var copy = new Palette(source);

        for (var i = 0; i < copy.Count; i++)
        {
            var color = copy[i];

            if (color is { Red: 0, Green: 0, Blue: 0 })
                copy[i] = CONSTANTS.Transparent;
        }

        return copy;
    }
}
