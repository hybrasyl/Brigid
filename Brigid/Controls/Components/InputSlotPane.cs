#region
using Brigid.Controls.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Components;

/// <summary>
///     A content pane that draws a dark, bordered "input slot" box behind each of its <see cref="UITextBox" />
///     children, so the fillable areas read as solid boxes. The legacy prefabs baked these into their background
///     art; the from-scratch modals draw them.
/// </summary>
public sealed class InputSlotPane : UIPanel
{
    private static readonly Color SlotFill = new(0, 0, 0, 205);
    private static readonly Color SlotBorder = DialogPalette.Divider;

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        //behind the children (drawn by base.Draw): a filled box per text field.
        foreach (var child in Children)
            if (child is UITextBox { Visible: true } box)
                DrawBorderedRect(spriteBatch, new Rectangle(box.ScreenX, box.ScreenY, box.Width, box.Height), SlotFill, SlotBorder);

        base.Draw(spriteBatch);
    }
}
