#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A primitive, asset-free selectable tab: a centered label in a <see cref="DialogPalette" />-framed box
///     that swaps to a highlighted fill/border/text when <see cref="IsSelected" />. Unifies the near-identical
///     tab widgets hand-rolled by <c>WorldListControl</c> (class filters) and <c>BankShopPanel</c> (categories)
///     so tab columns read identically across the modernized-dialog surfaces. Lives beside its sibling
///     primitives <see cref="TextButton" /> / <see cref="DialogPalette" />. No hover visual (matching those
///     templates); the optional <see cref="Hovered" />/<see cref="Unhovered" /> events replace BankShop's
///     parent-cast tooltip callbacks without coupling the primitive to any specific parent.
/// </summary>
public sealed class SelectableTab : UIPanel
{
    //2px horizontal inset keeps a long label (set dynamically via SetText) off the 1px border, matching
    //BankShopPanel.CategoryTab's guard.
    private const int LABEL_INSET = 2;

    private readonly UILabel Label;

    public event ClickedHandler? Clicked;
    public event Action? Hovered;
    public event Action? Unhovered;

    public bool IsSelected
    {
        get;
        set
        {
            field = value;
            BackgroundColor = value ? DialogPalette.TabSelectedFill : DialogPalette.TabIdleFill;
            BorderColor = value ? DialogPalette.TabSelectedBorder : DialogPalette.TabIdleBorder;
            Label.ForegroundColor = value ? DialogPalette.SelectedText : LegendColors.White;
        }
    }

    public SelectableTab(string text, int width, int height)
    {
        Width = width;
        Height = height;
        BackgroundColor = DialogPalette.TabIdleFill;
        BorderColor = DialogPalette.TabIdleBorder;

        Label = new UILabel
        {
            X = LABEL_INSET,
            Y = 0,
            Width = width - 2 * LABEL_INSET,
            Height = height,
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ShrinkToFit = false,
            ForegroundColor = LegendColors.White,
            IsHitTestVisible = false
        };

        AddChild(Label);
    }

    public void SetText(string text) => Label.Text = text;

    public override void OnMouseEnter() => Hovered?.Invoke();

    public override void OnMouseLeave() => Unhovered?.Invoke();

    public override void OnClick(ClickEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        Clicked?.Invoke();
        e.Handled = true;
    }
}
