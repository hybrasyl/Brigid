using Brigid.Rendering;
using Xunit;

namespace Brigid.Tests;

// The font tests share the process-global static FontEngine/FontStashSharp FontSystem, which is
// single-threaded by design (the game drives it from the draw thread only). Serialize them into
// one non-parallel collection so concurrent test classes don't race the shared instance.
[CollectionDefinition("FontEngine", DisableParallelization = true)]
public sealed class FontEngineCollection;

[Collection("FontEngine")]

// Regression: the emoji-picker crash. The text measure/layout path must never throw on
// astral codepoints (full surrogate pair) nor on a LONE surrogate — the latter is exactly
// what InputBuffer.HandleTextInputEvent produces while typing an emoji, since it splits
// UTF-8 text per `char`, so the buffer transiently holds an unpaired surrogate. FontStashSharp
// calls Char.ConvertToUtf32 internally and throws on unpaired surrogates; FontEngine sanitizes
// them first. These tests pin that the measure/wrap paths return without throwing.
public class SurrogateReproTests
{
    private const string Emoji = "🙃";   // 🙃 U+1F643, a UTF-16 surrogate pair
    private const string LoneHigh = "\uD83D";      // unpaired high surrogate (per-char split)
    private const string LoneLow = "\uDE43";       // unpaired low surrogate

    [Fact]
    public void FontEngine_MeasureWidth_FullEmoji()
    {
        FontEngine.Initialize(0);
        var w = FontEngine.Instance.MeasureWidth(Emoji);
        Assert.True(w >= 0);
    }

    [Fact]
    public void FontEngine_MeasureWidth_LoneSurrogates()
    {
        FontEngine.Initialize(0);
        Assert.True(FontEngine.Instance.MeasureWidth(LoneHigh) >= 0);
        Assert.True(FontEngine.Instance.MeasureWidth(LoneLow) >= 0);
    }

    [Fact]
    public void TextRenderer_MeasureCharWidth_LoneSurrogate()
    {
        FontEngine.Initialize(0);
        // single lone surrogate char — what MeasureCharWidth receives during hit-test/wrap
        Assert.True(TextRenderer.MeasureCharWidth('\uD83D') >= 0);
    }

    [Fact]
    public void TextRenderer_MeasureWidth_And_Wrap_Emoji()
    {
        FontEngine.Initialize(0);
        Assert.True(TextRenderer.MeasureWidth(Emoji) >= 0);
        Assert.True(TextRenderer.MeasureWidth("hi " + Emoji + " there") >= 0);
        _ = TextRenderer.FindLineBreak(Emoji, 40);
        _ = TextRenderer.WrapText("hi " + Emoji + " there and more text to force a wrap somewhere", 40);
    }
}
