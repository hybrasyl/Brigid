using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Xunit;

namespace Brigid.Tests;

// The draw of a built-in icon made a closure for its cache lookup at the start of the method, also for a role with no
// image, which is most roles. The new HUD draws 17 to 19 icons each frame. The lookup now uses a static factory.
public sealed class BuiltInArtAllocationTests
{
    [Fact]
    public void ARoleWithNoImageAllocatesNothing()
    {
        var bounds = new Rectangle(0, 0, 16, 16);

        //the first call can load the images
        BuiltInArt.TryDraw(null!, "icon.no_such_role", bounds, bounds);

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 100; i++)
            Assert.False(BuiltInArt.TryDraw(null!, "icon.no_such_role", bounds, bounds));

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
