#region
using Chaos.Client.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.Components;

/// <summary>
///     Text label with optional word-wrap, text selection, and color code support. Re-renders only when content or color
///     changes. When WordWrap is true, text wraps to the label width; use ScrollOffset to scroll when ContentHeight exceeds
///     the label bounds.
/// </summary>

// ReSharper disable once ClassCanBeSealed.Global
public class UILabel : UIElement
{
    private readonly TextElement TextElement = new();
    private int CursorPosition;
    private int SelectionAnchor;
    private bool Dragging;
    private long LastClickTime;
    private int LastClickPosition = -1;
    private int ClickCount;

    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;

    public bool IsSelectable { get; set; }
    public bool HasSelection => IsSelectable && (SelectionAnchor != CursorPosition);
    private int SelectionStart => Math.Min(SelectionAnchor, CursorPosition);
    private int SelectionEnd => Math.Max(SelectionAnchor, CursorPosition);

    public bool ColorCodesEnabled
    {
        get => TextElement.ColorCodesEnabled;
        set => TextElement.ColorCodesEnabled = value;
    }

    public Color ForegroundColor
    {
        get => TextElement.Color;
        set => Invalidate(TextElement.Text, value);
    }

    /// <summary>
    ///     Vertical scroll offset in pixels for wrapped text content.
    /// </summary>
    public int ScrollOffset { get; set; }

    public ShadowStyle ShadowStyle { get; set; }

    public string Text
    {
        get => TextElement.Text;
        set => Invalidate(value, TextElement.Color);
    }

    public float Opacity { get; set; } = 1f;
    public VerticalAlignment VerticalAlignment { get; set; }
    public bool TruncateWithEllipsis { get; set; } = true;
    public bool WordWrap { get; set; }

    /// <summary>
    ///     Total pixel height of the rendered content. For wrapped text, this may exceed the label bounds.
    /// </summary>
    public int ContentHeight => TextElement.Height;

    public UILabel()
    {
        PaddingLeft = 1;
        PaddingRight = 1;
        PaddingTop = 1;
        PaddingBottom = 1;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        //empty labels still need a valid ClipRect so ContainsPoint hit-testing works.
        //ClipRect is otherwise only refreshed inside base.Draw, which the next early
        //return would skip.
        if (!TextElement.HasContent)
        {
            UpdateClipRect();

            return;
        }

        base.Draw(spriteBatch);

        var innerX = ScreenX + PaddingLeft;
        var innerY = ScreenY + PaddingTop;
        var innerW = Width - PaddingLeft - PaddingRight;
        var innerH = Height - PaddingTop - PaddingBottom;

        if (HasSelection)
        {
            if (TextElement.WrappedLines is not null)
                DrawWrappedWithSelection(spriteBatch, innerX, innerY, innerH);
            else
                DrawSingleLineWithSelection(spriteBatch, innerX, innerY, innerW, innerH);
        } else if (TextElement.WrappedLines is not null)
        {
            var firstLine = ScrollOffset / TextRenderer.CHAR_HEIGHT;
            var maxLines = (innerH + TextRenderer.CHAR_HEIGHT - 1) / TextRenderer.CHAR_HEIGHT;
            var endLine = Math.Min(TextElement.WrappedLines.Count, firstLine + maxLines);

            for (var lineIdx = firstLine; lineIdx < endLine; lineIdx++)
            {
                var lineY = innerY + (lineIdx - firstLine) * TextRenderer.CHAR_HEIGHT;

                if (TextElement.WrappedLines[lineIdx].Length > 0)
                    TextElement.Draw(spriteBatch, new Vector2(innerX, lineY), ClipRect, TextElement.WrappedLines[lineIdx], Opacity);
            }
        } else if (TruncateWithEllipsis && (TextElement.Width > innerW))
        {
            //ellipsis truncation — find longest prefix that fits with "..."
            var text = TextElement.Text;
            var ellipsisWidth = TextRenderer.MeasureWidth("...");
            var maxTextWidth = innerW - ellipsisWidth;
            var truncLen = text.Length;

            while ((truncLen > 0) && (TextRenderer.MeasureWidth(text[..truncLen]) > maxTextWidth))
                truncLen--;

            var truncated = truncLen > 0 ? text[..truncLen] + "..." : "...";
            TextElement.Draw(spriteBatch, new Vector2(innerX, innerY + (int)(((VerticalAlignment == VerticalAlignment.Top ? TextElement.Height : innerH) - TextRenderer.CHAR_HEIGHT) / 2f)), ClipRect, truncated, Opacity);
        } else
        {
            var bounds = new Rectangle(
                innerX,
                innerY,
                innerW,
                VerticalAlignment == VerticalAlignment.Top ? TextElement.Height : innerH);

            //center alignment clamps to the left edge when the text is wider than the
            //bounds, so overflow clips off the right rather than both sides. Right
            //alignment keeps its natural overflow so clipping stays on the left.
            var textX = HorizontalAlignment switch
            {
                HorizontalAlignment.Center when TextElement.Width <= bounds.Width => bounds.X + (bounds.Width - TextElement.Width) / 2,
                HorizontalAlignment.Right                                         => bounds.X + bounds.Width - TextElement.Width,
                _                                                                 => bounds.X
            };

            var textY = bounds.Y + (bounds.Height - TextElement.Height) / 2;
            var pos = new Vector2(textX, textY);

            TextElement.Draw(spriteBatch, pos, ClipRect, opacity: Opacity);
        }
    }

    private void DrawSingleLineWithSelection(SpriteBatch spriteBatch, int innerX, int innerY, int innerW, int innerH)
    {
        var text = PlainText;
        var selStart = SnapSelectionBoundary(Math.Min(SelectionStart, text.Length));
        var selEnd = Math.Min(SelectionEnd, text.Length);

        var drawX = HorizontalAlignment switch
        {
            HorizontalAlignment.Center when TextElement.Width <= innerW => innerX + (innerW - TextElement.Width) / 2,
            HorizontalAlignment.Right                                   => innerX + innerW - TextElement.Width,
            _                                                           => innerX
        };

        var drawY = innerY + (((VerticalAlignment == VerticalAlignment.Top ? TextElement.Height : innerH) - TextElement.Height) / 2);

        //pre-selection segment
        if (selStart > 0)
            DrawTextClipped(spriteBatch, new Vector2(drawX, drawY), text[..selStart], TextElement.Color, ColorCodesEnabled);

        //selection segment: white rect + black text
        var selStartX = drawX + (selStart > 0 ? TextRenderer.MeasureWidth(text[..selStart]) : 0);
        var selText = text[selStart..selEnd];
        var selWidth = TextRenderer.MeasureWidth(selText);

        DrawRectClipped(spriteBatch, new Rectangle(selStartX, drawY, selWidth, TextRenderer.CHAR_HEIGHT), Color.White);
        DrawTextClipped(spriteBatch, new Vector2(selStartX, drawY), selText, Color.Black, ColorCodesEnabled);

        //post-selection segment
        if (selEnd < text.Length)
        {
            var postX = selStartX + selWidth;
            DrawTextClipped(spriteBatch, new Vector2(postX, drawY), text[selEnd..], TextElement.Color, ColorCodesEnabled);
        }
    }

    private void DrawWrappedWithSelection(SpriteBatch spriteBatch, int innerX, int innerY, int innerH)
    {
        var lines = TextElement.WrappedLines!;
        var firstLine = ScrollOffset / TextRenderer.CHAR_HEIGHT;
        var maxLines = (innerH + TextRenderer.CHAR_HEIGHT - 1) / TextRenderer.CHAR_HEIGHT;
        var endLine = Math.Min(lines.Count, firstLine + maxLines);
        var selStart = SnapSelectionBoundary(SelectionStart);
        var selEnd = SelectionEnd;
        var charOffset = 0;

        //compute character offset up to firstline
        for (var i = 0; i < firstLine; i++)
            charOffset += lines[i].Length;

        for (var i = firstLine; i < endLine; i++)
        {
            var lineText = lines[i];
            var lineY = innerY + (i - firstLine) * TextRenderer.CHAR_HEIGHT;
            var lineStartIdx = charOffset;
            var lineEndIdx = charOffset + lineText.Length;

            if ((selStart < lineEndIdx) && (selEnd > lineStartIdx) && (lineText.Length > 0))
            {
                var hlStart = Math.Max(selStart, lineStartIdx) - lineStartIdx;
                var hlEnd = Math.Min(selEnd, lineEndIdx) - lineStartIdx;

                //pre-selection segment
                if (hlStart > 0)
                    DrawTextClipped(spriteBatch, new Vector2(innerX, lineY), lineText[..hlStart], TextElement.Color, ColorCodesEnabled);

                //selection segment: white rect + black text
                var hlX = innerX + (hlStart > 0 ? TextRenderer.MeasureWidth(lineText[..hlStart]) : 0);
                var hlText = lineText[hlStart..hlEnd];
                var hlWidth = TextRenderer.MeasureWidth(hlText);

                DrawRectClipped(spriteBatch, new Rectangle(hlX, lineY, hlWidth, TextRenderer.CHAR_HEIGHT), Color.White);
                DrawTextClipped(spriteBatch, new Vector2(hlX, lineY), hlText, Color.Black, ColorCodesEnabled);

                //post-selection segment
                if (hlEnd < lineText.Length)
                {
                    var postX = hlX + hlWidth;
                    DrawTextClipped(spriteBatch, new Vector2(postX, lineY), lineText[hlEnd..], TextElement.Color, ColorCodesEnabled);
                }
            } else if (lineText.Length > 0)
                DrawTextClipped(spriteBatch, new Vector2(innerX, lineY), lineText, TextElement.Color, ColorCodesEnabled);

            charOffset = lineEndIdx;
        }
    }

    private string PlainText => TextElement.Text;

    private void Invalidate(string text, Color color)
    {
        //clear selection when text content changes
        if (text != TextElement.Text)
        {
            CursorPosition = 0;
            SelectionAnchor = 0;
        }

        TextElement.WrapWidth = WordWrap ? Width - PaddingLeft - PaddingRight : 0;
        TextElement.ShadowStyle = ShadowStyle;
        TextElement.Update(text, color);
    }

    public override void ResetInteractionState()
    {
        Dragging = false;
        ClickCount = 0;
        CursorPosition = 0;
        SelectionAnchor = 0;
    }

    private void MoveCursor(int newPosition, bool extendSelection)
    {
        if (extendSelection && !HasSelection)
            SelectionAnchor = CursorPosition;

        CursorPosition = Math.Clamp(SnapPastColorCode(newPosition), 0, PlainText.Length);

        if (!extendSelection)
            SelectionAnchor = CursorPosition;
    }

    private int FindWordBoundaryLeft(int from)
    {
        if (from <= 0)
            return 0;

        var text = PlainText;
        var i = from - 1;

        while ((i > 0) && (text[i] == ' '))
            i--;

        while ((i > 0) && (text[i - 1] != ' '))
            i--;

        return SnapPastColorCode(i);
    }

    private int FindWordBoundaryRight(int from)
    {
        var text = PlainText;

        if (from >= text.Length)
            return text.Length;

        var i = from;

        while ((i < text.Length) && (text[i] != ' '))
            i++;

        while ((i < text.Length) && (text[i] == ' '))
            i++;

        return SnapPastColorCode(i);
    }

    /// <summary>
    ///     If position lands inside a {=x} color code, snap forward past it.
    /// </summary>
    private int SnapPastColorCode(int position)
    {
        var text = PlainText;

        if (!ColorCodesEnabled || (position <= 0) || (position >= text.Length))
            return position;

        if ((position >= 2) && TextRenderer.IsColorCode(text, position - 2))
            return Math.Min(position + 1, text.Length);

        if (((position + 1) < text.Length) && TextRenderer.IsColorCode(text, position - 1))
            return Math.Min(position + 2, text.Length);

        return position;
    }

    /// <summary>
    ///     Snaps a selection boundary to the start of a color code if position lands inside one.
    /// </summary>
    private int SnapSelectionBoundary(int position)
    {
        var text = PlainText;

        if (!ColorCodesEnabled || (position <= 0) || (position >= text.Length))
            return position;

        if ((position >= 2) && TextRenderer.IsColorCode(text, position - 2))
            return position - 2;

        if (((position + 1) < text.Length) && TextRenderer.IsColorCode(text, position - 1))
            return position - 1;

        return position;
    }

    /// <summary>
    ///     Moves position left, skipping entirely over any {=x} color code.
    /// </summary>
    private int StepLeft(int position)
    {
        if (position <= 0)
            return 0;

        var text = PlainText;

        if (ColorCodesEnabled && (position >= 3) && TextRenderer.IsColorCode(text, position - 3))
            return position - 3;

        return position - 1;
    }

    /// <summary>
    ///     Moves position right, skipping entirely over any {=x} color code.
    /// </summary>
    private int StepRight(int position)
    {
        var text = PlainText;

        if (position >= text.Length)
            return text.Length;

        if (ColorCodesEnabled && TextRenderer.IsColorCode(text, position))
            return Math.Min(position + 3, text.Length);

        return position + 1;
    }

    /// <summary>
    ///     Strips {=x} color codes from text for clipboard operations.
    /// </summary>
    private static string StripColorCodes(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sb = new System.Text.StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            if (TextRenderer.IsColorCode(text, i))
            {
                i += 2;

                continue;
            }

            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    private int HitTestSingleLine(int mouseX)
    {
        var innerX = ScreenX + PaddingLeft;
        var innerW = Width - PaddingLeft - PaddingRight;

        var drawX = HorizontalAlignment switch
        {
            HorizontalAlignment.Center => innerX + (innerW - TextElement.Width) / 2,
            HorizontalAlignment.Right  => innerX + innerW - TextElement.Width,
            _                    => innerX
        };

        var localX = mouseX - drawX;
        var text = PlainText;

        if ((text.Length == 0) || (localX <= 0))
            return 0;

        var prevWidth = 0;

        for (var i = 0; i < text.Length;)
        {
            //skip color codes as atomic units
            if (ColorCodesEnabled && TextRenderer.IsColorCode(text, i))
            {
                i += 3;

                continue;
            }

            var nextI = i + 1;
            var charWidth = prevWidth + TextRenderer.MeasureCharWidth(text[i]);
            var midpoint = (prevWidth + charWidth) / 2;

            if (localX < midpoint)
                return i;

            prevWidth = charWidth;
            i = nextI;
        }

        return text.Length;
    }

    private int HitTestWrapped(int mouseX, int mouseY)
    {
        var lines = TextElement.WrappedLines;

        if (lines is null || (lines.Count == 0))
            return 0;

        var innerY = ScreenY + PaddingTop;
        var firstLine = ScrollOffset / TextRenderer.CHAR_HEIGHT;
        var localY = mouseY - innerY;
        var clickLine = firstLine + localY / TextRenderer.CHAR_HEIGHT;
        clickLine = Math.Clamp(clickLine, 0, lines.Count - 1);

        var charOffset = 0;

        for (var i = 0; i < clickLine; i++)
            charOffset += lines[i].Length;

        var lineText = lines[clickLine];
        var localX = mouseX - ScreenX - PaddingLeft;

        if ((lineText.Length == 0) || (localX <= 0))
            return charOffset;

        var prevWidth = 0;

        for (var i = 0; i < lineText.Length;)
        {
            //skip color codes as atomic units
            if (ColorCodesEnabled && TextRenderer.IsColorCode(lineText, i))
            {
                i += 3;

                continue;
            }

            var nextI = i + 1;
            var charWidth = prevWidth + TextRenderer.MeasureCharWidth(lineText[i]);
            var midpoint = (prevWidth + charWidth) / 2;

            if (localX < midpoint)
                return charOffset + i;

            prevWidth = charWidth;
            i = nextI;
        }

        return charOffset + lineText.Length;
    }

    private int GetWrappedLineForPosition(int position)
    {
        var lines = TextElement.WrappedLines;

        if (lines is null)
            return 0;

        var charOffset = 0;

        for (var i = 0; i < lines.Count; i++)
        {
            if (position < (charOffset + lines[i].Length))
                return i;

            charOffset += lines[i].Length;
        }

        return Math.Max(0, lines.Count - 1);
    }

    private int GetWrappedLineStart(int lineIndex)
    {
        var lines = TextElement.WrappedLines;

        if (lines is null)
            return 0;

        var offset = 0;

        for (var i = 0; i < lineIndex; i++)
            offset += lines[i].Length;

        return offset;
    }

    private void ClampPositions()
    {
        var len = PlainText.Length;

        if (CursorPosition > len)
            CursorPosition = len;

        if (SelectionAnchor > len)
            SelectionAnchor = len;
    }

    public override void OnMouseDown(MouseDownEvent e)
    {
        if (!IsSelectable || (e.Button != MouseButton.Left))
            return;

        ClampPositions();

        var isWrapped = TextElement.WrappedLines is not null;

        var clickPos = isWrapped
            ? HitTestWrapped(e.ScreenX, e.ScreenY)
            : HitTestSingleLine(e.ScreenX);

        var now = Environment.TickCount64;

        if (((now - LastClickTime) < 400) && (clickPos == LastClickPosition))
            ClickCount++;
        else
            ClickCount = 1;

        LastClickTime = now;
        LastClickPosition = clickPos;

        if (ClickCount == 3)
        {
            if (isWrapped)
            {
                var line = GetWrappedLineForPosition(clickPos);
                SelectionAnchor = GetWrappedLineStart(line);
                CursorPosition = SelectionAnchor + TextElement.WrappedLines![line].Length;
            } else
            {
                SelectionAnchor = 0;
                CursorPosition = PlainText.Length;
            }

            ClickCount = 0;
        } else if (ClickCount == 2)
        {
            SelectionAnchor = FindWordBoundaryLeft(clickPos);
            CursorPosition = FindWordBoundaryRight(clickPos);
        } else if (e.Shift)
            CursorPosition = clickPos;
        else
        {
            CursorPosition = clickPos;
            SelectionAnchor = clickPos;
        }

        Dragging = true;
        e.Handled = true;
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        if (!IsSelectable || !Dragging)
            return;

        var isWrapped = TextElement.WrappedLines is not null;

        var dragPos = isWrapped
            ? HitTestWrapped(e.ScreenX, e.ScreenY)
            : HitTestSingleLine(e.ScreenX);

        CursorPosition = dragPos;
    }

    public override void OnMouseUp(MouseUpEvent e)
    {
        if (e.Button == MouseButton.Left)
            Dragging = false;
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (!IsSelectable)
            return;

        ClampPositions();

        var shift = e.Shift;
        var ctrl = e.Ctrl;
        var isWrapped = TextElement.WrappedLines is not null;

        switch (e.Key)
        {
            case Keys.Left:
                if (!shift && HasSelection)
                    MoveCursor(SelectionStart, false);
                else if (CursorPosition > 0)
                    MoveCursor(ctrl ? FindWordBoundaryLeft(CursorPosition) : StepLeft(CursorPosition), shift);

                e.Handled = true;

                break;

            case Keys.Right:
                if (!shift && HasSelection)
                    MoveCursor(SelectionEnd, false);
                else if (CursorPosition < PlainText.Length)
                    MoveCursor(ctrl ? FindWordBoundaryRight(CursorPosition) : StepRight(CursorPosition), shift);

                e.Handled = true;

                break;

            case Keys.Up when isWrapped:
            {
                var line = GetWrappedLineForPosition(CursorPosition);

                if (line > 0)
                {
                    var lineStart = GetWrappedLineStart(line);
                    var col = CursorPosition - lineStart;
                    var prevLineStart = GetWrappedLineStart(line - 1);
                    var prevLineLen = TextElement.WrappedLines![line - 1].Length;
                    MoveCursor(prevLineStart + Math.Min(col, prevLineLen), shift);
                }

                e.Handled = true;

                break;
            }

            case Keys.Down when isWrapped:
            {
                var lines = TextElement.WrappedLines!;
                var line = GetWrappedLineForPosition(CursorPosition);

                if ((line + 1) < lines.Count)
                {
                    var lineStart = GetWrappedLineStart(line);
                    var col = CursorPosition - lineStart;
                    var nextLineStart = GetWrappedLineStart(line + 1);
                    var nextLineLen = lines[line + 1].Length;
                    MoveCursor(nextLineStart + Math.Min(col, nextLineLen), shift);
                }

                e.Handled = true;

                break;
            }

            case Keys.Home:
                if (isWrapped && !ctrl)
                {
                    var line = GetWrappedLineForPosition(CursorPosition);
                    MoveCursor(GetWrappedLineStart(line), shift);
                } else
                    MoveCursor(0, shift);

                e.Handled = true;

                break;

            case Keys.End:
                if (isWrapped && !ctrl)
                {
                    var line = GetWrappedLineForPosition(CursorPosition);
                    MoveCursor(GetWrappedLineStart(line) + TextElement.WrappedLines![line].Length, shift);
                } else
                    MoveCursor(PlainText.Length, shift);

                e.Handled = true;

                break;

            case Keys.A when ctrl && (PlainText.Length > 0):
                SelectionAnchor = 0;
                CursorPosition = PlainText.Length;
                e.Handled = true;

                break;

            case Keys.C when ctrl && HasSelection:
                Clipboard.SetText(StripColorCodes(PlainText[SelectionStart..Math.Min(SelectionEnd, PlainText.Length)]));
                e.Handled = true;

                break;
        }
    }
}