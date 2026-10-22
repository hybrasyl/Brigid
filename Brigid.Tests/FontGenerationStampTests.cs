#region
using Brigid.Controls.Components;
using Brigid.Rendering;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>A new stamp is changed, a mark makes it current, and a new face makes it changed again.</summary>
[Collection("FontEngine")]
public sealed class FontGenerationStampTests
{
    public FontGenerationStampTests() => FontEngine.Initialize(0);

    [Fact]
    public void TheStampFollowsTheFontGeneration()
    {
        var stamp = new FontGenerationStamp();

        Assert.True(stamp.Changed);

        stamp.Mark();

        Assert.False(stamp.Changed);

        FontEngine.Instance.CycleFont();

        Assert.True(stamp.Changed);
    }
}
