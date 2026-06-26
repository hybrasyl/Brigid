#region
using System.Text;
using Brigid.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Draws text via per-character SpriteBatch calls from the font texture atlas. Supports mixed English (8x12) and
///     Korean (16x12) text via codepage 949, inline {=x color codes, drop shadows, and word wrapping.
/// </summary>
public static class TextRenderer
{
    public const int CHAR_WIDTH = 6;
    public const int CHAR_HEIGHT = 12;

    private const int ENGLISH_GLYPH_WIDTH = 8;
    private const int KOREAN_GLYPH_WIDTH = 16;
    private const int GLYPH_HEIGHT = CHAR_HEIGHT;
    private const int ENGLISH_ADVANCE = ENGLISH_GLYPH_WIDTH - 2;
    private const int KOREAN_ADVANCE = KOREAN_GLYPH_WIDTH - 2;

    private static Encoding? KoreanEncoding;

    private static void EnsureInitialized() => KoreanEncoding ??= Encoding.GetEncoding(949);

    #region Korean Glyph Mapping
    /// <summary>
    ///     Maps a Unicode character to its Korean font glyph index via codepage 949 (EUC-KR).
    /// </summary>
    private static int GetKoreanGlyphIndex(char c)
    {
        Span<char> chars = [c];
        Span<byte> bytes = stackalloc byte[2];

        var count = KoreanEncoding!.GetBytes(chars, bytes);

        if (count != 2)
            return -1;

        var lead = bytes[0];
        var trail = bytes[1];

        //hangul jamo: lead 0xa4, trail 0xa1-0xd3 -> indices 0-50
        if ((lead == 0xA4) && trail is >= 0xA1 and <= 0xD3)
            return trail - 0xA1;

        //hangul syllables: lead 0xb0-0xc8, trail 0xa1-0xfe -> indices 51-2400
        if (lead is >= 0xB0 and <= 0xC8 && trail is >= 0xA1 and <= 0xFE)
            return 51 + (lead - 0xB0) * 94 + (trail - 0xA1);

        return -1;
    }
    #endregion

    #region Draw Methods
    /// <summary>
    ///     Draws a single line of text from the font atlas. Handles inline {=x color codes by changing the vertex color per
    ///     character.
    /// </summary>
    public static void DrawText(
        SpriteBatch spriteBatch,
        Vector2 position,
        string text,
        Color color,
        bool colorCodesEnabled = true,
        float opacity = 1f)
    {
        if (string.IsNullOrEmpty(text))
            return;

        EnsureInitialized();

        var atlas = FontAtlas.Instance;
        var cursorX = position.X;
        var y = position.Y;
        var activeColor = opacity < 1f ? color * opacity : color;

        for (var i = 0; i < text.Length; i++)
        {
            if (colorCodesEnabled && IsColorCode(text, i))
            {
                var codeColor = GetColorCode(text[i + 2])!.Value;
                activeColor = opacity < 1f ? codeColor * opacity : codeColor;
                i += 2;

                continue;
            }

            var c = text[i];

            if (c is >= (char)33 and <= (char)0x17F)
            {
                var info = atlas.GetEnglishGlyphInfo(c);

                if (info.Region.Atlas is not null)
                    spriteBatch.Draw(
                        info.Region.Atlas,
                        new Vector2(cursorX + info.XOffset, y + info.YOffset),
                        info.Region.SourceRect,
                        activeColor);

                cursorX += info.XAdvance > 0 ? info.XAdvance : ENGLISH_ADVANCE;

                continue;
            }

            if (IsKorean(c))
            {
                var koreanIndex = GetKoreanGlyphIndex(c);

                if (koreanIndex >= 0)
                {
                    var glyph = atlas.GetKoreanGlyph(koreanIndex);

                    spriteBatch.Draw(
                        glyph.Atlas,
                        new Vector2(cursorX, y),
                        glyph.SourceRect,
                        activeColor);
                }

                cursorX += KOREAN_ADVANCE;

                continue;
            }

            //space or unmapped — advance without drawing
            cursorX += ENGLISH_ADVANCE;
        }
    }

    /// <summary>
    ///     Draws a single line of text with a dual diagonal drop shadow. The shadow is drawn at (-1,+1) and (+1,+1) relative
    ///     to the main text, matching the original Dark Ages client name tag rendering. The bounding box of the result is
    ///     (MeasureWidth(text) + 2) wide and (CHAR_HEIGHT + 1) tall.
    /// </summary>
    public static void DrawShadowedText(
        SpriteBatch spriteBatch,
        Vector2 position,
        string text,
        Color textColor,
        Color shadowColor,
        bool colorCodesEnabled = true)
    {
        if (string.IsNullOrEmpty(text))
            return;

        //shadow at down-right (+1,+1) and down-left (-1,+1) relative to main text at (1,0) within the bounding box
        DrawText(
            spriteBatch,
            position + new Vector2(2, 1),
            text,
            shadowColor,
            colorCodesEnabled);

        DrawText(
            spriteBatch,
            position + new Vector2(0, 1),
            text,
            shadowColor,
            colorCodesEnabled);

        DrawText(
            spriteBatch,
            position + new Vector2(1, 0),
            text,
            textColor,
            colorCodesEnabled);
    }

    /// <summary>
    ///     Draws text with per-glyph clipping against a clip rectangle. Only called when text
    ///     partially intersects the clip bounds — the common fully-inside case bypasses this.
    /// </summary>
    public static void DrawTextClipped(
        SpriteBatch spriteBatch,
        Vector2 position,
        string text,
        Color color,
        Rectangle clipRect,
        bool colorCodesEnabled = true,
        float opacity = 1f)
    {
        if (string.IsNullOrEmpty(text))
            return;

        EnsureInitialized();

        var atlas = FontAtlas.Instance;
        var cursorX = position.X;
        var y = position.Y;
        var activeColor = opacity < 1f ? color * opacity : color;

        for (var i = 0; i < text.Length; i++)
        {
            if (colorCodesEnabled && IsColorCode(text, i))
            {
                var codeColor = GetColorCode(text[i + 2])!.Value;
                activeColor = opacity < 1f ? codeColor * opacity : codeColor;
                i += 2;

                continue;
            }

            var c = text[i];

            if (c is >= (char)33 and <= (char)0x17F)
            {
                var info = atlas.GetEnglishGlyphInfo(c);

                if (info.Region.Atlas is not null)
                {
                    var glyphPos = new Vector2(cursorX + info.XOffset, y + info.YOffset);
                    var srcRect = info.Region.SourceRect;

                    if (ClipGlyph(ref glyphPos, ref srcRect, in clipRect))
                        spriteBatch.Draw(info.Region.Atlas, glyphPos, srcRect, activeColor);
                }

                cursorX += info.XAdvance > 0 ? info.XAdvance : ENGLISH_ADVANCE;

                continue;
            }

            if (IsKorean(c))
            {
                var koreanIndex = GetKoreanGlyphIndex(c);

                if (koreanIndex >= 0)
                {
                    var glyph = atlas.GetKoreanGlyph(koreanIndex);
                    var glyphPos = new Vector2(cursorX, y);
                    var srcRect = glyph.SourceRect;

                    if (ClipGlyph(ref glyphPos, ref srcRect, in clipRect))
                        spriteBatch.Draw(glyph.Atlas, glyphPos, srcRect, activeColor);
                }

                cursorX += KOREAN_ADVANCE;

                continue;
            }

            cursorX += ENGLISH_ADVANCE;
        }
    }

    /// <summary>
    ///     Clips a single glyph draw against a clip rectangle.
    /// </summary>
    private static bool ClipGlyph(ref Vector2 position, ref Rectangle sourceRect, in Rectangle clipRect)
    {
        var destX = (int)position.X;
        var destY = (int)position.Y;
        var destRight = destX + sourceRect.Width;
        var destBottom = destY + sourceRect.Height;

        if ((destX >= clipRect.Right) || (destRight <= clipRect.X) ||
            (destY >= clipRect.Bottom) || (destBottom <= clipRect.Y))
            return false;

        if ((destX >= clipRect.X) && (destRight <= clipRect.Right) &&
            (destY >= clipRect.Y) && (destBottom <= clipRect.Bottom))
            return true;

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
    ///     Draws a list of text lines top-to-bottom, each on its own row (12px line height).
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
            DrawText(
                spriteBatch,
                new Vector2(position.X, y),
                line,
                color,
                colorCodesEnabled);
            y += GLYPH_HEIGHT;
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
            DrawText(
                spriteBatch,
                new Vector2(position.X, y),
                lines[i],
                color,
                colorCodesEnabled);
            y += GLYPH_HEIGHT;
        }
    }
    #endregion

    #region Measurement
    /// <summary>
    ///     Finds the character index at which to break a line to fit within maxWidth pixels. Prefers breaking at the last
    ///     space; falls back to force-breaking mid-word. When colorCodesEnabled is true, {=x} color codes are skipped for
    ///     width measurement (they have zero visual width). When false, they are measured as literal characters.
    /// </summary>
    public static int FindLineBreak(string text, int maxWidth, bool colorCodesEnabled = true)
    {
        var width = 0;
        var lastSpace = -1;

        for (var i = 0; i < text.Length; i++)
        {
            //skip color codes — they have zero visual width when enabled
            if (colorCodesEnabled && IsColorCode(text, i))
            {
                i += 2;

                continue;
            }

            if (text[i] == ' ')
                lastSpace = i;

            width += MeasureCharWidth(text[i]);

            if (width > maxWidth)
                return lastSpace > 0 ? lastSpace + 1 : Math.Max(1, i);
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

    //Latin-1 Supplement (0x80–0xFF) and Latin Extended-A (0x100–0x17F) are English-side codepoints, not Korean.
    private static bool IsKorean(char c) => c > 0x17F;

    /// <summary>
    ///     Returns the horizontal pixel advance for a single character. English uses the per-glyph advance from the
    ///     active atlas (proportional when a BMFont is loaded, fixed 6px otherwise). Korean is always fixed 14px.
    /// </summary>
    public static int MeasureCharWidth(char c) => IsKorean(c) ? KOREAN_ADVANCE : FontAtlas.Instance.GetEnglishAdvance(c);

    /// <summary>
    ///     Measures the pixel width of a text string. Skips {=x} color codes.
    /// </summary>
    public static int MeasureWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var width = 0;

        for (var i = 0; i < text.Length; i++)
        {
            //skip color codes — they have zero visual width
            if (IsColorCode(text, i))
            {
                i += 2;

                continue;
            }

            width += IsKorean(text[i]) ? KOREAN_ADVANCE : FontAtlas.Instance.GetEnglishAdvance(text[i]);
        }

        return width;
    }
    #endregion

    #region Word Wrapping
    /// <summary>
    ///     Word-wraps text into lines that fit within maxWidth pixels. Splits on explicit newlines, then wraps each paragraph
    ///     by character width.
    /// </summary>
    public static List<string> WrapLines(string text, int maxWidth)
    {
        var lines = new List<string>();

        if ((maxWidth <= 0) || string.IsNullOrEmpty(text))
            return lines;

        foreach (var paragraph in text.Split('\n'))
        {
            var remaining = paragraph;

            while (remaining.Length > 0)
            {
                var lineEnd = FindLineBreak(remaining, maxWidth);

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
    ///     Word-wraps text with full escape sequence preprocessing. Handles literal \n, \r, tab collapsing, and splits on \r,
    ///     \n, \t delimiters before word-wrapping each paragraph.
    /// </summary>
    public static List<string> WrapText(string text, int maxWidth)
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
                var lineEnd = FindLineBreak(remaining, maxWidth);
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