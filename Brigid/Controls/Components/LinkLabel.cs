#region
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.Components;

/// <summary>
///     A UILabel that raises <see cref="Clicked" /> on left-click, for link-style text (e.g. the update notice on
///     the start screen).
/// </summary>
public sealed class LinkLabel : UILabel
{
    private Color? RestingColor;

    /// <summary>Text color while hovered, so the label reads as clickable. Defaults to white.</summary>
    public Color HoverColor { get; set; } = Color.White;

    public event Action? Clicked;

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
