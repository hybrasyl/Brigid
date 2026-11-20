#region
using Brigid.Controls.Components;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     The base of the primitive buttons (<see cref="TextButton" />, <see cref="IconButton" />): the hover and the pressed
///     state from the mouse, and the button part of the theme for the state. The order of the states is disabled, then
///     pressed, then hover; a state that the theme leaves out uses <see cref="ThemeRoles.ButtonNormal" />, and with no theme
///     the button keeps its flat look. A subclass handles the click.
/// </summary>
public abstract class ButtonBase : UIPanel
{
    /// <summary>True while the mouse is over the button.</summary>
    protected bool Hovered { get; private set; }

    /// <summary>True while the left button is down on the button.</summary>
    protected bool Pressed { get; private set; }

    /// <summary>True when the button shows the pressed look: by default while it is pressed.</summary>
    protected virtual bool ShowsPressed => Pressed;

    /// <summary>True for a button that takes every mouse press, so that no press goes on to the world under it.</summary>
    protected virtual bool TakesEveryPress => false;

    private string StateRole
        => !Enabled ? ThemeRoles.ButtonDisabled
            : ShowsPressed ? ThemeRoles.ButtonPressed
            : Hovered ? ThemeRoles.ButtonHover : ThemeRoles.ButtonNormal;

    /// <summary>The hover or the pressed state changed.</summary>
    protected virtual void OnStateChanged() { }

    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (TryDrawThemePart(spriteBatch, StateRole, bounds, ThemeRoles.ButtonNormal))
            return;

        base.DrawChrome(spriteBatch, bounds);
    }

    /// <summary>
    ///     Shows the hover or the pressed look with no mouse, for the primitive gallery. The button then takes no mouse
    ///     input, so that the mouse does not set the state again.
    /// </summary>
    internal void ShowState(bool hovered, bool pressed)
    {
        IsHitTestVisible = false;
        Hovered = hovered;
        Pressed = pressed;
        OnStateChanged();
    }

    public override void OnMouseEnter()
    {
        Hovered = true;
        OnStateChanged();
    }

    public override void OnMouseLeave()
    {
        Hovered = false;
        Pressed = false;
        OnStateChanged();
    }

    public override void OnMouseDown(MouseDownEvent e)
    {
        if (e.Button != MouseButton.Left)
        {
            if (TakesEveryPress)
                e.Handled = true;

            return;
        }

        Pressed = true;
        OnStateChanged();
        e.Handled = true;
    }

    public override void OnMouseUp(MouseUpEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        Pressed = false;
        OnStateChanged();
    }
}
