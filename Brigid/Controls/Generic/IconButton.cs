#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A primitive button that shows an icon role (<see cref="RoleIcon" />) on the button parts of the theme, else on the
///     flat button look of <see cref="TextButton" />. A selected button (its window is open) shows the pressed part. It
///     raises a left click, a right click and the mouse coming over it and going away, so its host can show a tooltip.
/// </summary>
public sealed class IconButton : UIPanel
{
    //the space between the edge of the button and its icon
    private const int ICON_INSET = 2;

    private readonly RoleIcon Icon;

    private bool Hovered;
    private bool Pressed;

    public IconButton(string role, Rectangle bounds)
    {
        SetBounds(bounds);
        BorderColor = DialogPalette.FrameBorder;

        Icon = new RoleIcon { Name = "icon", Role = role };
        Icon.SetBounds(new Rectangle(ICON_INSET, ICON_INSET, bounds.Width - 2 * ICON_INSET, bounds.Height - 2 * ICON_INSET));
        AddChild(Icon);
    }

    /// <summary>The icon role that the button shows.</summary>
    public string? Role => Icon.Role;

    /// <summary>True while the window of the button is open: the button shows the pressed look.</summary>
    public bool IsSelected { get; set; }

    public event Action? Clicked;
    public event Action? RightClicked;
    public event Action<IconButton>? Entered;
    public event Action? Left;

    private bool ShowsPressed => Enabled && (IsSelected || Pressed);

    protected internal override Color? DrawnBackgroundColor
        => ShowsPressed ? DialogPalette.ButtonPressFill
            : Enabled && Hovered ? DialogPalette.ButtonHoverFill : DialogPalette.ButtonIdleFill;

    //the flat border is part of the look: the outline, or the divider colour while disabled
    protected internal override Color? DrawnBorderColor => Enabled ? ThemeColor(ThemeRoles.Border) : ThemeColor(ThemeRoles.Divider);

    //the state order of TextButton: disabled, then pressed or selected, then hover
    private string StateRole
        => !Enabled ? ThemeRoles.ButtonDisabled
            : ShowsPressed ? ThemeRoles.ButtonPressed
            : Hovered ? ThemeRoles.ButtonHover : ThemeRoles.ButtonNormal;

    //a state that the theme leaves out uses button.normal; with no theme the button keeps its flat look
    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (TryDrawThemePart(spriteBatch, StateRole, bounds, ThemeRoles.ButtonNormal))
            return;

        base.DrawChrome(spriteBatch, bounds);
    }

    public override void OnMouseEnter()
    {
        Hovered = true;
        Entered?.Invoke(this);
    }

    public override void OnMouseLeave()
    {
        Hovered = false;
        Pressed = false;
        Left?.Invoke();
    }

    public override void OnMouseDown(MouseDownEvent e)
    {
        if (e.Button == MouseButton.Left)
            Pressed = true;

        e.Handled = true;
    }

    public override void OnMouseUp(MouseUpEvent e)
    {
        if (e.Button == MouseButton.Left)
            Pressed = false;
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
