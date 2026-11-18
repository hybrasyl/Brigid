#region
using Brigid.Rendering.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     The masks of the clock swipe of one size (<see cref="ImageUtil.BuildClockWedge" />), for each step of
///     <see cref="STEPS" />, built when a draw first needs them. The effect bar covers the time that has gone (a wedge from
///     12 o'clock); the Abilities window covers the cooldown that is left (the rest of the turn after the time gone), so
///     the edge of both moves clockwise. A part of the time is rounded to the nearest step, so a swipe that moves smoothly
///     builds at most <see cref="STEPS" /> masks of each kind; the six colour bands of 0x3A fall on steps exactly. The
///     owner disposes the cache.
/// </summary>
internal sealed class ClockWedgeCache(int size) : IDisposable
{
    /// <summary>The steps of a full turn: a 15 px mask has about 56 edge pixels, so a finer step does not show.</summary>
    public const int STEPS = 60;

    /// <summary>The tint of the swipe: it darkens the icon, and it carries meaning, so it is not a theme colour.</summary>
    public static readonly Color SwipeColor = Color.Black * 0.6f;

    private readonly Texture2D?[] GoneMasks = new Texture2D?[STEPS + 1];
    private readonly Texture2D?[] LeftMasks = new Texture2D?[STEPS + 1];

    /// <summary>The step of <paramref name="part" /> (0 to 1 of a turn), rounded to the nearest.</summary>
    public static int StepOf(float part) => (int)MathF.Round(Math.Clamp(part, 0f, 1f) * STEPS);

    /// <summary>The mask that covers the time gone, from 12 o'clock to <paramref name="gone" />; null when none has gone.</summary>
    public Texture2D? ForGone(GraphicsDevice device, float gone)
    {
        var step = StepOf(gone);

        return step == 0 ? null : GoneMasks[step] ??= ImageUtil.BuildClockWedge(device, size, 0, (float)step / STEPS);
    }

    /// <summary>The mask that covers the time left, from the time gone to the full turn; null when none is left.</summary>
    public Texture2D? ForLeft(GraphicsDevice device, float left)
    {
        var step = StepOf(left);

        return step == 0 ? null : LeftMasks[step] ??= ImageUtil.BuildClockWedge(device, size, 1f - (float)step / STEPS, 1);
    }

    public void Dispose()
    {
        foreach (var masks in new[] { GoneMasks, LeftMasks })
            for (var i = 0; i < masks.Length; i++)
            {
                masks[i]?.Dispose();
                masks[i] = null;
            }
    }
}
