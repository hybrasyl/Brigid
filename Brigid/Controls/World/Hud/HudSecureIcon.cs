#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud;

/// <summary>
///     The padlock at the right edge of the HUD's server-name box, showing whether the world connection
///     is carried over TLS. Shared by both HUD layouts, which build the same box from the same prefab
///     rect.
/// </summary>
/// <remarks>
///     Its own label rather than a suffix on the server name: the name is centred in the box and comes
///     from the server, so appending to it would move the name whenever the transport changed and would
///     let a server put a padlock in the HUD by naming itself with one.
/// </remarks>
internal static class HudSecureIcon
{
    /// <summary>
    ///     Drawn below <see cref="FontEngine.UiSize" />. That size is chosen per face from the ink of the
    ///     <em>primary</em> face, and the padlock comes from the emoji fallback, whose ink is square and
    ///     fills its em — so at the UI size it overflows the 12px cell the prefab boxes are built on and
    ///     the top of the lock lands outside the box.
    /// </summary>
    private const int GLYPH_SIZE = 10;

    //wide enough for the glyph's square ink at GLYPH_SIZE; the box's own text is centred and does not reach it.
    private const int WIDTH = 12;

    /// <summary>Builds the icon for a server-name box occupying <paramref name="serverBox" />.</summary>
    public static UILabel Create(Rectangle serverBox)
    {
        var icon = new UILabel
        {
            Name = "SecureIcon",
            X = serverBox.Right - WIDTH,
            Y = serverBox.Y,
            Width = WIDTH,
            Height = serverBox.Height,
            FontSize = GLYPH_SIZE,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Text = Glyphs.PADLOCK,
            PaddingLeft = 0,
            PaddingRight = 0,
            ShrinkToFit = false,
            IsHitTestVisible = false
        };

        Apply(icon, false);

        return icon;
    }

    /// <summary>
    ///     Colours the padlock for the current transport: green encrypted, white not. Colour rather than
    ///     presence, so the indicator occupies the same space either way and a reader who sees no green
    ///     is looking at an answer rather than at nothing.
    /// </summary>
    public static void Apply(UILabel icon, bool secure)
        => icon.ForegroundColor = secure ? LegendColors.Lime : LegendColors.White;
}
