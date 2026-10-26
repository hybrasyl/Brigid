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

    //a clock in milliseconds that the test moves
    private sealed class ManualClock : TimeProvider
    {
        public long Ms;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => Ms;
    }

    //runs updates with a wall clock that moves wallMsPerUpdate at each Update, and draws on every drawEvery-th Update
    private static int MeasureFps(int updates, int wallMsPerUpdate, int drawEvery, int stallBeforeUpdate = -1)
    {
        var previousClock = DebugOverlay.Clock;
        var clock = new ManualClock();

        DebugOverlay.Clock = clock;
        DebugOverlay.IsActive = true;
        DebugOverlay.ResetFrameCounters();

        try
        {
            for (var i = 0; i < updates; i++)
            {
                if (i == stallBeforeUpdate)
                    clock.Ms += 5000;

                DebugOverlay.BeginFrame();
                DebugOverlay.Update(Step);

                if ((i % drawEvery) == (drawEvery - 1))
                    DebugOverlay.EndFrame();

                clock.Ms += wallMsPerUpdate;
            }

            return DebugOverlay.FramesPerSecond;
        } finally
        {
            DebugOverlay.IsActive = false;
            DebugOverlay.ResetFrameCounters();
            DebugOverlay.Clock = previousClock;
        }
    }

    //50 Updates and 25 Draws each second, for two seconds. A window closes on the first Update at or after its second,
    //so the 101st Update closes the second window
    [Fact]
    public void FramesPerSecond_CountsDrawnFramesWhenUpdatesSkipDraws() => Assert.Equal(25, MeasureFps(101, 20, 2));

    //a 5 s stall before Update 60 closes one window and starts the next at that Update; the window after it reads 25
    [Fact]
    public void FramesPerSecond_StartsTheWindowAgainAfterAStall() => Assert.Equal(25, MeasureFps(111, 20, 2, 60));

    //each 20 ms game step takes 40 ms of real time: 25 Updates and 25 Draws each real second. On game time the window
    //closed after 50 Updates, which is two real seconds, and showed 50
    [Fact]
    public void FramesPerSecond_UsesWallTimeWhenUpdatesFallBehind() => Assert.Equal(25, MeasureFps(51, 40, 1));
}
