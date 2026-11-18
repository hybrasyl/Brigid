#region
using Brigid.Rendering.Utility;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     The masks of the clock swipe of one size (<see cref="ImageUtil.BuildClockWedge" />), one for each step of
///     <see cref="STEPS" />, built when a draw first needs them. A part of the time is rounded to the nearest step, so a
///     swipe that moves smoothly (an exact time left, HS-1723) builds at most <see cref="STEPS" /> masks; the six colour
///     bands of 0x3A fall on steps exactly. The owner disposes the cache.
/// </summary>
internal sealed class ClockWedgeCache(int size) : IDisposable
{
    /// <summary>The steps of a full turn: a 15 px mask has about 56 edge pixels, so a finer step does not show.</summary>
    public const int STEPS = 60;

    private readonly Texture2D?[] Masks = new Texture2D?[STEPS + 1];

    /// <summary>The step of <paramref name="gone" /> (0 to 1 of a turn), rounded to the nearest.</summary>
    public static int StepOf(float gone) => (int)MathF.Round(Math.Clamp(gone, 0f, 1f) * STEPS);

    /// <summary>The mask for <paramref name="gone" />, or null when no time has gone.</summary>
    public Texture2D? For(GraphicsDevice device, float gone)
    {
        var step = StepOf(gone);

        return step == 0 ? null : Masks[step] ??= ImageUtil.BuildClockWedge(device, size, (float)step / STEPS);
    }

    public void Dispose()
    {
        for (var i = 0; i < Masks.Length; i++)
        {
            Masks[i]?.Dispose();
            Masks[i] = null;
        }
    }
}
