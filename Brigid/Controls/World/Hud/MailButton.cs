#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud;

/// <summary>
///     Mail notification button used in the bottom-right HUD. While animating, pulses a 1-pixel border in
///     <see cref="PulseColor" /> on top of the button texture every 500 ms. Mirrors the retail Dark Ages mail blink —
///     see <c>docs/re_notes/mail_parcel_indicator.md</c>.
/// </summary>
public sealed class MailButton : UIButton
{
    private const float INTERVAL_MS = 500f;

    private float Timer;
    private bool Phase;

    public bool IsAnimating { get; private set; }

    public Color PulseColor { get; set; } = Color.Yellow;

    /// <summary>
    ///     Starts or stops the pulse. Setting to the current value is a no-op so repeated "unread mail" updates from the
    ///     server don't restart the timer.
    /// </summary>
    public void SetAnimating(bool enabled)
    {
        if (IsAnimating == enabled)
            return;

        IsAnimating = enabled;
        Timer = 0f;
        Phase = false;
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible)
            return;

        base.Update(gameTime);

        if (!IsAnimating)
            return;

        Timer += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        if (Timer < INTERVAL_MS)
            return;

        Timer -= INTERVAL_MS;
        Phase = !Phase;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);

        if (IsAnimating && Phase)
            DrawBorder(spriteBatch, new Rectangle(ScreenX, ScreenY, Width, Height), PulseColor);
    }
}
