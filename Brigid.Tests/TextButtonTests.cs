using Brigid.Controls.Generic;
using Brigid.Rendering;
using Xunit;

namespace Brigid.Tests;

// The lobby repeats the last clicked menu button on Enter through PerformClick, which a disabled button ignores.
[Collection("FontEngine")]
public class TextButtonTests
{
    public TextButtonTests() => FontEngine.Initialize(0);

    [Fact]
    public void PerformClick_RaisesClicked_OnlyWhenEnabled()
    {
        var button = new TextButton("OK", 60, 20);
        var clicks = 0;
        button.Clicked += () => clicks++;

        button.PerformClick();
        button.SetEnabled(false);
        button.PerformClick();

        Assert.Equal(1, clicks);
    }
}
