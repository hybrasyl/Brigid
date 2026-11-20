#region
using Brigid.Controls.Components;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     A slot of the effect bar (BRIG-147): the <see cref="ThemeRoles.HudEffectSlot" /> part, else a dark box with a border
///     in the theme's divider colour, with the effect's icon in it and the clock swipe over the icon. The icon is the
///     cached texture of <see cref="UiRenderer" />; the slot does not own it.
/// </summary>
internal sealed class HudEffectSlot : UIElement
{
    private readonly Func<ushort, Texture2D?> IconSource;
    private readonly ClockWedgeCache Wedges;

    public HudEffectSlot(ClockWedgeCache wedges, Func<ushort, Texture2D?> iconSource)
    {
        Wedges = wedges;
        IconSource = iconSource;
        IsHitTestVisible = false;
        Visible = false;
    }

    /// <summary>The icon of the effect in the slot, or null for an empty slot.</summary>
    public ushort? Icon { get; private set; }

    /// <summary>The texture of the icon.</summary>
    public Texture2D? IconTexture { get; private set; }

    /// <summary>The part of the time of the effect that has gone, from 0 to 1.</summary>
    public float Gone { get; private set; }

    /// <summary>Shows the effect of <paramref name="icon" />; the texture is read again only for a new icon.</summary>
    public void Show(ushort icon, float gone)
    {
        if (Icon != icon)
        {
            Icon = icon;
            IconTexture = IconSource(icon);
        }

        Gone = Math.Clamp(gone, 0f, 1f);
        Visible = true;
    }

    /// <summary>The part of an icon texture that the slot draws: at most the icon square, from the top left.</summary>
    internal static Rectangle IconSourceRect(int width, int height)
        => new(0, 0, Math.Min(width, MainHudLayout.EFFECT_ICON), Math.Min(height, MainHudLayout.EFFECT_ICON));

    public void Clear()
    {
        Icon = null;
        IconTexture = null;
        Gone = 0;
        Visible = false;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        if ((ClipRect.Width <= 0) || (ClipRect.Height <= 0))
            return;

        var bounds = new Rectangle(ScreenX, ScreenY, Width, Height);

        DrawSlotBox(spriteBatch, bounds, ThemeRoles.HudEffectSlot);

        var icon = new Vector2(ScreenX + MainHudLayout.EffectIconInSlot.X, ScreenY + MainHudLayout.EffectIconInSlot.Y);

        //the placeholder of an unknown icon is the 32 x 32 checker: only the icon's own square draws, so the border stays
        if (IconTexture is { } texture)
            DrawTexture(spriteBatch, texture, icon, IconSourceRect(texture.Width, texture.Height), Color.White);

        DrawTexture(spriteBatch, Wedges.ForGone(spriteBatch.GraphicsDevice, Gone), icon, ClockWedgeCache.SwipeColor);
    }
}
