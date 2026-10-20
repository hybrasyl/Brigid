#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A primitive, asset-free text button: a filled/bordered box with a centered label and hover / pressed / disabled
///     states. Shared by the from-scratch panels (<see cref="World.Popups.Dialog.BankShopPanel" />,
///     <see cref="World.Popups.WorldList.WorldListControl" />) so the pager / OK / Close affordances read identically
///     across the modernized dialog surfaces.
/// </summary>
public sealed class TextButton : UIPanel
{
    private static readonly Color IdleFill = DialogPalette.ButtonIdleFill;
    private static readonly Color HoverFill = DialogPalette.ButtonHoverFill;
    private static readonly Color PressFill = DialogPalette.ButtonPressFill;
    private static readonly Color EdgeColor = DialogPalette.FrameBorder;
    private static readonly Color DisabledEdge = DialogPalette.Divider;
    private static readonly Color DisabledText = DialogPalette.DisabledText;

    private readonly UILabel Label;

    //when non-Regular (e.g. FontStyle.Icon for the ↺ reset glyph), the label is drawn by this control through the
    //styled TextRenderer path instead of the plain UILabel, so the glyph resolves to the symbol face. See Draw.
    private readonly FontStyle LabelStyle;

    private bool Hovered;
    private bool Pressed;

    //optional accent for the label (e.g. a conflict warning); null = the default white/disabled behavior.
    private Color? TextColorOverride;

    public event ClickedHandler? Clicked;

    public TextButton(string text, int width, int height, FontStyle labelStyle = FontStyle.Regular)
    {
        Width = width;
        Height = height;
        BorderColor = EdgeColor;
        LabelStyle = labelStyle;

        Label = new UILabel
        {
            X = 0,
            Y = 0,
            Width = width,
            Height = height,
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ForegroundColor = LegendColors.White,
            ShrinkToFit = false,
            IsHitTestVisible = false,
            //a styled label is drawn by this control instead (the plain label can't select the symbol face)
            Visible = labelStyle == FontStyle.Regular
        };

        AddChild(Label);
        RefreshVisual();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        //the theme can come from a parent, which can change after construction, so the colours are resolved per draw
        RefreshVisual();

        base.Draw(spriteBatch);

        if (LabelStyle == FontStyle.Regular)
            return;

        var text = Label.Text;

        if (string.IsNullOrEmpty(text))
            return;

        //centre the styled glyph in the button using the styled width; band-centred vertically like a normal label.
        //clip to this element's ClipRect (refreshed by base.Draw) so a partially-scrolled row trims the glyph.
        //the label is hidden, so it does not apply its colour role itself
        Label.ApplyForegroundRole();
        var color = Label.DrawnForegroundColor;
        var textWidth = TextRenderer.MeasureWidth(text, LabelStyle);
        var x = ScreenX + (Width - textWidth) / 2;
        var y = ScreenY + (Height - TextRenderer.CHAR_HEIGHT) / 2;

        FontEngine.Instance.DrawLine(spriteBatch, text, new Vector2(x, y), color, ClipRect, style: LabelStyle);
    }

    //the flat border is part of the look: the outline, or the divider colour while disabled
    protected internal override Color? DrawnBorderColor
        => BorderColor is null ? null : Enabled ? ThemeColor(ThemeRoles.Border, EdgeColor) : ThemeColor(ThemeRoles.Divider, DisabledEdge);

    //a state that the theme leaves out uses button.normal; with no theme the button keeps its flat look
    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (TryDrawThemePart(spriteBatch, StateRole, bounds, ThemeRoles.ButtonNormal))
            return;

        base.DrawChrome(spriteBatch, bounds);
    }

    private string StateRole
        => !Enabled ? ThemeRoles.ButtonDisabled
            : Pressed ? ThemeRoles.ButtonPressed
            : Hovered ? ThemeRoles.ButtonHover : ThemeRoles.ButtonNormal;

    //Enabled has no change hook on UIElement, so callers flip state through here to refresh the visual
    public void SetEnabled(bool value)
    {
        Enabled = value;
        RefreshVisual();
    }

    public void SetText(string text) => Label.Text = text;

    /// <summary>
    ///     Shows the hover or the pressed look with no mouse, for the primitive gallery. The button then takes no mouse
    ///     input, so that the mouse does not set the state again.
    /// </summary>
    internal void ShowState(bool hovered, bool pressed)
    {
        IsHitTestVisible = false;
        Hovered = hovered;
        Pressed = pressed;
        RefreshVisual();
    }

    /// <summary>Tints the label (null restores the default white / disabled-grey behavior).</summary>
    public void SetTextColor(Color? color)
    {
        TextColorOverride = color;
        RefreshVisual();
    }

    private void RefreshVisual()
    {
        BackgroundColor = Enabled ? Pressed ? PressFill : Hovered ? HoverFill : IdleFill : IdleFill;
        if (!Enabled)
            Label.SetForegroundRole(ThemeRoles.TextDisabled, DisabledText);
        else if (TextColorOverride is { } overrideColor)
            Label.ForegroundColor = overrideColor;
        else
            Label.SetForegroundRole(ThemeRoles.Text, LegendColors.White);

        BorderColor = Enabled ? EdgeColor : DisabledEdge;
    }

    public override void OnMouseEnter()
    {
        Hovered = true;
        RefreshVisual();
    }

    public override void OnMouseLeave()
    {
        Hovered = false;
        Pressed = false;
        RefreshVisual();
    }

    public override void OnMouseDown(MouseDownEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        Pressed = true;
        RefreshVisual();
        e.Handled = true;
    }

    public override void OnMouseUp(MouseUpEvent e)
    {
        if (e.Button == MouseButton.Left)
        {
            Pressed = false;
            RefreshVisual();
        }
    }

    public override void OnClick(ClickEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        PerformClick();
        e.Handled = true;
    }

    /// <summary>Raises <see cref="Clicked" /> as a left click does. A disabled button does nothing.</summary>
    public void PerformClick()
    {
        if (Enabled)
            Clicked?.Invoke();
    }
}
