#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud;

/// <summary>
///     The padlock shown at the right edge of the HUD's server-name box while the world connection is
///     carried over TLS. Shared by both HUD layouts, which build the same box from the same prefab rect.
/// </summary>
/// <remarks>
///     Its own label rather than a suffix on the server name: the name is centred in the box and comes
///     from the server, so appending to it would move the name whenever the transport changed and would
///     let a server put a padlock in the HUD by naming itself with one.
/// </remarks>
internal static class HudSecureIcon
{
    //wide enough for the glyph's square ink at UI size; the box's own text is centred and does not reach it.
    private const int WIDTH = 13;

    /// <summary>Builds the icon for a server-name box occupying <paramref name="serverBox" />, hidden.</summary>
    public static UILabel Create(Rectangle serverBox)
        => new()
        {
            Name = "SecureIcon",
            X = serverBox.Right - WIDTH,
            Y = serverBox.Y,
            Width = WIDTH,
            Height = serverBox.Height,
            HorizontalAlignment = HorizontalAlignment.Right,
            ForegroundColor = LegendColors.White,
            Text = Glyphs.PADLOCK,
            PaddingLeft = 0,
            PaddingRight = 0,
            ShrinkToFit = false,
            IsHitTestVisible = false,
            Visible = false
        };
}
