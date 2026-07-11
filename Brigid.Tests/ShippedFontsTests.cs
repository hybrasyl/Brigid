#region
using FontStashSharp;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     Smoke tests that every font file shipped in Brigid/Content/Fonts loads into FontStashSharp and produces
///     nonzero measurements for a representative sample — headless (measurement needs no GraphicsDevice), so a
///     corrupt or unparseable font (e.g. a color-table-only emoji font) fails here instead of at client startup.
/// </summary>
public class ShippedFontsTests
{
    private static string FontsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Brigid", "Content", "Fonts");

            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Brigid/Content/Fonts not found above the test bin directory");
    }

    [Theory]
    [InlineData("NotoSansMono-Regular.ttf", "The quick brown fox")]
    [InlineData("NotoSansMono-Bold.ttf", "The quick brown fox")]
    [InlineData("AnonymousPro-Regular.ttf", "The quick brown fox")]
    [InlineData("AnonymousPro-Bold.ttf", "The quick brown fox")]
    [InlineData("AnonymousPro-Italic.ttf", "The quick brown fox")]
    [InlineData("AnonymousPro-BoldItalic.ttf", "The quick brown fox")]
    [InlineData("IosevkaCharonMono-Regular.ttf", "The quick brown fox")]
    [InlineData("NotoEmoji-Regular.ttf", "😀🎉👍🔥")]
    public void ShippedFont_LoadsAndMeasuresSample(string file, string sample)
    {
        var system = new FontSystem();
        system.AddFont(File.ReadAllBytes(Path.Combine(FontsDir(), file)));

        var size = system.GetFont(15).MeasureString(sample);

        Assert.True(size.X > 0, $"{file} measured zero width for its sample");
    }
}
