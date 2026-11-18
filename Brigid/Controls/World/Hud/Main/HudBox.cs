#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     A box of the top bar of the new HUD: the <see cref="ThemeRoles.HudBox" /> part, else a dark fill with a border in the
///     theme's divider colour. Its labels and icons do not take the mouse, so the box gets the hover and the click.
/// </summary>
internal sealed class HudBox : UIPanel
{
    public HudBox(string name, Rectangle bounds)
    {
        Name = name;
        SetBounds(bounds);
        BackgroundColor = DialogPalette.SlotFill;
        BorderColor = DialogPalette.Divider;
    }

    /// <summary>A left click on the box.</summary>
    public event Action? Clicked;

    /// <summary>The mouse came over the box.</summary>
    public event Action<HudBox>? Entered;

    /// <summary>The mouse left the box.</summary>
    public event Action? Left;

    //the flat border is part of the look
    protected internal override Color? DrawnBorderColor => ThemeColor(ThemeRoles.Divider);

    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (TryDrawThemePart(spriteBatch, ThemeRoles.HudBox, bounds))
            return;

        base.DrawChrome(spriteBatch, bounds);
    }

    public override void OnClick(ClickEvent e)
    {
        if (e.Button == MouseButton.Left)
            Clicked?.Invoke();

        e.Handled = true;
    }

    public override void OnMouseEnter() => Entered?.Invoke(this);

    public override void OnMouseLeave() => Left?.Invoke();
}
