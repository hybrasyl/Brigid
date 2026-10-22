#region
using Brigid.Controls.Components;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     One line of a <see cref="TooltipFrame" />: text in the colour of a theme role, or in a literal colour for a colour
///     that carries meaning (for example the durability of an item).
/// </summary>
public readonly record struct TooltipLine
{
    private TooltipLine(string text, string? role, Color color)
    {
        Text = text;
        Role = role;
        Color = color;
    }

    public string Text { get; }

    /// <summary>The theme colour role of the line, or null for a literal colour.</summary>
    public string? Role { get; }

    /// <summary>The colour of a literal line, and the fallback of a themed line: the flat value of its role.</summary>
    public Color Color { get; }

    /// <summary>A line in the theme colour of <paramref name="role" />, else the flat value of the role.</summary>
    public static TooltipLine Themed(string text, string role) => new(text, role, DialogPalette.ForRole(role));

    /// <summary>A line in a colour that carries meaning, with or without a theme.</summary>
    public static TooltipLine Literal(string text, Color color) => new(text, null, color);
}

/// <summary>
///     A primitive tooltip (BRIG-125): lines of text in a small frame at the cursor. It draws the theme's
///     <see cref="ThemeRoles.FramePopup" /> part, else the flat frame. Each line wraps at the largest text width, and the
///     frame fits the widest wrapped line. <see cref="PlaceAtCursor" /> puts it down and to the right of the cursor, or to
///     the left when there is no space.
///     <para>
///         The item tooltip is a child of the root. A panel that draws its tooltip over its own content, for example the
///         bank and shop window, keeps a tooltip with no parent, so that the panel does not clip it, and calls
///         <see cref="UIElement.Draw" /> at the end of its own draw. Such a tooltip is not in the control tree: a tree walk
///         (the debug overlay, a name lookup) does not find it, and it gets no update. The tooltip takes no mouse input.
///     </para>
/// </summary>
public class TooltipFrame : FramePanel
{
    /// <summary>The space between the edge and the text in the flat frame.</summary>
    internal const int PADDING = 6;

    private readonly int MaxTextWidth;
    private readonly List<(string Text, TooltipLine Line)> Wrapped = [];

    private readonly FontGenerationStamp Fitted = new();
    private IReadOnlyList<TooltipLine> Lines = [];

    /// <param name="maxTextWidth">The largest width of a line of text; a longer line wraps.</param>
    public TooltipFrame(int maxTextWidth)
    {
        Visible = false;
        UseTheme = true;
        IsHitTestVisible = false;
        MaxTextWidth = maxTextWidth;
    }

    protected override string FrameRole => ThemeRoles.FramePopup;

    /// <summary>The wrapped lines, in the order they draw. Internal for the tests.</summary>
    internal IEnumerable<string> WrappedText => Wrapped.Select(line => line.Text);

    /// <summary>Sets the lines, and fits the frame to them.</summary>
    public void SetLines(IReadOnlyList<TooltipLine> lines)
    {
        Lines = lines;
        Fit();
    }

    /// <summary>
    ///     Puts the tooltip down and to the right of the cursor, or to its left when the right side has no space, in the
    ///     640×480 viewport. The coordinates are in the parent, so give screen coordinates to a tooltip with no parent or
    ///     with the root as its parent.
    /// </summary>
    public void PlaceAtCursor(int mouseX, int mouseY)
    {
        EnsureFitted();
        (X, Y) = TooltipPlacement.CursorAnchored(mouseX, mouseY, Width, Height);
    }

    //a new face or window size wraps the lines again; a tooltip with no parent gets no update, so the callers check
    private void EnsureFitted()
    {
        if (Fitted.Changed)
            Fit();
    }

    //wraps each line at the largest text width, at the current face, and sizes the frame to the result
    private void Fit()
    {
        Wrapped.Clear();
        var widest = 0;

        foreach (var line in Lines)
            foreach (var text in TextRenderer.WrapText(line.Text, MaxTextWidth))
            {
                Wrapped.Add((text, line));
                widest = Math.Max(widest, TextRenderer.MeasureWidth(text));
            }

        Fitted.Mark();

        var inset = InsetFor(PADDING);

        //1 px more than the text, so that a rounding difference does not clip the last glyph
        Width = 2 * inset + Math.Min(MaxTextWidth, widest) + 1;
        Height = 2 * inset + Wrapped.Count * TextRenderer.CHAR_HEIGHT;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        EnsureFitted();
        base.Draw(spriteBatch);

        //the chrome does not draw when the tooltip is fully clipped, so the text does not draw either
        if ((ClipRect.Width <= 0) || (ClipRect.Height <= 0))
            return;

        var inset = InsetFor(PADDING);
        var y = ScreenY + inset;

        foreach (var (text, line) in Wrapped)
        {
            TextRenderer.DrawText(spriteBatch, new Vector2(ScreenX + inset, y), text, RoleColor(line.Role, line.Color));
            y += TextRenderer.CHAR_HEIGHT;
        }
    }
}
