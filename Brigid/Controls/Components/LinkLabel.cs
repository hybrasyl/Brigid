#region
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Components;

/// <summary>
///     A UILabel that raises <see cref="Clicked" /> on left-click, for link-style text (e.g. the update notice on
///     the start screen). Single-line only: it draws its text itself through the styled FontEngine path so it can
///     render bold/italic, which the shared UILabel pipeline doesn't plumb.
/// </summary>
public sealed class LinkLabel : UILabel
{
    private Color? RestingColor;

    /// <summary>Text color while hovered, so the label reads as clickable. Defaults to white.</summary>
    public Color HoverColor { get; set; } = Color.White;

    /// <summary>Font style for the link text (e.g. bold for the update notice).</summary>
    public FontStyle TextStyle { get; set; } = FontStyle.Regular;

    public event Action? Clicked;

    //this overrides Draw entirely rather than going through TextElement (it draws in an explicit FontStyle, which
    //TextElement does not carry), so the inherited FontSize has to be honoured here by hand or it is silently inert
    private int GlyphSize => FontSize ?? FontEngine.Instance.UiSize;

    /// <summary>Pixel width of the current text under <see cref="TextStyle" /> — use for hit-box sizing.</summary>
    public int MeasureTextWidth() => FontEngine.Instance.MeasureWidth(Text, GlyphSize, TextStyle);

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || string.IsNullOrEmpty(Text))
            return;

        UpdateClipRect();

        var engine = FontEngine.Instance;
        var size = GlyphSize;
        var textWidth = MeasureTextWidth();

        var x = HorizontalAlignment == HorizontalAlignment.Right
            ? ScreenX + Width - textWidth
            : ScreenX;

        var y = ScreenY + (Height - engine.GetLineHeight(size, TextStyle)) / 2;

        engine.DrawLine(spriteBatch, Text, new Vector2(x, y), ForegroundColor, ClipRect, size, TextStyle);
    }

    public override void OnClick(ClickEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        Clicked?.Invoke();
        e.Handled = true;
    }

    public override void OnMouseEnter()
    {
        RestingColor = ForegroundColor;
        ForegroundColor = HoverColor;
    }

    public override void OnMouseLeave()
    {
        if (RestingColor is { } resting)
            ForegroundColor = resting;

        RestingColor = null;
    }
}
