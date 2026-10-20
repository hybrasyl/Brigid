#region
using Brigid.Controls.Components;
using Brigid.Rendering;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A social status icon (BRIG-122): the new art of its <c>icon.status.*</c> role (<see cref="IconArt" />: the
///     <c>ui_theme</c> part, else Brigid's own icon), else the legacy frame that the host gives in
///     <see cref="LegacyTexture" />. The new art shows on every host, themed or not.
///     <para>
///         The status book, the other-profile view and the Aisling list show it. The HUD emote button is a prefab button,
///         so <see cref="ShowOn" /> puts the icon over the button when there is new art, and gives the button the legacy
///         frame when there is none.
///     </para>
/// </summary>
public sealed class StatusIcon : UIElement
{
    /// <summary>The status to show, or null for none.</summary>
    public SocialStatus? Status { get; set; }

    /// <summary>The legacy frame for <see cref="Status" />, drawn at its own size when there is no new art.</summary>
    public Texture2D? LegacyTexture { get; set; }

    /// <summary>The <c>ui_theme</c> role of <paramref name="status" />, or null for a value that is not a known status.</summary>
    public static string? RoleFor(SocialStatus status)
        => (byte)status < ThemeRoles.StatusIcons.Length ? ThemeRoles.StatusIcons[(byte)status] : null;

    /// <summary>True when the theme or Brigid has an image for <paramref name="status" />, so no legacy frame is needed.</summary>
    public static bool HasArt(SocialStatus status)
        => RoleFor(status) is { } role && IconArt.Has(role);

    /// <summary>The legacy status icon of the status book and the Aisling list: a frame of <c>_nemots.spf</c>.</summary>
    public static Texture2D? NemotsFrame(SocialStatus status) => UiRenderer.Instance?.GetSpfTexture("_nemots.spf", (byte)status);

    /// <summary>
    ///     Shows <paramref name="status" />. The legacy frame comes from <paramref name="legacyFrame" /> only when there is no
    ///     new art, so that a host with new art reads no dat file. Returns true when the icon has something to draw.
    /// </summary>
    public bool Show(SocialStatus status, Func<SocialStatus, Texture2D?> legacyFrame)
    {
        Status = status;

        if (RoleFor(status) is null)
        {
            LegacyTexture = null;

            return false;
        }

        if (HasArt(status))
        {
            LegacyTexture = null;

            return true;
        }

        LegacyTexture = legacyFrame(status);

        return LegacyTexture is not null;
    }

    /// <summary>
    ///     Shows <paramref name="status" /> on a prefab <paramref name="button" /> that this icon lies over. With new art,
    ///     the icon draws it and the button draws none of its legacy frames, also not the hover and pressed frames. Else the
    ///     icon hides, and the button shows <paramref name="legacyFrame" />; a null frame leaves the button as it is.
    /// </summary>
    public void ShowOn(UIButton button, SocialStatus status, Func<Texture2D?> legacyFrame)
    {
        Status = status;
        LegacyTexture = null;
        Visible = HasArt(status);

        if (Visible)
        {
            button.NormalTexture = null;
            button.SelectedTexture = null;
            button.HoverTexture = null;
            button.PressedTexture = null;

            return;
        }

        if (legacyFrame() is not { } frame)
            return;

        button.NormalTexture = frame;
        button.SelectedTexture = frame;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        if ((ClipRect.Width <= 0) || (ClipRect.Height <= 0))
            return;

        if (Status is { } status && RoleFor(status) is { } role
            && IconArt.TryDraw(spriteBatch, role, new Rectangle(ScreenX, ScreenY, Width, Height), ClipRect))
            return;

        if (LegacyTexture is not null)
            DrawTexture(spriteBatch, LegacyTexture, new Vector2(ScreenX, ScreenY), Color.White);
    }
}
