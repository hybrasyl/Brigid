#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
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
    private bool Hovered;
    private bool Pressed;

    public event ClickedHandler? Clicked;

    public TextButton(string text, int width, int height)
    {
        Width = width;
        Height = height;
        BorderColor = EdgeColor;

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
            IsHitTestVisible = false
        };

        AddChild(Label);
        RefreshVisual();
    }

    //Enabled has no change hook on UIElement, so callers flip state through here to refresh the visual
    public void SetEnabled(bool value)
    {
        Enabled = value;
        RefreshVisual();
    }

    private void RefreshVisual()
    {
        BackgroundColor = Enabled ? Pressed ? PressFill : Hovered ? HoverFill : IdleFill : IdleFill;
        Label.ForegroundColor = Enabled ? LegendColors.White : DisabledText;
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
