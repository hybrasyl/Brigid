using System.Diagnostics;
using Brigid.Controls.Generic;
using Microsoft.Xna.Framework;
using Xunit;

namespace Brigid.Tests;

// DebugOverlay is a static class, so tests that change its state run serially.
[CollectionDefinition("DebugOverlay", DisableParallelization = true)]
public sealed class DebugOverlayCollection;

// The client runs a fixed time step. Under load, MonoGame runs catch-up Updates and skips Draws, so the F11 FPS counter
// counted Updates and showed the target rate while the drawn rate fell. It now counts in EndFrame, once per Draw, and
// closes each one-second window on wall time, not on the game time of the Updates (BRIG-111). The tests give the
// overlay a clock that moves a known step at each Update.
[Collection("DebugOverlay")]
public class DebugOverlayFpsTests
{
    private static readonly GameTime Step = new(TimeSpan.Zero, TimeSpan.FromMilliseconds(20));

    //runs updates with a wall clock that moves wallMsPerUpdate at each Update, and draws on every drawEvery-th Update
    private static int MeasureFps(int updates, int wallMsPerUpdate, int drawEvery)
    {
        var previousClock = DebugOverlay.GetTimestamp;
        long ticks = 0;
        var ticksPerUpdate = Stopwatch.Frequency * wallMsPerUpdate / 1000;

        DebugOverlay.GetTimestamp = () => ticks;
        DebugOverlay.IsActive = true;
        DebugOverlay.ResetFrameCounters();

        try
        {
            for (var i = 0; i < updates; i++)
            {
                DebugOverlay.BeginFrame();
                DebugOverlay.Update(Step);

                if ((i % drawEvery) == (drawEvery - 1))
                    DebugOverlay.EndFrame();

                ticks += ticksPerUpdate;
            }

            return DebugOverlay.FramesPerSecond;
        } finally
        {
            DebugOverlay.IsActive = false;
            DebugOverlay.GetTimestamp = previousClock;
        }
    }

    //50 Updates and 25 Draws each second, for two seconds
    [Fact]
    public void FramesPerSecond_CountsDrawnFramesWhenUpdatesSkipDraws() => Assert.Equal(25, MeasureFps(101, 20, 2));

    //each 20 ms game step takes 40 ms of real time: 25 Updates and 25 Draws each real second. On game time the window
    //closed after 50 Updates, which is two real seconds, and showed 50
    [Fact]
    public void FramesPerSecond_UsesWallTimeWhenUpdatesFallBehind() => Assert.Equal(25, MeasureFps(51, 40, 1));
}
