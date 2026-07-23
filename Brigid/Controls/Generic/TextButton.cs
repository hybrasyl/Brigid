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
        base.Draw(spriteBatch);

        if (LabelStyle == FontStyle.Regular)
            return;

        var text = Label.Text;

        if (string.IsNullOrEmpty(text))
            return;

        //centre the styled glyph in the button using the styled width; band-centred vertically like a normal label.
        //clip to this element's ClipRect (refreshed by base.Draw) so a partially-scrolled row trims the glyph.
        var color = Enabled ? TextColorOverride ?? LegendColors.White : DisabledText;
        var textWidth = TextRenderer.MeasureWidth(text, LabelStyle);
        var x = ScreenX + (Width - textWidth) / 2;
        var y = ScreenY + (Height - TextRenderer.CHAR_HEIGHT) / 2;

        FontEngine.Instance.DrawLine(spriteBatch, text, new Vector2(x, y), color, ClipRect, style: LabelStyle);
    }

    //Enabled has no change hook on UIElement, so callers flip state through here to refresh the visual
    public void SetEnabled(bool value)
    {
        Enabled = value;
        RefreshVisual();
    }

    public void SetText(string text) => Label.Text = text;

    /// <summary>Tints the label (null restores the default white / disabled-grey behavior).</summary>
    public void SetTextColor(Color? color)
    {
        TextColorOverride = color;
        RefreshVisual();
    }

    private void RefreshVisual()
    {
        BackgroundColor = Enabled ? Pressed ? PressFill : Hovered ? HoverFill : IdleFill : IdleFill;
        Label.ForegroundColor = Enabled ? TextColorOverride ?? LegendColors.White : DisabledText;
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

        if (Enabled)
            Clicked?.Invoke();

        e.Handled = true;
    }
}
