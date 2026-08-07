#region
using Brigid.Data.Models;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     Covers <see cref="ControlPrefab.ResolveAnimationSpan" />, which expands the endpoints an animated control's
///     <c>&lt;IMAGE&gt;</c> list declares into the frame span the panel has to render.
/// </summary>
public class ControlAnimationSpanTests
{
    [Fact]
    public void ResolveAnimationSpan_ExpandsTheWholeLoop_ForTheStartScreenLogo()
    {
        //_nstart.txt LOGO, verbatim: the flame ring is 20 frames and the layout names only its ends.
        (string, int)[] images = [("_nslogo.spf", 0), ("_nslogo.spf", 19)];

        var span = ControlPrefab.ResolveAnimationSpan(images);

        Assert.Equal(("_nslogo.spf", 0, 20), span);
    }

    [Fact]
    public void ResolveAnimationSpan_StartsAtTheFirstEntry_WhenTheSpanIsOffsetFromZero()
    {
        (string, int)[] images = [("_ncarrow.spf", 1), ("_ncarrow.spf", 3)];

        var span = ControlPrefab.ResolveAnimationSpan(images);

        Assert.Equal(("_ncarrow.spf", 1, 3), span);
    }

    [Fact]
    public void ResolveAnimationSpan_YieldsASingleFrame_ForAOneEntryList()
    {
        (string, int)[] images = [("_nstart.spf", 0)];

        var span = ControlPrefab.ResolveAnimationSpan(images);

        Assert.Equal(("_nstart.spf", 0, 1), span);
    }

    [Fact]
    public void ResolveAnimationSpan_YieldsTheFirstEntryAlone_WhenTheEntriesDoNotAscend()
    {
        (string, int)[] images = [("dlgcre04.epf", 1), ("dlgcre04.epf", 0)];

        var span = ControlPrefab.ResolveAnimationSpan(images);

        Assert.Equal(("dlgcre04.epf", 1, 1), span);
    }

    [Fact]
    public void ResolveAnimationSpan_YieldsNoFrames_ForAControlWithNoImages()
    {
        var span = ControlPrefab.ResolveAnimationSpan(null);

        Assert.Equal((string.Empty, 0, 0), span);
    }
}
