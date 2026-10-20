#region
using Brigid.Controls.Components;
using Brigid.Rendering;
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
///         box without the change echoing back as a user toggle. Fill is palette-driven (idle/hover).
///     </para>
///     <para>
///         A themed box draws the <see cref="ThemeRoles.CheckBoxNormal" /> or <see cref="ThemeRoles.CheckBoxHover" /> part
///         in place of the flat fill and border, and the <see cref="ThemeRoles.CheckBoxMark" /> part over the whole box when
///         it is checked. With no part, it draws the flat look: the border in the theme's <see cref="ThemeRoles.Border" />
///         colour and the check in its <see cref="ThemeRoles.TextSelected" /> colour.
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

    public CheckBox()
    {
        Width = DEFAULT_SIZE;
        Height = DEFAULT_SIZE;
        BorderColor = DialogPalette.FrameBorder;
        BackgroundColor = DialogPalette.ButtonIdleFill;
    }

    //the flat border is part of the look
    protected internal override Color? DrawnBorderColor => BorderColor is null ? null : ThemeColor(ThemeRoles.Border);

    /// <summary>
    ///     Shows the hover look, or not, with no mouse, for the primitive gallery. The check box then takes no mouse input,
    ///     so that the mouse does not set the state again.
    /// </summary>
    internal void ShowState(bool hovered)
    {
        IsHitTestVisible = false;
        Hovered = hovered;
        RefreshFill();
    }

    private void RefreshFill() => BackgroundColor = Hovered ? DialogPalette.ButtonHoverFill : DialogPalette.ButtonIdleFill;

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        var box = new Rectangle(ScreenX, ScreenY, Width, Height);

        //a themed box draws its part in place of the flat fill and border, and the flat box when the part does not draw
        UpdateClipRect();

        if (!TryDrawThemePart(spriteBatch, Hovered ? ThemeRoles.CheckBoxHover : ThemeRoles.CheckBoxNormal, box, ThemeRoles.CheckBoxNormal))
            base.Draw(spriteBatch); //clip + box fill + border

        if (!Checked)
            return;

        //the mark part covers the whole box, so its art holds its own margin
        if (TryDrawThemePart(spriteBatch, ThemeRoles.CheckBoxMark, box))
            return;

        var inner = new Rectangle(ScreenX + CHECK_INSET, ScreenY + CHECK_INSET, Width - 2 * CHECK_INSET, Height - 2 * CHECK_INSET);
        DrawRectClipped(spriteBatch, inner, ThemeColor(ThemeRoles.TextSelected));
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
