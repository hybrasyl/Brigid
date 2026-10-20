using Brigid.Utilities;
using Xunit;

namespace Brigid.Tests;

// The lobby homepage URL comes from the game server and goes to Process.Start with UseShellExecute, which runs any
// string it is given. Only an absolute http or https URL may open.
public class BrowserTests
{
    [Theory]
    [InlineData("http://www.hybrasyl.com", "http://www.hybrasyl.com/")]
    [InlineData("https://www.darkages.com/news?id=1", "https://www.darkages.com/news?id=1")]
    [InlineData("  https://example.org  ", "https://example.org/")]
    public void TryGetWebUri_AcceptsHttpAndHttps(string url, string expected)
    {
        Assert.True(Browser.TryGetWebUri(url, out var uri));
        Assert.Equal(expected, uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("www.hybrasyl.com")]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData("calc.exe")]
    [InlineData(@"\\host\share\x.exe")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("/usr/bin/xcalc")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:privacy")]
    [InlineData("mailto:a@b.c")]
    [InlineData("ftp://example.org/x")]
    public void TryGetWebUri_RefusesEverythingElse(string? url)
    {
        Assert.False(Browser.TryGetWebUri(url, out var uri));
        Assert.Null(uri);
    }
}
