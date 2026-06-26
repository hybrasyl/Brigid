#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Panel;

/// <summary>
///     Stat raise button used in <see cref="StatsPanel" />. Alternates its <see cref="UIButton.NormalTexture" /> between
///     two frames every 500 ms while animating to pulse attention when the player has unspent stat points.
/// </summary>
public sealed class StatButton : UIButton
{
    private const float INTERVAL_MS = 500f;

    private Texture2D? FrameA;
    private Texture2D? FrameB;
    private float Timer;
    private bool Phase;

    public bool IsAnimating { get; private set; }

    /// <summary>
    ///     Assigns the two frames to swap between on each tick. Frame A is also set as the current NormalTexture and is
    ///     restored whenever the animation stops.
    /// </summary>
    public void SetFrames(Texture2D frameA, Texture2D frameB)
    {
        FrameA = frameA;
        FrameB = frameB;
        NormalTexture = frameA;
    }

    /// <summary>
    ///     Starts or stops the frame-swap animation. Setting to the current value is a no-op. On transition, the timer
    ///     and phase reset and <see cref="UIButton.NormalTexture" /> reverts to Frame A.
    /// </summary>
    public void SetAnimating(bool enabled)
    {
        if (IsAnimating == enabled)
            return;

        IsAnimating = enabled;
        Timer = 0f;
        Phase = false;
        NormalTexture = FrameA;
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
        NormalTexture = Phase ? FrameB : FrameA;
    }
}
