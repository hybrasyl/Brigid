#region
using Brigid.Controls.Components;
using Brigid.Rendering;
using Brigid.Rendering.Markdown;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A primitive that shows markdown: it lays the text out with <see cref="MarkdownLayoutEngine" /> at its own width and
///     replays the layout, clipped to its bounds and moved up by <see cref="ScrollY" />. It owns no frame and no scroll bar,
///     so a host puts it in whatever frame it has. <see cref="MarkdownView" />, the menu alerts and the Story view use it.
///     <para>
///         The layout is made again when the width or <see cref="FontEngine.Generation" /> changes, from
///         <see cref="Update" />, and <see cref="LaidOut" /> then tells the host, which can resize its frame or its
///         scroll range.
///     </para>
/// </summary>
public sealed class MarkdownBlock : UIElement
{
    //cull margin taller than any laid-out line so partially-visible spans still draw
    private const int CULL_MARGIN = 40;

    private static readonly Color CodeColor = LegendColors.PaleSilver;
    private static readonly Color ListMarkerColor = LegendColors.Silver;
    private static readonly Color RuleColor = LegendColors.DimGray;
    private static readonly Color CodeBackgroundColor = new(0, 0, 0, 150);

    private string Source = string.Empty;
    private int LayoutGeneration = -1;
    private int LayoutWidth = -1;

    /// <summary>The colour of body text when no theme colour applies.</summary>
    public Color BodyColor { get; set; } = LegendColors.White;

    /// <summary>The colour of headings when no theme colour applies.</summary>
    public Color HeadingColor { get; set; } = LegendColors.Gold;

    /// <summary>
    ///     When true, a leading level-1 heading is not drawn and goes to <see cref="MarkdownLayout.Title" />, for a host
    ///     that shows the title in its own title bar.
    /// </summary>
    public bool ExtractTitle { get; init; }

    /// <summary>
    ///     The pixel size of body text; headings scale with it. The default suits a reading view. A small panel sets a
    ///     smaller size.
    /// </summary>
    public int TextSize { get; init; } = MarkdownLayoutEngine.BODY_SIZE;

    /// <summary>The pixel distance the content is moved up. The host clamps it to the content height.</summary>
    public int ScrollY { get; set; }

    /// <summary>The current layout, or null before the first one.</summary>
    public MarkdownLayout? Layout { get; private set; }

    /// <summary>The height of the laid-out content in pixels.</summary>
    public int ContentHeight => Layout?.ContentHeight ?? 0;

    /// <summary>Raised after each new layout.</summary>
    public event Action? LaidOut;

    /// <summary>Sets the markdown and lays it out at the current width.</summary>
    public void SetMarkdown(string? markdown)
    {
        Source = markdown ?? string.Empty;
        Relayout();
    }

    /// <summary>Lays the markdown out again when the width or the font generation changed since the last layout.</summary>
    public void EnsureLayout()
    {
        if ((LayoutWidth != Width) || (LayoutGeneration != FontEngine.Instance.Generation))
            Relayout();
    }

    public override void Update(GameTime gameTime)
    {
        if (Visible)
            EnsureLayout();
    }

    private void Relayout()
    {
        LayoutWidth = Width;
        LayoutGeneration = FontEngine.Instance.Generation;
        Layout = MarkdownLayoutEngine.Layout(Source, Width, FontEngine.Instance, ExtractTitle, TextSize);
        LaidOut?.Invoke();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        UpdateClipRect();

        if ((ClipRect.Width <= 0) || (ClipRect.Height <= 0))
            return;

        if (Layout is not { } layout)
            return;

        var originX = ScreenX;
        var originY = ScreenY - ScrollY;

        //all three lists are emitted in ascending Y by the layout engine, so once an element starts below
        //the clip rect every later one does too — break instead of scanning the whole document each frame
        foreach (var rect in layout.CodeBackgrounds)
        {
            if (originY + rect.Y > ClipRect.Bottom)
                break;

            if (originY + rect.Y + rect.Height < ClipRect.Y)
                continue;

            DrawRectClipped(spriteBatch, new Rectangle(originX + rect.X, originY + rect.Y, rect.Width, rect.Height), CodeBackgroundColor);
        }

        foreach (var rule in layout.Rules)
        {
            if (originY + rule.Y > ClipRect.Bottom)
                break;

            if (originY + rule.Y + rule.Height < ClipRect.Y)
                continue;

            DrawRectClipped(
                spriteBatch,
                new Rectangle(originX + rule.X, originY + rule.Y, rule.Width, rule.Height),
                ThemeColor(ThemeRoles.Divider, RuleColor));
        }

        var body = ThemeColor(ThemeRoles.Text, BodyColor);
        var heading = ThemeColor(ThemeRoles.TextTitle, HeadingColor);

        foreach (var span in layout.Spans)
        {
            var y = originY + span.Y;

            if (y > ClipRect.Bottom)
                break;

            if (y + CULL_MARGIN < ClipRect.Y)
                continue;

            var color = span.Kind switch
            {
                MarkdownSpanKind.Heading => heading,
                MarkdownSpanKind.Code => CodeColor,
                MarkdownSpanKind.ListMarker => ListMarkerColor,
                _ => body
            };

            FontEngine.Instance.DrawLine(spriteBatch, span.Text, new Vector2(originX + span.X, y), color, ClipRect, span.Size, span.Style);
        }
    }
}
