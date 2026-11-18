#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     The profile text of the Details window (BRIG-151): a read-only text box on the input slot art, with the size of the
///     profile text editor's box, so it wraps the text as the editor does. A left click raises <see cref="Clicked" />. The
///     wheel scrolls a text that has more lines than the box shows, for example a text that the editor saved before its
///     box took this size (Sabrael, 2026-11-18).
/// </summary>
internal sealed class ProfileTextBox : UIPanel
{
    private readonly InputSlotPane Pane;
    private readonly UITextBox TextBox;

    public ProfileTextBox()
    {
        Name = "profile_text";

        TextBox = new UITextBox
        {
            Name = "text",
            IsMultiLine = true,
            IsReadOnly = true,
            IsHitTestVisible = false
        };
        TextBox.SetForegroundRole(ThemeRoles.InputText);

        Pane = new InputSlotPane
        {
            Name = "pane",
            IsHitTestVisible = false
        };
        Pane.AddChild(TextBox);
        AddChild(Pane);
    }

    public string Text
    {
        get => TextBox.Text;

        set
        {
            if (TextBox.Text == value)
                return;

            TextBox.Text = value;
            TextBox.ScrollOffset = 0;
        }
    }

    /// <summary>The first line that the box shows, from 0.</summary>
    internal int FirstLineShown => TextBox.ScrollOffset / TextRenderer.CHAR_HEIGHT;

    /// <summary>A left click: the host opens the profile text editor.</summary>
    public event Action? Clicked;

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        Pane.SetBounds(new Rectangle(0, 0, bounds.Width, bounds.Height));
        TextBox.SetBounds(new Rectangle(0, 0, bounds.Width, bounds.Height));
    }

    //the text box lays out its lines at each update, so the line count is current when the wheel turns
    public override void OnMouseScroll(MouseScrollEvent e)
    {
        var hidden = Math.Max(0, TextBox.LineCount - TextBox.VisibleLineCount);
        TextBox.ScrollOffset = Math.Clamp(TextBox.ScrollOffset - e.Delta * TextRenderer.CHAR_HEIGHT, 0, hidden * TextRenderer.CHAR_HEIGHT);
        e.Handled = true;
    }

    public override void OnClick(ClickEvent e)
    {
        if (e.Button == MouseButton.Left)
            Clicked?.Invoke();

        e.Handled = true;
    }
}
