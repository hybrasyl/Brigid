#region
using Brigid.Definitions;
using Brigid.Rendering;
using FontStashSharp;
using FontStashSharp.Rasterizers.StbTrueTypeSharp;
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
    [InlineData("ComicShannsMono-Regular.ttf", "The quick brown fox")]
    [InlineData("NotoEmoji-Regular.ttf", "😀🎉👍🔥")]
    public void ShippedFont_LoadsAndMeasuresSample(string file, string sample)
    {
        var system = new FontSystem();
        system.AddFont(File.ReadAllBytes(Path.Combine(FontsDir(), file)));

        var size = system.GetFont(15).MeasureString(sample);

        Assert.True(size.X > 0, $"{file} measured zero width for its sample");
    }

    /// <summary>
    ///     The padlock the connection line and the HUD's server box draw. No UI face covers it; it
    ///     resolves through the emoji fallback added to every FontSystem, and an uncovered codepoint
    ///     renders nothing at all rather than failing — so the absence would only ever be noticed on
    ///     screen, by someone who did not know a padlock was meant to be there.
    /// </summary>
    [Fact]
    public void EmojiFallback_CoversThePadlockGlyph()
    {
        //asked of the font's cmap rather than of MeasureString: an uncovered codepoint still measures a
        //.notdef advance (16px on this face), so a width tells you a string was measured and nothing
        //at all about whether the glyph exists.
        var loader = new StbTrueTypeSharpLoader(new StbTrueTypeSharpSettings());
        var source = loader.Load(File.ReadAllBytes(Path.Combine(FontsDir(), "NotoEmoji-Regular.ttf")));

        Assert.NotNull(source.GetGlyphId(char.ConvertToUtf32(Glyphs.PADLOCK, 0)));

        //control, so a pass means the lookup discriminates rather than answering yes to everything.
        Assert.Null(source.GetGlyphId(0xE000));
    }

    [Fact]
    public void IconFont_CoversResetGlyph()
    {
        //FontStyle.Icon exists because the UI faces lack the symbol glyphs, notably the keybind reset glyph ↺ (U+21BA).
        //Resolve the face the same way FontEngine does rather than naming a file, so moving IsIcon to a face without
        //the glyph fails here instead of silently blanking the reset button — the bug this face selection fixes.
        var iconFile = FontEngine.IconFaceFile;

        Assert.NotNull(iconFile);

        var system = new FontSystem();
        system.AddFont(File.ReadAllBytes(Path.Combine(FontsDir(), iconFile)));

        var width = system.GetFont(15).MeasureString("↺").X;

        Assert.True(width > 0, $"{iconFile} (the IsIcon face) does not cover U+21BA (the ↺ reset glyph)");
    }
}
