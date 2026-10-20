#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Draws the new art of an icon role (<c>icon.*</c>, for example <c>icon.status.awake</c>): the part of the installed
///     <c>ui_theme</c> pack, else Brigid's own image (<see cref="BuiltInArt" />). The caller draws its legacy art when this
///     gives false.
///     <para>
///         An icon is content, not the look of a panel. So the theme part shows on every host, also on a host that does not
///         take the theme (<c>UseTheme</c>), in the same way as the ability, item and legend mark icon packs. Do not draw an
///         icon role through <c>UIElement.TryDrawThemePart</c>, which shows a part only on a themed host.
///     </para>
/// </summary>
public static class IconArt
{
    /// <summary>
    ///     True when the theme has a usable image for <paramref name="role" />, or Brigid has one. A caller that reads its
    ///     legacy art only when there is no new art uses this.
    /// </summary>
    public static bool Has(string role) => ThemeArt.HasUsablePart(role) || BuiltInArt.Has(role);

    /// <summary>
    ///     Draws the new art for <paramref name="role" /> into a logical rectangle, clipped to <paramref name="clip" />. Call
    ///     from the native UI pass. Returns false, and draws nothing, when there is no new art.
    /// </summary>
    public static bool TryDraw(SpriteBatch spriteBatch, string role, Rectangle bounds, Rectangle clip, Color? tint = null)
        => ThemeArt.TryDraw(spriteBatch, role, bounds, clip, tint: tint) || BuiltInArt.TryDraw(spriteBatch, role, bounds, clip, tint);
}
