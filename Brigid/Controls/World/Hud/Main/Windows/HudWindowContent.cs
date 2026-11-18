#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     The content of a window of the new HUD, which a window card binds to its panel with
///     <see cref="HudWindowHost.BindPanel" />. The panel gives the content its bounds, and a themed frame with a wide
///     border makes them smaller than the flat ones, so the content lays out in <see cref="LayOut" /> from the size that
///     it gets, not from fixed numbers.
/// </summary>
internal abstract class HudWindowContent : UIPanel
{
    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        LayOut(bounds.Width, bounds.Height);
    }

    /// <summary>Places the children in a content of <paramref name="width" /> by <paramref name="height" />.</summary>
    protected abstract void LayOut(int width, int height);
}
