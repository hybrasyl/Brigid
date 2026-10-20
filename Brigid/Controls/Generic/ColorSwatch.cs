#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A primitive colour swatch: a box filled with one colour, with a 1 px border, and a 2 px bright border while
///     <see cref="IsSelected" />. A click raises <see cref="Clicked" />; the host moves the selection. The borders use the
///     theme's <see cref="ThemeRoles.Divider" /> and <see cref="ThemeRoles.TextSelected" /> colours when it has them.
/// </summary>
public sealed class ColorSwatch : UIPanel
{
    private readonly Color Fill;

    public bool IsSelected { get; set; }

    public event ClickedHandler? Clicked;

    public ColorSwatch(Color fill) => Fill = fill;

    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        DrawRectClipped(spriteBatch, bounds, Fill);

        if (!IsSelected)
        {
            DrawBorderClipped(spriteBatch, bounds, ThemeColor(ThemeRoles.Divider));

            return;
        }

        var selected = ThemeColor(ThemeRoles.TextSelected);
        DrawBorderClipped(spriteBatch, bounds, selected);
        DrawBorderClipped(spriteBatch, new Rectangle(bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2), selected);
    }

    public override void OnClick(ClickEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        Clicked?.Invoke();
        e.Handled = true;
    }
}
