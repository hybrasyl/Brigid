using Brigid.Controls.Generic;
using Microsoft.Xna.Framework;
using Xunit;

namespace Brigid.Tests;

// DebugOverlay is a static class, so tests that change its state run serially.
[CollectionDefinition("DebugOverlay", DisableParallelization = true)]
public sealed class DebugOverlayCollection;

// The client runs a fixed time step. Under load, MonoGame runs catch-up Updates and skips Draws, so the F11 FPS counter
// counted Updates and showed the target rate while the drawn rate fell. It now counts in EndFrame, once per Draw.
// The 20 ms step adds up exactly in a float, so each second rolls over on a known Update.
[Collection("DebugOverlay")]
public class DebugOverlayFpsTests
{
    private static readonly GameTime Step = new(TimeSpan.Zero, TimeSpan.FromMilliseconds(20));

    [Fact]
    public void FramesPerSecond_CountsDrawnFramesWhenUpdatesSkipDraws()
    {
        DebugOverlay.IsActive = true;
        DebugOverlay.ResetFrameCounters();

        try
        {
            //50 Updates and 25 Draws each second, for two seconds
            for (var i = 0; i < 100; i++)
            {
                DebugOverlay.BeginFrame();
                DebugOverlay.Update(Step);

                if ((i % 2) == 1)
                    DebugOverlay.EndFrame();
            }

            Assert.Equal(25, DebugOverlay.FramesPerSecond);
        } finally
        {
            DebugOverlay.IsActive = false;
        }
    }
}
