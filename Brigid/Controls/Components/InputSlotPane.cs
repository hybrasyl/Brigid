#region
using Brigid.Controls.Generic;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Components;

/// <summary>
///     A content pane that draws a dark, bordered "input slot" box behind each of its <see cref="UITextBox" />
///     children, so the fillable areas read as solid boxes. The legacy prefabs baked these into their background
///     art; the from-scratch modals draw them. A pane that is itself a form hosts one of these and adds its
///     fields to it, rather than inheriting — the chrome is a decorator, not a base class.
///     <para>
///         A themed pane draws the theme's <see cref="ThemeRoles.InputSlot" /> part behind each field, and
///         <see cref="ThemeRoles.InputSlotFocus" /> (else <see cref="ThemeRoles.InputSlot" />) behind the focused field. With
///         no part, it draws the flat box, with the border in the theme's <see cref="ThemeRoles.Divider" /> colour.
///     </para>
/// </summary>
public sealed class InputSlotPane : UIPanel
{
    private static readonly Color SlotFill = DialogPalette.SlotFill;

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        //the slots draw before base.Draw, which would refresh the clip only after them
        UpdateClipRect();

        //behind the children (drawn by base.Draw): a filled box per text field.
        foreach (var child in Children)
            if (child is UITextBox { Visible: true } box)
                DrawSlot(spriteBatch, new Rectangle(box.ScreenX, box.ScreenY, box.Width, box.Height), box.IsFocused);

        base.Draw(spriteBatch);
    }

    private void DrawSlot(SpriteBatch spriteBatch, Rectangle slot, bool focused)
    {
        if (focused
                ? TryDrawThemePart(spriteBatch, ThemeRoles.InputSlotFocus, slot, ThemeRoles.InputSlot)
                : TryDrawThemePart(spriteBatch, ThemeRoles.InputSlot, slot))
            return;

        DrawBorderedRect(spriteBatch, slot, SlotFill, ThemeColor(ThemeRoles.Divider));
    }
}
