using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Xunit;

namespace Brigid.Tests;

// UIPanel draws the debug boxes and names through its DrawOverChild hook, so that Controls/Components does not depend on
// the overlay in Controls/Generic (BRIG-137). The overlay sets the hook while it is on, and clears it when it goes off.
[Collection("DebugOverlay")]
public class DebugOverlayHookTests
{
    [Fact]
    public void TheOverlaySetsTheChildHookOnlyWhileItIsOn()
    {
        try
        {
            DebugOverlay.IsActive = true;
            Assert.NotNull(UIPanel.DrawOverChild);

            DebugOverlay.IsActive = false;
            Assert.Null(UIPanel.DrawOverChild);
        } finally
        {
            DebugOverlay.IsActive = false;
        }
    }
}
