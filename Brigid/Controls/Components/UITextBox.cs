#region
using Brigid.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.Components;

// ReSharper disable once ClassCanBeSealed.Global
public class UITextBox : UIElement
{
    private const int CURSOR_BLINK_MS = 530;
    private const int CURSOR_WIDTH = 1;
    private const long TRIPLE_CLICK_MS = 400;

    private static UITextBox? FocusedTextBox;

    private readonly TextElement TextElement = new();
    private string CachedLayoutText = string.Empty;
    private int CachedLayoutWidth;
    private int ClickCount;
    private double CursorTimer;
    private bool CursorVisible;
    private bool Dragging;
    private long LastClickTime;
    private int LastClickPosition = -1;
    private List<int> LineStarts = [0];
    private int SelectionAnchor;
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>
    ///     When true, prevents text input that would cause the content to exceed the visible area. Only applies to multiline
    ///     text boxes. Uses the computed line layout to determine whether content fits.
    /// </summary>
    public bool ClampToVisibleArea { get; set; }

    /// <summary>
    ///     When true, inline {=x} color codes in the text are parsed and rendered with the specified color. Default true.
    /// </summary>
    public bool ColorCodesEnabled
    {
        get => TextElement.ColorCodesEnabled;
        set => TextElement.ColorCodesEnabled = value;
    }

    public int CursorPosition { get; internal set; }

    /// <summary>
    ///     Background color drawn behind the textbox when focused. Null = no overlay.
    /// </summary>
    public Color? FocusedBackgroundColor { get; set; }

    public Color ForegroundColor { get; set; } = LegendColors.Silver;

    public bool IsFocused
    {
        get;

        set
        {
            if (field == value)
                return;

            field = value;
            BackgroundColor = value ? FocusedBackgroundColor : null;

            if (value)
            {
                if (FocusedTextBox is not null && (FocusedTextBox != this))
                    FocusedTextBox.IsFocused = false;

                FocusedTextBox = this;
                TextBoxFocusGained?.Invoke(this);
            } else if (FocusedTextBox == this)
                FocusedTextBox = null;
        }
    }

    /// <summary>
    ///     Fired when any UITextBox gains focus via IsFocused becoming true.
    ///     The InputDispatcher subscribes to this to keep keyboard routing in sync.
    /// </summary>
    public static event TextBoxFocusHandler? TextBoxFocusGained;

    public bool IsMasked { get; set; }
    public bool IsMultiLine { get; set; }

    public bool IsReadOnly { get; set; }

    /// <summary>
    ///     When true, Tab key cycles focus to the next sibling UITextBox with IsTabStop=true
    ///     instead of inserting a tab character. The parent panel handles the actual cycling.
    /// </summary>
    public bool IsTabStop { get; set; }
    public bool IsSelectable { get; set; } = true;

    public int MaxLength { get; set; } = 12;

    /// <summary>
    ///     Non-editable prefix rendered before the editable text (e.g. "Name: " for chat). Not included in <see cref="Text" />
    ///     and cannot be deleted by the user.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    public int ScrollOffset { get; set; }

    //horizontal pixel scroll for single-line boxes: keeps the caret in view when the text is wider than the box,
    //instead of hard-rejecting characters that would overflow the visible width (which silently dropped pasted tails).
    private int HScrollOffset;

    public string Text { get; set; } = string.Empty;

    public static bool IsAnyFocused
    {
        get
        {
            if (FocusedTextBox is null)
                return false;

            //verify the focused text box is still effectively visible
            //(its own visible flag and all ancestors are visible)
            if (!IsEffectivelyVisible(FocusedTextBox))
            {
                FocusedTextBox.IsFocused = false;

                return false;
            }

            return true;
        }
    }

    private int FirstVisibleLine => ScrollOffset / TextRenderer.CHAR_HEIGHT;

    /// <summary>
    ///     Number of laid-out lines in the current text (multiline only; 1 when empty).
    /// </summary>
    public int LineCount => LineStarts.Count;

    public bool HasSelection => IsSelectable && (SelectionAnchor != CursorPosition);

    public string SelectedText => HasSelection ? Text[SelectionStart..Math.Min(SelectionEnd, Text.Length)] : string.Empty;

    public int SelectionEnd => Math.Max(SelectionAnchor, CursorPosition);
    public int SelectionLength => SelectionEnd - SelectionStart;

    public int SelectionStart => Math.Min(SelectionAnchor, CursorPosition);

    public int VisibleLineCount => (Height - PaddingTop + PaddingBottom) / TextRenderer.CHAR_HEIGHT;

    public UITextBox()
    {
        PaddingLeft = 2;
        PaddingRight = 2;
        PaddingTop = 2;
        PaddingBottom = 2;
    }

    /// <summary>
    ///     Ensures cursor and anchor are within valid range after external Text changes.
    /// </summary>
    private void ClampPositions()
    {
        var len = Text.Length;

        if (CursorPosition > len)
            CursorPosition = len;

        if (SelectionAnchor > len)
            SelectionAnchor = len;
    }

    private void ComputeLineLayout()
    {
        var innerWidth = Width - PaddingLeft + PaddingRight;

        if ((innerWidth == CachedLayoutWidth) && (Text == CachedLayoutText))
            return;

        CachedLayoutWidth = innerWidth;
        CachedLayoutText = Text;
        LineStarts = [0];

        if (string.IsNullOrEmpty(Text) || (innerWidth <= 0))
            return;

        var pos = 0;

        while (pos <= Text.Length)
        {
            var nlIndex = Text.IndexOf('\n', pos);
            var paraEnd = nlIndex < 0 ? Text.Length : nlIndex;
            var para = Text[pos..paraEnd];

            if (para.Length == 0)
            {
                pos = paraEnd + 1;

                if ((nlIndex >= 0) && (pos <= Text.Length))
                    LineStarts.Add(pos);

                continue;
            }

            var paraOffset = pos;
            var remaining = para;

            while (remaining.Length > 0)
            {
                var lineEnd = TextRenderer.FindLineBreak(remaining, innerWidth, ColorCodesEnabled);
                var consumed = lineEnd;

                while ((consumed < remaining.Length) && (remaining[consumed] == ' '))
                    consumed++;

                remaining = remaining[consumed..];
                paraOffset += consumed;

                if (remaining.Length > 0)
                    LineStarts.Add(paraOffset);
            }

            pos = paraEnd + 1;

            if ((nlIndex >= 0) && (pos <= Text.Length))
                LineStarts.Add(pos);
        }

        //ensure trailing \n produces a final empty line
        if ((Text.Length > 0) && (Text[^1] == '\n') && (LineStarts[^1] != Text.Length))
            LineStarts.Add(Text.Length);
    }

    public void ClearSelection() => SelectionAnchor = CursorPosition;

    private void DeleteSelection()
    {
        if (!HasSelection)
            return;

        var start = SelectionStart;
        var length = Math.Min(SelectionLength, Text.Length - start);

        Text = Text.Remove(start, length);
        CursorPosition = start;
        SelectionAnchor = start;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        if (IsMultiLine)
            DrawMultiLine(spriteBatch);
        else
            DrawSingleLine(spriteBatch);
    }

    private void DrawMultiLine(SpriteBatch spriteBatch)
    {
        var sx = ScreenX;
        var sy = ScreenY;
        var textX = sx + PaddingLeft;
        var textY = sy + PaddingTop;
        var firstLine = FirstVisibleLine;
        var visibleCount = VisibleLineCount;
        var lastLine = Math.Min(firstLine + visibleCount, LineStarts.Count);
        var selStart = SnapSelectionBoundary(SelectionStart);
        var selEnd = SelectionEnd;

        for (var i = firstLine; i < lastLine; i++)
        {
            var lineText = GetLineText(i);
            var lineY = textY + (i - firstLine) * TextRenderer.CHAR_HEIGHT;
            var lineStartIdx = LineStarts[i];
            var lineEndIdx = lineStartIdx + lineText.Length;

            if (HasSelection && (selStart < lineEndIdx) && (selEnd > lineStartIdx) && (lineText.Length > 0))
            {
                var hlStart = Math.Max(selStart, lineStartIdx) - lineStartIdx;
                var hlEnd = Math.Min(selEnd, lineEndIdx) - lineStartIdx;

                //pre-selection segment
                if (hlStart > 0)
                    DrawTextClipped(spriteBatch, new Vector2(textX, lineY), lineText[..hlStart], ForegroundColor, ColorCodesEnabled);

                //selection segment: white rect + black text
                var hlX = textX + (hlStart > 0 ? TextRenderer.MeasureWidth(lineText[..hlStart]) : 0);
                var hlText = lineText[hlStart..hlEnd];
                var hlWidth = TextRenderer.MeasureWidth(hlText);

                DrawRectClipped(spriteBatch, new Rectangle(hlX, lineY, hlWidth, TextRenderer.CHAR_HEIGHT), Color.White);
                DrawTextClipped(spriteBatch, new Vector2(hlX, lineY), hlText, Color.Black, ColorCodesEnabled);

                //post-selection segment
                if (hlEnd < lineText.Length)
                {
                    var postX = hlX + hlWidth;
                    DrawTextClipped(spriteBatch, new Vector2(postX, lineY), lineText[hlEnd..], ForegroundColor, ColorCodesEnabled);
                }
            } else if (lineText.Length > 0)
                DrawTextClipped(
                    spriteBatch,
                    new Vector2(textX, lineY),
                    lineText,
                    ForegroundColor,
                    ColorCodesEnabled);
        }

        if (!IsFocused || !CursorVisible || IsReadOnly)
            return;

        var cursorLine = GetLineForPosition(CursorPosition);

        if ((cursorLine >= firstLine) && (cursorLine < lastLine))
        {
            var cLineText = GetLineText(cursorLine);
            var colOffset = Math.Min(CursorPosition - LineStarts[cursorLine], cLineText.Length);
            var cursorX = textX + (colOffset > 0 ? TextRenderer.MeasureWidth(cLineText[..colOffset]) : 0);
            var cursorY = textY + (cursorLine - firstLine) * TextRenderer.CHAR_HEIGHT;

            DrawRectClipped(
                spriteBatch,
                new Rectangle(
                    cursorX,
                    cursorY,
                    CURSOR_WIDTH,
                    TextRenderer.CHAR_HEIGHT),
                ForegroundColor);
        }
    }

    private void DrawSingleLine(SpriteBatch spriteBatch)
    {
        var displayText = IsMasked ? new string('*', Text.Length) : Text;
        var sx = ScreenX;
        var sy = ScreenY;
        var textY = sy + PaddingTop;
        var textHeight = Height - PaddingTop + PaddingBottom;

        //prefix offset — non-editable text rendered before editable content
        var prefixWidth = 0;

        if ((Prefix.Length > 0) && IsFocused)
        {
            prefixWidth = TextRenderer.MeasureWidth(Prefix);
            DrawTextClipped(spriteBatch, new Vector2(sx + PaddingLeft, textY), Prefix, ForegroundColor);
        }

        //when focused, scroll horizontally so the caret stays visible; unfocused boxes render from the start (or via
        //the alignment branch below), matching prior behavior.
        var availableWidth = Width - PaddingLeft - PaddingRight - prefixWidth;

        if (IsFocused)
            UpdateHorizontalScroll(displayText, availableWidth);
        else
            HScrollOffset = 0;

        var textStartX = sx + PaddingLeft + prefixWidth - HScrollOffset;

        if (HasSelection && (displayText.Length > 0))
        {
            var selStart = SnapSelectionBoundary(Math.Min(SelectionStart, displayText.Length));
            var selEnd = Math.Min(SelectionEnd, displayText.Length);

            //pre-selection segment
            if (selStart > 0)
                DrawTextClipped(spriteBatch, new Vector2(textStartX, textY), displayText[..selStart], ForegroundColor, ColorCodesEnabled);

            //selection segment: white rect + black text
            var selStartX = textStartX + (selStart > 0 ? TextRenderer.MeasureWidth(displayText[..selStart]) : 0);
            var selText = displayText[selStart..selEnd];
            var selWidth = TextRenderer.MeasureWidth(selText);

            DrawRectClipped(spriteBatch, new Rectangle(selStartX, textY, selWidth, TextRenderer.CHAR_HEIGHT), Color.White);
            DrawTextClipped(spriteBatch, new Vector2(selStartX, textY), selText, Color.Black, ColorCodesEnabled);

            //post-selection segment
            if (selEnd < displayText.Length)
            {
                var postX = selStartX + selWidth;
                DrawTextClipped(spriteBatch, new Vector2(postX, textY), displayText[selEnd..], ForegroundColor, ColorCodesEnabled);
            }
        } else
        {
            TextElement.Update(displayText, ForegroundColor);

            if ((HorizontalAlignment != HorizontalAlignment.Left) && !IsFocused)
            {
                var alignBounds = new Rectangle(
                    sx + PaddingLeft,
                    textY,
                    Width - PaddingLeft + PaddingRight,
                    textHeight);

                var alignX = HorizontalAlignment switch
                {
                    HorizontalAlignment.Center => alignBounds.X + (alignBounds.Width - TextElement.Width) / 2,
                    HorizontalAlignment.Right  => alignBounds.X + alignBounds.Width - TextElement.Width,
                    _                    => alignBounds.X
                };

                var alignY = alignBounds.Y + (alignBounds.Height - TextElement.Height) / 2;
                DrawTextClipped(spriteBatch, new Vector2(alignX, alignY), displayText, ForegroundColor, ColorCodesEnabled);
            } else
                DrawTextClipped(spriteBatch, new Vector2(textStartX, textY), displayText, ForegroundColor, ColorCodesEnabled);
        }

        if (!IsFocused || !CursorVisible || IsReadOnly)
            return;

        var cursorX = textStartX;
        var clampedPos = Math.Min(CursorPosition, displayText.Length);

        if (clampedPos > 0)
            cursorX += TextRenderer.MeasureWidth(displayText[..clampedPos]);

        DrawRectClipped(
            spriteBatch,
            new Rectangle(
                cursorX,
                textY,
                CURSOR_WIDTH,
                TextRenderer.CHAR_HEIGHT),
            ForegroundColor);
    }

    /// <summary>
    ///     Nudges <see cref="HScrollOffset" /> so the caret stays within the visible width of a single-line box. Only
    ///     scrolls when the caret would fall outside the window, so a stable view is kept while editing mid-text; clamps
    ///     back to 0 whenever the whole text fits.
    /// </summary>
    private void UpdateHorizontalScroll(string displayText, int availableWidth)
    {
        if (availableWidth <= 0)
        {
            HScrollOffset = 0;

            return;
        }

        var clampedPos = Math.Min(CursorPosition, displayText.Length);
        var caretPixel = clampedPos > 0 ? TextRenderer.MeasureWidth(displayText[..clampedPos]) : 0;

        if (caretPixel < HScrollOffset)
            HScrollOffset = caretPixel;
        else if (caretPixel > (HScrollOffset + availableWidth - CURSOR_WIDTH))
            HScrollOffset = caretPixel - availableWidth + CURSOR_WIDTH;

        //never scroll past the end or leave a gap once the text fits within the box
        var maxOffset = Math.Max(0, TextRenderer.MeasureWidth(displayText) - availableWidth + CURSOR_WIDTH);
        HScrollOffset = Math.Clamp(HScrollOffset, 0, maxOffset);
    }

    private void EnsureCursorVisible()
    {
        var cursorLine = GetLineForPosition(CursorPosition);
        var firstVisible = FirstVisibleLine;
        var visibleCount = VisibleLineCount;

        if (visibleCount <= 0)
            return;

        if (cursorLine < firstVisible)
            ScrollOffset = cursorLine * TextRenderer.CHAR_HEIGHT;
        else if (cursorLine >= (firstVisible + visibleCount))
            ScrollOffset = (cursorLine - visibleCount + 1) * TextRenderer.CHAR_HEIGHT;
    }

    /// <summary>
    ///     Forces a layout recompute and returns true if the content exceeds the visible line count. Only meaningful when both
    ///     <see cref="ClampToVisibleArea" /> and <see cref="IsMultiLine" /> are true.
    /// </summary>
    private bool ExceedsVisibleArea()
    {
        //invalidate cached layout to force recomputation
        CachedLayoutText = string.Empty;
        ComputeLineLayout();

        return LineStarts.Count > VisibleLineCount;
    }

    private int FindWordBoundaryLeft(int from)
    {
        if (from <= 0)
            return 0;

        var i = from - 1;

        while ((i > 0) && (Text[i] == ' '))
            i--;

        while ((i > 0) && (Text[i - 1] != ' '))
            i--;

        return SnapPastColorCode(i);
    }

    private int FindWordBoundaryRight(int from)
    {
        if (from >= Text.Length)
            return Text.Length;

        var i = from;

        while ((i < Text.Length) && (Text[i] != ' '))
            i++;

        while ((i < Text.Length) && (Text[i] == ' '))
            i++;

        return SnapPastColorCode(i);
    }

    /// <summary>
    ///     If position lands inside a {=x} color code, snap forward past it.
    ///     Returns position unchanged if not inside a color code.
    /// </summary>
    private int SnapPastColorCode(int position)
    {
        if (!ColorCodesEnabled || (position <= 0) || (position >= Text.Length))
            return position;

        //at index 2 of a 3-char {=x} code (position - 2 is the '{')
        if ((position >= 2) && TextRenderer.IsColorCode(Text, position - 2))
            return Math.Min(position + 1, Text.Length);

        //at index 1 of a 3-char {=x} code (position - 1 is the '{')
        if (((position + 1) < Text.Length) && TextRenderer.IsColorCode(Text, position - 1))
            return Math.Min(position + 2, Text.Length);

        return position;
    }

    /// <summary>
    ///     Snaps a selection boundary to not split a color code. If position lands inside
    ///     a {=x} code, adjusts it to the start of the code (for segment splitting).
    /// </summary>
    private int SnapSelectionBoundary(int position)
    {
        if (!ColorCodesEnabled || (position <= 0) || (position >= Text.Length))
            return position;

        //at index 2 of a 3-char {=x} code
        if ((position >= 2) && TextRenderer.IsColorCode(Text, position - 2))
            return position - 2;

        //at index 1 of a 3-char {=x} code
        if (((position + 1) < Text.Length) && TextRenderer.IsColorCode(Text, position - 1))
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

        //if the character before us is the end of a color code, skip the whole code
        if (ColorCodesEnabled && (position >= 3) && TextRenderer.IsColorCode(Text, position - 3))
            return position - 3;

        return position - 1;
    }

    /// <summary>
    ///     Moves position right, skipping entirely over any {=x} color code.
    /// </summary>
    private int StepRight(int position)
    {
        if (position >= Text.Length)
            return Text.Length;

        //if we're at the start of a color code, skip the whole code
        if (ColorCodesEnabled && TextRenderer.IsColorCode(Text, position))
            return Math.Min(position + 3, Text.Length);

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

    private int GetLineForPosition(int position)
    {
        for (var i = LineStarts.Count - 1; i >= 0; i--)
            if (position >= LineStarts[i])
                return i;

        return 0;
    }

    private string GetLineText(int lineIndex)
    {
        if ((lineIndex < 0) || (lineIndex >= LineStarts.Count))
            return string.Empty;

        var start = LineStarts[lineIndex];
        int end;

        if ((lineIndex + 1) < LineStarts.Count)
        {
            end = LineStarts[lineIndex + 1];

            if ((end > start) && (end <= Text.Length) && (Text[end - 1] == '\n'))
                end--;
        } else
            end = Text.Length;

        while ((end > start) && (Text[end - 1] == ' '))
            end--;

        return Text[start..end];
    }

    private void HandleBackspace()
    {
        if (HasSelection)
            DeleteSelection();
        else if (CursorPosition > 0)
        {
            //delete the entire color code atomically if the cursor is right after one
            var stepPos = StepLeft(CursorPosition);
            var deleteCount = CursorPosition - stepPos;
            Text = Text.Remove(stepPos, deleteCount);
            CursorPosition = stepPos;
            SelectionAnchor = stepPos;
        }

        ResetCursor();
    }

    private int HitTestCursorPosition(int localX)
    {
        //offset by prefix width when focused
        if ((Prefix.Length > 0) && IsFocused)
            localX -= TextRenderer.MeasureWidth(Prefix);

        //account for horizontal scroll so clicks map to the actual character under the cursor
        if (IsFocused)
            localX += HScrollOffset;

        if ((Text.Length == 0) || (localX <= 0))
            return 0;

        var displayText = IsMasked ? new string('*', Text.Length) : Text;
        var prevWidth = 0;

        for (var i = 0; i < displayText.Length;)
        {
            //skip color codes as atomic units — they have zero visual width
            if (ColorCodesEnabled && TextRenderer.IsColorCode(displayText, i))
            {
                i += 3;

                continue;
            }

            var nextI = i + 1;
            var charWidth = prevWidth + TextRenderer.MeasureCharWidth(displayText[i]);
            var midpoint = (prevWidth + charWidth) / 2;

            if (localX < midpoint)
                return i;

            prevWidth = charWidth;
            i = nextI;
        }

        return displayText.Length;
    }

    private int HitTestCursorPosition(int targetPixelX, string lineText)
    {
        if ((lineText.Length == 0) || (targetPixelX <= 0))
            return 0;

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

            if (targetPixelX < midpoint)
                return i;

            prevWidth = charWidth;
            i = nextI;
        }

        return lineText.Length;
    }

    private int HitTestMultiLine(int mouseX, int mouseY)
    {
        var localY = mouseY - ScreenY - PaddingTop;
        var clickLine = FirstVisibleLine + localY / TextRenderer.CHAR_HEIGHT;
        clickLine = Math.Clamp(clickLine, 0, LineStarts.Count - 1);
        var lineText = GetLineText(clickLine);
        var localX = mouseX - ScreenX - PaddingLeft;
        var colInLine = HitTestCursorPosition(localX, lineText);

        return LineStarts[clickLine] + colInLine;
    }

    private int HitTestFromScreenPos(int screenX, int screenY)
    {
        if (IsMultiLine)
            return HitTestMultiLine(screenX, screenY);

        return HitTestCursorPosition(screenX - ScreenX - PaddingLeft);
    }

    private static bool IsEffectivelyVisible(UIElement element)
    {
        for (var current = element; current is not null; current = current.Parent)
            if (!current.Visible)
                return false;

        return true;
    }

    /// <summary>
    ///     Moves the cursor to a new position. When extendSelection is false, the selection anchor follows the cursor (no
    ///     selection). When true, the anchor stays put so the selection grows or shrinks.
    /// </summary>
    private void MoveCursor(int newPosition, bool extendSelection)
    {
        //if starting a new selection, pin the anchor at the current position
        if (extendSelection && !HasSelection)
            SelectionAnchor = CursorPosition;

        CursorPosition = Math.Clamp(SnapPastColorCode(newPosition), 0, Text.Length);

        //when not extending, collapse selection by syncing anchor to new cursor position
        if (!extendSelection)
            SelectionAnchor = CursorPosition;

        ResetCursor();
    }

    public event TextBoxFocusHandler? OnFocused;

    private void HandlePaste()
    {
        var clipText = Clipboard.GetText();

        if (string.IsNullOrEmpty(clipText))
            return;

        //strip newlines for single-line textboxes
        if (!IsMultiLine)
            clipText = clipText.Replace("\r", "").Replace("\n", "");

        //snapshot for potential clamptovisiblearea revert
        var savedText = Text;
        var savedCursor = CursorPosition;
        var savedAnchor = SelectionAnchor;

        if (HasSelection)
            DeleteSelection();

        //truncate to maxlength
        var available = MaxLength - Text.Length;

        if (available <= 0)
            return;

        if (clipText.Length > available)
            clipText = clipText[..available];

        var insertPos = CursorPosition;
        Text = Text.Insert(insertPos, clipText);
        CursorPosition = insertPos + clipText.Length;
        SelectionAnchor = CursorPosition;
        ResetCursor();

        //single-line boxes scroll horizontally rather than reject overflow; only multiline clamp-to-area reverts here
        if (ClampToVisibleArea && IsMultiLine && ExceedsVisibleArea())
        {
            Text = savedText;
            CursorPosition = savedCursor;
            SelectionAnchor = savedAnchor;
            CachedLayoutText = string.Empty;
        }
    }

    /// <summary>
    ///     Inserts a newline at the caret (replacing any selection), honoring MaxLength and ClampToVisibleArea.
    ///     Multiline only; reached from both the Enter key and any '\r'/'\n' text-input event.
    /// </summary>
    private void InsertNewline()
    {
        //snapshot state before additive mutation for potential overflow revert
        var savedText = Text;
        var savedCursor = CursorPosition;
        var savedAnchor = SelectionAnchor;

        if (HasSelection)
            DeleteSelection();

        if (Text.Length < MaxLength)
        {
            var nlInsertPos = CursorPosition;
            Text = Text.Insert(nlInsertPos, "\n");
            CursorPosition = nlInsertPos + 1;
            SelectionAnchor = CursorPosition;
            ResetCursor();
        }

        if (ClampToVisibleArea && ExceedsVisibleArea())
        {
            Text = savedText;
            CursorPosition = savedCursor;
            SelectionAnchor = savedAnchor;
            CachedLayoutText = string.Empty;
        }

        EnsureCursorVisible();
    }

    /// <summary>
    ///     Moves the caret to the start of the current visual line (start of text when single-line).
    /// </summary>
    private void MoveToLineStart(bool extendSelection)
    {
        if (IsMultiLine)
        {
            var line = GetLineForPosition(CursorPosition);
            MoveCursor(LineStarts[line], extendSelection);
        } else
            MoveCursor(0, extendSelection);
    }

    /// <summary>
    ///     Moves the caret to the end of the current visual line (end of text when single-line).
    /// </summary>
    private void MoveToLineEnd(bool extendSelection)
    {
        if (IsMultiLine)
        {
            var line = GetLineForPosition(CursorPosition);
            MoveCursor(LineStarts[line] + GetLineText(line).Length, extendSelection);
        } else
            MoveCursor(Text.Length, extendSelection);
    }

    private void ResetCursor()
    {
        CursorVisible = true;
        CursorTimer = 0;
    }

    public override void ResetInteractionState()
    {
        Dragging = false;

        if (IsFocused)
            IsFocused = false;
    }

    public void SelectAll()
    {
        if (!IsSelectable || (Text.Length == 0))
            return;

        SelectionAnchor = 0;
        CursorPosition = Text.Length;
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible || !Enabled)
            return;

        //clamp positions in case text was changed externally
        ClampPositions();

        if (IsMultiLine)
            ComputeLineLayout();

        if (IsFocused)
            UpdateCursorBlink(gameTime);
    }

    //── event handlers ──

    public override void OnMouseDown(MouseDownEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        var clickPos = HitTestFromScreenPos(e.ScreenX, e.ScreenY);

        if (e.Shift && IsFocused && IsSelectable)

            //shift+click extends selection to the click position
            CursorPosition = clickPos;
        else
        {
            CursorPosition = clickPos;
            SelectionAnchor = clickPos;
        }

        Dragging = true;
        ResetCursor();

        if (!IsFocused)
        {
            IsFocused = true;
            OnFocused?.Invoke(this);
        }

        e.Handled = true;
    }

    public override void OnClick(ClickEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        var clickPos = HitTestFromScreenPos(e.ScreenX, e.ScreenY);
        var now = Environment.TickCount64;

        //track click count for triple-click detection
        if (((now - LastClickTime) < TRIPLE_CLICK_MS) && (clickPos == LastClickPosition))
            ClickCount++;
        else
            ClickCount = 1;

        LastClickTime = now;
        LastClickPosition = clickPos;

        //triple-click: select line (multiline) or select all (single-line)
        if (ClickCount >= 3)
        {
            if (IsMultiLine)
            {
                var line = GetLineForPosition(clickPos);
                SelectionAnchor = LineStarts[line];
                var lineText = GetLineText(line);
                CursorPosition = LineStarts[line] + lineText.Length;
            } else
                SelectAll();

            ClickCount = 0;
            e.Handled = true;
        }
    }

    public override void OnDoubleClick(DoubleClickEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        var clickPos = HitTestFromScreenPos(e.ScreenX, e.ScreenY);
        SelectionAnchor = FindWordBoundaryLeft(clickPos);
        CursorPosition = FindWordBoundaryRight(clickPos);

        //bump click count so the next click within the window triggers triple-click
        ClickCount = 2;
        e.Handled = true;
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        if (!Dragging || !IsSelectable)
            return;

        var dragPos = HitTestFromScreenPos(e.ScreenX, e.ScreenY);

        if (dragPos != CursorPosition)
        {
            CursorPosition = dragPos;
            ResetCursor();
        }
    }

    public override void OnMouseUp(MouseUpEvent e)
    {
        if (e.Button == MouseButton.Left)
            Dragging = false;
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        if (!IsMultiLine || !IsFocused)
            return;

        var maxScroll = Math.Max(0, (LineStarts.Count - VisibleLineCount) * TextRenderer.CHAR_HEIGHT);
        ScrollOffset = Math.Clamp(ScrollOffset - e.Delta * TextRenderer.CHAR_HEIGHT, 0, maxScroll);
        e.Handled = true;
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (!IsFocused || !Enabled)
            return;

        var shift = e.Shift;
        var ctrl = e.Ctrl;

        switch (e.Key)
        {
            //── navigation ──
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
                else if (CursorPosition < Text.Length)
                    MoveCursor(ctrl ? FindWordBoundaryRight(CursorPosition) : StepRight(CursorPosition), shift);

                e.Handled = true;

                break;

            case Keys.Up:
                if (IsMultiLine)
                {
                    var cursorLine = GetLineForPosition(CursorPosition);

                    if (cursorLine > 0)
                    {
                        var colOffset = CursorPosition - LineStarts[cursorLine];
                        var currentLineText = GetLineText(cursorLine);
                        var colPixelX = TextRenderer.MeasureWidth(currentLineText[..Math.Min(colOffset, currentLineText.Length)]);
                        var targetLine = cursorLine - 1;
                        var targetText = GetLineText(targetLine);
                        var targetCol = HitTestCursorPosition(colPixelX, targetText);
                        MoveCursor(LineStarts[targetLine] + targetCol, shift);
                    } else
                        MoveCursor(0, shift);
                } else
                    MoveCursor(0, shift);

                e.Handled = true;

                break;

            case Keys.Down:
                if (IsMultiLine)
                {
                    var cursorLine = GetLineForPosition(CursorPosition);

                    if ((cursorLine + 1) < LineStarts.Count)
                    {
                        var colOffset = CursorPosition - LineStarts[cursorLine];
                        var currentLineText = GetLineText(cursorLine);
                        var colPixelX = TextRenderer.MeasureWidth(currentLineText[..Math.Min(colOffset, currentLineText.Length)]);
                        var targetLine = cursorLine + 1;
                        var targetText = GetLineText(targetLine);
                        var targetCol = HitTestCursorPosition(colPixelX, targetText);
                        MoveCursor(LineStarts[targetLine] + targetCol, shift);
                    } else
                        MoveCursor(Text.Length, shift);
                } else
                    MoveCursor(Text.Length, shift);

                e.Handled = true;

                break;

            case Keys.Home:
                if (ctrl)
                    MoveCursor(0, shift);
                else
                    MoveToLineStart(shift);

                e.Handled = true;

                break;

            case Keys.End:
                if (ctrl)
                    MoveCursor(Text.Length, shift);
                else
                    MoveToLineEnd(shift);

                e.Handled = true;

                break;

            //── selection / clipboard ──
            case Keys.A when ctrl && shift && IsSelectable:
                SelectAll();
                e.Handled = true;

                break;

            //readline-style caret movement: ctrl+a line start, ctrl+e line end
            case Keys.A when ctrl:
                MoveToLineStart(false);
                e.Handled = true;

                break;

            case Keys.E when ctrl:
                MoveToLineEnd(false);
                e.Handled = true;

                break;

            case Keys.C when ctrl && HasSelection:
            {
                var clipboardText = IsMasked ? new string('*', SelectionLength) : StripColorCodes(SelectedText);
                Clipboard.SetText(clipboardText);
                e.Handled = true;

                break;
            }

            case Keys.X when ctrl && HasSelection && !IsReadOnly:
            {
                var clipboardText = IsMasked ? new string('*', SelectionLength) : StripColorCodes(SelectedText);
                Clipboard.SetText(clipboardText);
                DeleteSelection();
                ResetCursor();
                e.Handled = true;

                break;
            }

            case Keys.V when ctrl && !IsReadOnly:
                HandlePaste();
                e.Handled = true;

                break;

            //── editing ──
            case Keys.Delete when ctrl && !IsReadOnly:
                if (HasSelection)
                    DeleteSelection();
                else if (CursorPosition < Text.Length)
                {
                    var wordEnd = FindWordBoundaryRight(CursorPosition);
                    Text = Text.Remove(CursorPosition, wordEnd - CursorPosition);
                }

                ResetCursor();
                e.Handled = true;

                break;

            case Keys.Delete when !IsReadOnly:
                if (HasSelection)
                    DeleteSelection();
                else if (CursorPosition < Text.Length)
                {
                    //delete the entire color code atomically if the cursor is at the start of one
                    var stepPos = StepRight(CursorPosition);
                    Text = Text.Remove(CursorPosition, stepPos - CursorPosition);
                }

                ResetCursor();
                e.Handled = true;

                break;

            case Keys.Back when ctrl && !IsReadOnly:
                if (HasSelection)
                    DeleteSelection();
                else if (CursorPosition > 0)
                {
                    var wordStart = FindWordBoundaryLeft(CursorPosition);
                    Text = Text.Remove(wordStart, CursorPosition - wordStart);
                    CursorPosition = wordStart;
                    SelectionAnchor = wordStart;
                }

                ResetCursor();
                e.Handled = true;

                break;

            case Keys.Back when !IsReadOnly:
                HandleBackspace();
                e.Handled = true;

                break;

            //enter never arrives as a text-input event (SDL only raises SDL_TEXTINPUT for
            //printable characters), so the newline has to be inserted from the key event.
            case Keys.Enter when IsMultiLine && !IsReadOnly:
                InsertNewline();
                e.Handled = true;

                break;

            default:
                //consume all other key presses while focused so they don't bubble
                //to hotkey handlers. actual character insertion happens via ontextinput.
                //let escape and tab bubble for unfocus / focus cycling.
                if (e.Key is not Keys.Escape && !(e.Key is Keys.Enter && !IsMultiLine) && !(e.Key is Keys.Tab && IsTabStop))
                    e.Handled = true;

                break;
        }

        if (IsMultiLine)
            EnsureCursorVisible();
    }

    public override void OnTextInput(TextInputEvent e)
    {
        if (!IsFocused || IsReadOnly || !Enabled)
            return;

        var c = e.Character;

        //backspace handled in onkeydown
        if (c == '\b')
            return;

        //tab cycles focus when istabstop is set — parent handles the actual transfer
        if ((c == '\t') && IsTabStop)
            return;

        if ((c == '\r') || (c == '\n'))
        {
            if (!IsMultiLine)
                return;

            InsertNewline();
            e.Handled = true;

            return;
        }

        if (char.IsControl(c))
            return;

        //snapshot state before additive mutation for potential overflow revert
        var priorText = Text;
        var priorCursor = CursorPosition;
        var priorAnchor = SelectionAnchor;

        //replace selection with typed character
        if (HasSelection)
            DeleteSelection();

        if (Text.Length >= MaxLength)
            return;

        //capture position before mutation to avoid setter interactions
        var insertPos = CursorPosition;
        Text = Text.Insert(insertPos, c.ToString());
        CursorPosition = insertPos + 1;
        SelectionAnchor = CursorPosition;
        ResetCursor();

        //single-line boxes scroll horizontally rather than reject overflow; only multiline clamp-to-area reverts here
        if (ClampToVisibleArea && IsMultiLine && ExceedsVisibleArea())
        {
            Text = priorText;
            CursorPosition = priorCursor;
            SelectionAnchor = priorAnchor;
            CachedLayoutText = string.Empty;
        }

        e.Handled = true;

        if (IsMultiLine)
            EnsureCursorVisible();
    }

    private void UpdateCursorBlink(GameTime gameTime)
    {
        CursorTimer += gameTime.ElapsedGameTime.TotalMilliseconds;

        if (CursorTimer >= CURSOR_BLINK_MS)
        {
            CursorVisible = !CursorVisible;
            CursorTimer -= CURSOR_BLINK_MS;
        }
    }
}
