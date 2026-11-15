using Brigid.Data;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     BRIG-111: one rule for a file name that Brigid makes from data it does not control. <see
///     cref="FileNames.IsSafeName" /> refuses a server-sent metafile name that is not the same bare file on every
///     platform, and <see cref="FileNames.ToSafeName" /> makes a profile folder name from a character name with the
///     same rule.
/// </summary>
public sealed class FileNamesTests
{
    [Theory]
    [InlineData("ItemInfo0")]
    [InlineData("SClass1")]
    [InlineData("SEvent12")]
    [InlineData("NationDesc")]
    [InlineData("Light")]
    [InlineData("a..b")]
    [InlineData("CONSOLE")]
    [InlineData("COM10")]
    [InlineData("COM⁴")]
    public void IsSafeName_AcceptsBareFileNames(string name) => Assert.True(FileNames.IsSafeName(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]
    [InlineData(@"..\x.bat")]
    [InlineData("../x.bat")]
    [InlineData(@"sub\x")]
    [InlineData("sub/x")]
    [InlineData(@"C:\Users\u\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\x.bat")]
    [InlineData("/home/u/.bashrc")]
    [InlineData(@"\\host\share\x")]
    [InlineData("x\0y")]
    [InlineData("a:b")]
    [InlineData("a|b")]
    [InlineData("Light.")]
    [InlineData("Light ")]
    [InlineData("NUL")]
    [InlineData("con.txt")]
    [InlineData("Com1")]
    [InlineData("COM0")]
    [InlineData("LPT0.txt")]
    [InlineData("COM¹")]
    [InlineData("com².dat")]
    [InlineData("LPT³")]
    [InlineData("lpt¹.txt")]
    [InlineData("CON .txt")]
    [InlineData("nul  .dat")]
    public void IsSafeName_RejectsAnythingThatIsNotTheSameBareFileOnEveryPlatform(string? name)
        => Assert.False(FileNames.IsSafeName(name));

    [Theory]
    [InlineData("Angelique", "Angelique")]
    [InlineData("  Sabrael  ", "Sabrael")]
    [InlineData(" x", "x")]
    [InlineData("a:b|c", "abc")]
    [InlineData(@"..\x", "..x")]
    [InlineData("../x", "..x")]
    [InlineData("name.", "name")]
    [InlineData("name. . ", "name")]
    [InlineData("x\0y", "xy")]
    [InlineData("CON", "_CON")]
    [InlineData("com1.txt", "_com1.txt")]
    [InlineData(null, "_")]
    [InlineData("", "_")]
    [InlineData("   ", "_")]
    [InlineData("..", "_")]
    [InlineData(":/?", "_")]
    public void ToSafeName_MakesASafeName_AndKeepsOneThatIsSafe(string? name, string expected)
    {
        var safe = FileNames.ToSafeName(name);

        Assert.Equal(expected, safe);
        Assert.True(FileNames.IsSafeName(safe));
        Assert.Equal(safe, FileNames.ToSafeName(safe));
    }
}
