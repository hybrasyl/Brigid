#region
using Brigid.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Draws and measures UI text. The glyph backend is <see cref="FontEngine" /> (FontStashSharp / TTF); this class
///     owns the higher-level concerns layered on top of it: inline <c>{=x}</c> color codes, drop shadows, per-glyph
///     clipping, line drawing, measurement, and word wrapping. The public API is unchanged from the legacy bitmap-font
///     implementation so existing controls keep calling it verbatim.
/// </summary>
public static class TextRenderer
{
    /// <summary>Legacy nominal character cell width. Retained for column-count layouts; real widths come from measurement.</summary>
    public const int CHAR_WIDTH = 6;

    /// <summary>Line-grid height in pixels. Layout positions each line on this grid; glyph ink size is independent.</summary>
    public const int CHAR_HEIGHT = 12;

    #region Draw Methods
    /// <summary>
    ///     Draws a single line of text. Handles inline <c>{=x}</c> color codes by drawing each color run separately.
    /// </summary>
    public static void DrawText(
        SpriteBatch spriteBatch,
        Vector2 position,
        string text,
        Color color,
        bool colorCodesEnabled = true,
        float opacity = 1f,
        float characterSpacing = 0f,
        int? size = null)
        => DrawCore(spriteBatch, position, text, color, colorCodesEnabled, opacity, null, characterSpacing, size);

    /// <summary>
    ///     Draws a single line in an explicit font style (e.g. <see cref="FontStyle.Bold" />) on the band-centered
    ///     legacy baseline, so a styled run aligns vertically with regular runs. No inline color-code parsing — for
    ///     short single-colour emphasis runs (counts, labels). Pair with the styled <see cref="MeasureWidth(string,
    ///     FontStyle)" /> for alignment.
    /// </summary>
    public static void DrawText(SpriteBatch spriteBatch, Vector2 position, string text, Color color, FontStyle style)
        => FontEngine.Instance.DrawLine(spriteBatch, text, position, color, clip: null, style: style);

    /// <summary>
    ///     Draws text with per-glyph clipping against a clip rectangle. Only called when text partially intersects the
    ///     clip bounds — the common fully-inside case bypasses this.
    /// </summary>
    public static void DrawTextClipped(
        SpriteBatch spriteBatch,
        Vector2 position,
        string text,
        Color color,
        Rectangle clipRect,
        bool colorCodesEnabled = true,
        float opacity = 1f,
        float characterSpacing = 0f,
        int? size = null)
        => DrawCore(spriteBatch, position, text, color, colorCodesEnabled, opacity, clipRect, characterSpacing, size);

    /// <summary>
    ///     Draws a list of text lines top-to-bottom, each on its own row (<see cref="CHAR_HEIGHT" /> line height).
    /// </summary>
    public static void DrawLines(
        SpriteBatch spriteBatch,
        Vector2 position,
        IReadOnlyList<string> lines,
        Color color,
        bool colorCodesEnabled = true)
    {
        var y = position.Y;

        foreach (var line in lines)
        {
            DrawText(spriteBatch, new Vector2(position.X, y), line, color, colorCodesEnabled);
            y += CHAR_HEIGHT;
        }
    }

    /// <summary>
    ///     Draws a visible window of text lines, supporting scrollable text areas.
    /// </summary>
    public static void DrawLines(
        SpriteBatch spriteBatch,
        Vector2 position,
        IReadOnlyList<string> lines,
        int startLine,
        int maxLines,
        Color color,
        bool colorCodesEnabled = true)
    {
        var y = position.Y;
        var endLine = Math.Min(lines.Count, startLine + maxLines);

        for (var i = startLine; i < endLine; i++)
        {
            DrawText(spriteBatch, new Vector2(position.X, y), lines[i], color, colorCodesEnabled);
            y += CHAR_HEIGHT;
        }
    }

    //Shared draw path. Walks the string, emitting one FontEngine line draw per color run so the cursor advance matches
    //MeasureWidth exactly (both sum per-run widths). A null clip draws unclipped.
    private static void DrawCore(
        SpriteBatch spriteBatch,
        Vector2 position,
        string text,
        Color color,
        bool colorCodesEnabled,
        float opacity,
        Rectangle? clip,
        float characterSpacing = 0f,
        int? size = null)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var engine = FontEngine.Instance;
        var px = size ?? FontEngine.Instance.UiSize;
        var cursorX = position.X;
        var y = position.Y;
        var activeColor = opacity < 1f ? color * opacity : color;

        if (!colorCodesEnabled)
        {
            engine.DrawLine(spriteBatch, text, new Vector2(cursorX, y), activeColor, clip, characterSpacing, size: px);

            return;
        }

        var runStart = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (!IsColorCode(text, i))
                continue;

            if (i > runStart)
            {
                var run = text[runStart..i];
                engine.DrawLine(spriteBatch, run, new Vector2(cursorX, y), activeColor, clip, characterSpacing, size: px);
                cursorX += engine.MeasureWidth(run, px) + characterSpacing * run.Length;
            }

            var codeColor = GetColorCode(text[i + 2])!.Value;
            activeColor = opacity < 1f ? codeColor * opacity : codeColor;
            i += 2;
            runStart = i + 1;
        }

        if (runStart < text.Length)
            engine.DrawLine(spriteBatch, text[runStart..], new Vector2(cursorX, y), activeColor, clip, characterSpacing, size: px);
    }
    #endregion

    #region Measurement
    /// <summary>
    ///     Finds the character index at which to break a line to fit within maxWidth pixels. Prefers breaking at the last
    ///     space; falls back to force-breaking mid-word. When colorCodesEnabled is true, {=x} color codes are skipped for
    ///     width measurement (they have zero visual width). When false, they are measured as literal characters.
    /// </summary>
    public static int FindLineBreak(
        string text,
        int maxWidth,
        bool colorCodesEnabled = true,
        int? size = null,
        float extraSpacing = 0f)
    {
        //whole-string fast path: one MeasureString instead of one per character, and it is the measurement the draw
        //actually uses. Autofit exists to make a full-width line fit without wrapping, so this is the dominant case by
        //construction — and re-wrapping the chat backlog on a resize drag pays this per message, per frame.
        //Only valid when color codes are skipped, which is the one thing MeasureWidth always does.
        if (colorCodesEnabled && (MeasureWidth(text, size, extraSpacing) <= maxWidth))
            return text.Length;

        var width = 0;
        var lastSpace = -1;
        var glyphs = 0;
        var i = 0;

        while (i < text.Length)
        {
            if (colorCodesEnabled && IsColorCode(text, i))
            {
                i += 3;

                continue;
            }

            if (text[i] == ' ')
                lastSpace = i;

            //a surrogate pair is one scalar: measure and step it atomically. Breaking between the halves leaves a
            //lone surrogate on each side, and SanitizeSurrogates turns each into U+FFFD — so an emoji that overflows
            //became two replacement glyphs instead of wrapping. Every index this returns is a scalar boundary
            //because the walk only ever advances by whole scalars.
            var step = char.IsHighSurrogate(text[i]) && (i + 1 < text.Length) && char.IsLowSurrogate(text[i + 1])
                ? 2
                : 1;

            //measuring one glyph at a time loses the inter-character tracking that a whole-string measurement (and
            //the draw) applies between glyphs, which over-estimated every wrapped line by ~1px per character and
            //broke lines well before they actually overflowed. Add it back for every glyph after the first.
            width += step == 1
                ? MeasureCharWidth(text[i], size, extraSpacing)
                : FontEngine.Instance.MeasureWidth(
                    text.Substring(i, 2),
                    size ?? FontEngine.Instance.UiSize,
                    FontStyle.Regular,
                    extraSpacing);

            if (glyphs > 0)
                width += (int)(FontEngine.DEFAULT_TRACKING + extraSpacing);

            glyphs++;

            //the floor is `step`, not 1: forcing a break at 1 inside a pair is the very split this guards against
            if (width > maxWidth)
                return lastSpace > 0 ? lastSpace + 1 : Math.Max(step, i);

            i += step;
        }

        return text.Length;
    }

    /// <summary>
    ///     Maps a color code character (the letter after {=) to its Color via the legend palette.
    /// </summary>
    public static Color? GetColorCode(char code)
    {
        var legendColor = LegendPalette.GetTextColor(code);

        if (!legendColor.HasValue)
            return null;

        return LegendColors.Get(legendColor.Value);
    }

    /// <summary>
    ///     Returns true if the text at position i starts a {=x color code sequence.
    /// </summary>
    public static bool IsColorCode(string text, int i)
        => ((i + 2) < text.Length) && (text[i] == '{') && (text[i + 1] == '=') && GetColorCode(text[i + 2]) is not null;

    /// <summary>
    ///     Returns the horizontal pixel advance for a single character, from the active font's metrics.
    /// </summary>
    public static int MeasureCharWidth(char c, int? size = null, float extraSpacing = 0f)
        => FontEngine.Instance.MeasureWidth(
            c.ToString(),
            size ?? FontEngine.Instance.UiSize,
            FontStyle.Regular,
            extraSpacing);

    /// <summary>
    ///     Measures the pixel width of a text string. Skips {=x} color codes (zero visual width). Sums per color run so
    ///     the result matches the cursor advance used while drawing.
    /// </summary>
    public static int MeasureWidth(string text, int? size = null, float extraSpacing = 0f)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var engine = FontEngine.Instance;
        var px = size ?? FontEngine.Instance.UiSize;
        var width = 0;
        var runStart = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (!IsColorCode(text, i))
                continue;

            if (i > runStart)
                width += engine.MeasureWidth(text[runStart..i], px, FontStyle.Regular, extraSpacing);

            i += 2;
            runStart = i + 1;
        }

        if (runStart < text.Length)
            width += engine.MeasureWidth(text[runStart..], px, FontStyle.Regular, extraSpacing);

        return width;
    }

    /// <summary>
    ///     Pixel width of a single-run string in an explicit font style, matching the styled <see cref="DrawText(
    ///     SpriteBatch, Vector2, string, Color, FontStyle)" /> advance. No color-code handling — bold/italic glyphs are
    ///     wider than regular, so alignment must measure in the style it draws.
    /// </summary>
    public static int MeasureWidth(string text, FontStyle style)
        => string.IsNullOrEmpty(text) ? 0 : FontEngine.Instance.MeasureWidth(text, FontEngine.Instance.UiSize, style);

    /// <summary>
    ///     Clips <paramref name="text" /> to at most <paramref name="maxChars" /> characters, appending a single-glyph
    ///     ellipsis ("…") when it overflows so the result never exceeds the budget. One home for the truncation rule so
    ///     list/tab/label sites share the same ellipsis instead of each rolling their own "…" vs "..." variant.
    /// </summary>
    public static string Truncate(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || (text.Length <= maxChars))
            return text;

        return maxChars <= 1 ? "…" : text[..(maxChars - 1)] + "…";
    }
    #endregion

    #region Word Wrapping
    /// <summary>
    ///     Word-wraps text into lines that fit within maxWidth pixels. Splits on explicit newlines, then wraps each
    ///     paragraph by character width.
    /// </summary>
    public static List<string> WrapLines(string text, int maxWidth, int? size = null, float extraSpacing = 0f)
    {
        var lines = new List<string>();

        if ((maxWidth <= 0) || string.IsNullOrEmpty(text))
            return lines;

        foreach (var paragraph in text.Split('\n'))
        {
            var remaining = paragraph;

            while (remaining.Length > 0)
            {
                var lineEnd = FindLineBreak(remaining, maxWidth, size: size, extraSpacing: extraSpacing);

                lines.Add(
                    remaining[..lineEnd]
                        .TrimEnd());

                remaining = remaining[lineEnd..]
                    .TrimStart();
            }

            if (paragraph.Length == 0)
                lines.Add(string.Empty);
        }

        return lines;
    }

    /// <summary>
    ///     Word-wraps text with full escape sequence preprocessing. Handles literal \n, \r, tab collapsing, and splits on
    ///     \r, \n, \t delimiters before word-wrapping each paragraph.
    /// </summary>
    public static List<string> WrapText(string text, int maxWidth, int? size = null, float extraSpacing = 0f)
    {
        var lines = new List<string>();

        //handle literal escape sequences (\n, \r) in addition to actual control characters
        text = text.Replace("\\n", "\n")
                   .Replace("\\r", "\r");

        //collapse consecutive tabs into a single newline
        while (text.Contains("\t\t"))
            text = text.Replace("\t\t", "\t");

        var paragraphs = text.Split('\r', '\n', '\t');
        string? activeColorCode = null;

        foreach (var paragraph in paragraphs)
        {
            if (string.IsNullOrEmpty(paragraph))
            {
                lines.Add(string.Empty);

                continue;
            }

            //inherit the active color code from prior lines so inline {=x} codes persist across wraps/paragraphs
            var remaining = activeColorCode is not null ? activeColorCode + paragraph : paragraph;

            while (remaining.Length > 0)
            {
                var lineEnd = FindLineBreak(remaining, maxWidth, size: size, extraSpacing: extraSpacing);
                var line = remaining[..lineEnd].TrimEnd();
                lines.Add(line);
                activeColorCode = FindLastColorCode(line) ?? activeColorCode;
                remaining = remaining[lineEnd..];

                //only re-prepend the active color code when the prepended length is strictly
                //less than what we just consumed — otherwise FindLineBreak on the next iteration
                //returns the same lineEnd and remaining never shrinks (infinite loop).
                if (activeColorCode is not null && (remaining.Length > 0) && (activeColorCode.Length < lineEnd))
                    remaining = activeColorCode + remaining;
            }
        }

        return lines;
    }

    private static string? FindLastColorCode(string line)
    {
        string? last = null;

        for (var i = 0; i <= (line.Length - 3); i++)
            if (IsColorCode(line, i))
            {
                last = line[i..(i + 3)];
                i += 2;
            }

        return last;
    }
    #endregion
}
