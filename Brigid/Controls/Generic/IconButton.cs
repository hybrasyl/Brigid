#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A primitive button that shows an icon role (<see cref="RoleIcon" />) on the button parts of the theme, else on the
///     flat button look of <see cref="TextButton" />. A selected button (its window is open) shows the pressed part. It
///     raises a left click, a right click and the mouse coming over it and going away, so its host can show a tooltip. It
///     takes every press and every click, so none goes on to the world under it.
/// </summary>
public sealed class IconButton : ButtonBase
{
    //the space between the edge of the button and its icon
    private const int ICON_INSET = 2;

    private readonly RoleIcon Icon;

    public IconButton(string role, Rectangle bounds)
    {
        BorderColor = DialogPalette.FrameBorder;

        Icon = new RoleIcon { Name = "icon", Role = role };
        AddChild(Icon);
        SetBounds(bounds);
    }

    //the icon follows the bounds, so a button that a layout places after its construction shows its icon
    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        Icon.SetBounds(new Rectangle(ICON_INSET, ICON_INSET, Math.Max(0, bounds.Width - 2 * ICON_INSET), Math.Max(0, bounds.Height - 2 * ICON_INSET)));
    }

    /// <summary>The icon role that the button shows.</summary>
    public string? Role
    {
        get => Icon.Role;
        set => Icon.Role = value;
    }

    /// <summary>True while the window of the button is open: the button shows the pressed look.</summary>
    public bool IsSelected { get; set; }

    public event Action? Clicked;
    public event Action? RightClicked;
    public event Action<IconButton>? Entered;
    public event Action? Left;

    protected override bool ShowsPressed => Enabled && (IsSelected || Pressed);

    protected override bool TakesEveryPress => true;

    protected internal override Color? DrawnBackgroundColor
        => ShowsPressed ? DialogPalette.ButtonPressFill
            : Enabled && Hovered ? DialogPalette.ButtonHoverFill : DialogPalette.ButtonIdleFill;

    //the flat border is part of the look: the outline, or the divider colour while disabled
    protected internal override Color? DrawnBorderColor => Enabled ? ThemeColor(ThemeRoles.Border) : ThemeColor(ThemeRoles.Divider);

    public override void OnMouseEnter()
    {
        base.OnMouseEnter();
        Entered?.Invoke(this);
    }

    public override void OnMouseLeave()
    {
        base.OnMouseLeave();
        Left?.Invoke();
    }

    public override void OnClick(ClickEvent e)
    {
        e.Handled = true;

        if (!Enabled)
            return;

        if (e.Button == MouseButton.Left)
            Clicked?.Invoke();
        else if (e.Button == MouseButton.Right)
            RightClicked?.Invoke();
    }
}
