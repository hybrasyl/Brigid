#region
using Brigid.Controls.Components;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     A control is themed when <c>UseTheme</c> is true on it or on the nearest parent that sets it, so one switch on a
///     panel themes everything in it, and a child can still opt out.
/// </summary>
public class ThemeInheritanceTests
{
    [Fact]
    public void ThemingIsInheritedFromTheNearestParentThatSetsIt()
    {
        var root = new UIPanel();
        var middle = new UIPanel();
        var leaf = new UIPanel();
        root.AddChild(middle);
        middle.AddChild(leaf);

        Assert.False(leaf.IsThemed);

        root.UseTheme = true;
        Assert.True(middle.IsThemed);
        Assert.True(leaf.IsThemed);

        middle.UseTheme = false;
        Assert.False(leaf.IsThemed);

        leaf.UseTheme = true;
        Assert.True(leaf.IsThemed);
        Assert.False(middle.IsThemed);
    }
}
