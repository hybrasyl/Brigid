#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A primitive, asset-free toggle: a <see cref="DialogPalette" />-outlined square that fills with a check
///     rect when <see cref="Checked" />. Replaces the settings panel's sprite buttons-as-toggles. The consumer
///     supplies its own sibling <see cref="UILabel" /> (matching the existing settings row layout, whose label
///     text is formatted per-setting by the consumer). Lives beside its themed sibling primitives
///     <see cref="TextButton" /> / <see cref="SelectableTab" /> / <see cref="DialogPalette" />.
///     <para>
///         Setting <see cref="Checked" /> directly is silent — <see cref="CheckedChanged" /> fires only on a
///         user click. That lets a consumer mirror external state (e.g. a <c>UserOptions</c> toggle) into the
///         box without the change echoing back as a user toggle. Fill is palette-driven (idle/hover);
///         <c>BorderColor</c> and <see cref="CheckColor" /> may be overridden.
///     </para>
/// </summary>
public sealed class CheckBox : UIElement
{
    public const int DEFAULT_SIZE = 14;

    //inset of the filled check rect from each edge of the box.
    private const int CHECK_INSET = 3;

    private bool Hovered;

    public event Action<bool>? CheckedChanged;

    public bool Checked { get; set; }

    /// <summary>The fill drawn for the check when <see cref="Checked" /> (defaults to the palette's selected accent).</summary>
    public Color CheckColor { get; set; } = DialogPalette.SelectedText;

    public CheckBox()
    {
        Width = DEFAULT_SIZE;
        Height = DEFAULT_SIZE;
        BorderColor = DialogPalette.FrameBorder;
        BackgroundColor = DialogPalette.ButtonIdleFill;
    }

    private void RefreshFill() => BackgroundColor = Hovered ? DialogPalette.ButtonHoverFill : DialogPalette.ButtonIdleFill;

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch); //clip + box fill + border

        if (!Visible || !Checked)
            return;

        var inner = new Rectangle(ScreenX + CHECK_INSET, ScreenY + CHECK_INSET, Width - 2 * CHECK_INSET, Height - 2 * CHECK_INSET);
        DrawRectClipped(spriteBatch, inner, CheckColor);
    }

    public override void ResetInteractionState()
    {
        Hovered = false;
        RefreshFill();
    }

    public override void OnMouseEnter()
    {
        Hovered = true;
        RefreshFill();
    }

    public override void OnMouseLeave()
    {
        Hovered = false;
        RefreshFill();
    }

    public override void OnClick(ClickEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        Checked = !Checked;
        CheckedChanged?.Invoke(Checked);
        e.Handled = true;
    }
}
