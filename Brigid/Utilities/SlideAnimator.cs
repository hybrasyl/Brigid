#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Utilities;

/// <summary>
///     Reusable horizontal slide animation state. Slides a UIElement between an off-screen position and a target position
///     with eased-quadratic-out interpolation. All 6+ sliding controls delegate to this instead of duplicating the logic.
/// </summary>
public struct SlideAnimator
{
    private const int TOTAL_FRAMES = 3;
    private const float TOTAL_DURATION_MS = 300f;
    private const float FRAME_INTERVAL_MS = TOTAL_DURATION_MS / TOTAL_FRAMES;

    private float FrameTimer;
    private int CurrentFrame;

    public int OffScreenX { get; private set; }
    public bool Sliding { get; private set; }
    public bool SlidingOut { get; private set; }
    public int TargetX { get; private set; }

    /// <summary>
    ///     Sets positions for a panel that slides from the right edge of a viewport.
    /// </summary>
    public void SetViewportBounds(Rectangle viewport, int panelWidth)
    {
        TargetX = viewport.X + viewport.Width - panelWidth;
        OffScreenX = viewport.X + viewport.Width;
    }

    /// <summary>
    ///     Sets positions for a panel that slides leftward from an anchor point.
    /// </summary>
    public void SetSlideAnchor(int anchorX, int panelWidth)
    {
        OffScreenX = anchorX;
        TargetX = anchorX - panelWidth;
    }

    /// <summary>
    ///     Begins a slide-in animation. Sets the element off-screen and visible.
    /// </summary>
    public void SlideIn(UIElement element)
    {
        element.X = OffScreenX;
        element.Visible = true;
        Sliding = true;
        SlidingOut = false;
        CurrentFrame = 0;
        FrameTimer = 0;
    }

    /// <summary>
    ///     Begins a slide-out animation.
    /// </summary>
    public void SlideOut()
    {
        Sliding = true;
        SlidingOut = true;
        CurrentFrame = 0;
        FrameTimer = 0;
    }

    /// <summary>
    ///     Shows the element at the target position instantly (no animation).
    /// </summary>
    public void ShowInPlace(UIElement element)
    {
        element.X = TargetX;
        element.Visible = true;
        Sliding = false;
    }

    /// <summary>
    ///     Hides the element and resets slide state.
    /// </summary>
    public void Hide(UIElement element)
    {
        element.Visible = false;
        Sliding = false;
        element.X = OffScreenX;
    }

    /// <summary>
    ///     Advances the slide animation. Returns true if a slide-out just completed (caller should handle close).
    /// </summary>
    public bool Update(GameTime gameTime, UIElement element)
    {
        if (!Sliding)
            return false;

        FrameTimer += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        var targetFrame = Math.Min((int)(FrameTimer / FRAME_INTERVAL_MS) + 1, TOTAL_FRAMES);

        if (targetFrame <= CurrentFrame)
            return false;

        CurrentFrame = targetFrame;

        var t = (float)CurrentFrame / TOTAL_FRAMES;

        if (SlidingOut)
        {
            element.X = (int)MathHelper.Lerp(TargetX, OffScreenX, t);

            if (CurrentFrame >= TOTAL_FRAMES)
            {
                Hide(element);

                return true;
            }
        } else
        {
            element.X = (int)MathHelper.Lerp(OffScreenX, TargetX, t);

            if (CurrentFrame >= TOTAL_FRAMES)
            {
                element.X = TargetX;
                Sliding = false;
            }
        }

        return false;
    }
}