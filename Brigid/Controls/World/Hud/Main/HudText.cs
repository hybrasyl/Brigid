#region
using Brigid.Controls.Components;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>The one setup of a line of text of the new HUD: centred in its height, no padding, in a theme colour role.</summary>
internal static class HudText
{
    /// <summary>
    ///     A label that does not take the mouse, so the control under it gets the hover and the click. The caller places it
    ///     and adds it.
    /// </summary>
    public static UILabel Label(string name, string role, HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = new UILabel
        {
            Name = name,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            PaddingLeft = 0,
            PaddingRight = 0,
            IsHitTestVisible = false
        };

        label.SetForegroundRole(role);

        return label;
    }
}
