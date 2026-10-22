#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A primitive, asset-free stepper: a previous button, a centred value and a next button, in one row. The host owns the
///     value: it handles <see cref="Previous" /> and <see cref="Next" />, and shows the result with <see cref="SetValue" />.
///     The children are named <c>previous</c>, <c>value</c> and <c>next</c>, so a <c>ui_panels</c> pack can address them
///     under the stepper's own name.
/// </summary>
public sealed class Stepper : UIPanel
{
    private readonly UILabel Value;

    /// <summary>Raised when the player presses the previous button.</summary>
    public event Action? Previous;

    /// <summary>Raised when the player presses the next button.</summary>
    public event Action? Next;

    /// <summary>The text between the buttons.</summary>
    public string ValueText => Value.Text;

    /// <param name="width">Width of the whole stepper.</param>
    /// <param name="height">Height of the row. The buttons are square.</param>
    public Stepper(int width, int height)
    {
        Width = width;
        Height = height;

        var back = new TextButton("<", height, height) { Name = "previous", X = 0, Y = 0 };
        back.Clicked += () => Previous?.Invoke();

        var forward = new TextButton(">", height, height) { Name = "next", X = width - height, Y = 0 };
        forward.Clicked += () => Next?.Invoke();

        Value = new UILabel
        {
            Name = "value",
            X = height,
            Y = 0,
            Width = width - 2 * height,
            Height = height,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        AddChild(back);
        AddChild(Value);
        AddChild(forward);
    }

    public void SetValue(string text) => Value.Text = text;

    public override void Draw(SpriteBatch spriteBatch)
    {
        //the theme can come from a parent, which can change after construction, so the colour is resolved per draw
        Value.ForegroundColor = ThemeColor(ThemeRoles.Text, TextColors.Default);

        base.Draw(spriteBatch);
    }
}
