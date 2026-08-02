#region
using Brigid.Rendering;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     A line break must never land between the halves of a surrogate pair. Each half alone is a lone surrogate, and
///     <c>SanitizeSurrogates</c> replaces it with U+FFFD on the way to the draw — so an emoji that merely overflowed
///     the line came out as two replacement glyphs on two lines instead of wrapping intact.
///     <para>
///         This matters because <c>ChatPanel</c> keeps wrapping as a deliberate backstop for exactly this input: the
///         autofit budget covers the ASCII the DA protocol carries, but fallback faces (CJK, emoji) do not share the
///         monospace advance, so an unusual message is expected to overflow and break.
///     </para>
///     <para>
///         Two mechanisms, both reproduced before the fix. Pairs straddling an odd offset broke on the low surrogate
///         (a leading "abc" is what makes the offsets odd — with pairs aligned to even indices the breaks happened to
///         land on boundaries, which is why a naive probe reports no problem). And a width too narrow for even one
///         glyph hit the <c>Math.Max(1, i)</c> floor, which forced index 1 — inside the very first pair.
///     </para>
/// </summary>
[Collection("FontEngine")]
public class SurrogateWrapTests
{
    private const string EMOJI = "\U0001F600";

    private static void AssertNeverSplits(string text, int maxWidth)
    {
        var index = TextRenderer.FindLineBreak(text, maxWidth);

        Assert.InRange(index, 0, text.Length);

        //a break landing on a low surrogate means its high half went to the previous line
        if ((index > 0) && (index < text.Length))
            Assert.False(
                char.IsLowSurrogate(text[index]),
                $"break at {index} of \"{text.Length}\" chars (width {maxWidth}) split a surrogate pair");
    }

    [Theory]
    [InlineData(20)]
    [InlineData(26)]
    [InlineData(33)]
    [InlineData(40)]
    [InlineData(47)]
    public void PairsStraddlingAnOddOffset_AreNeverSplit(int maxWidth)
    {
        FontEngine.Initialize(0);

        AssertNeverSplits("abc" + string.Concat(Enumerable.Repeat(EMOJI, 10)), maxWidth);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void AWidthTooNarrowForOneGlyph_TakesTheWholePair_RatherThanHalf(int maxWidth)
    {
        FontEngine.Initialize(0);

        AssertNeverSplits(EMOJI + EMOJI, maxWidth);
    }

    [Fact]
    public void AlignedPairs_AreNeverSplit()
    {
        FontEngine.Initialize(0);

        var text = string.Concat(Enumerable.Repeat(EMOJI, 20));

        foreach (var width in new[] { 30, 50, 80, 130 })
            AssertNeverSplits(text, width);
    }

    /// <summary>
    ///     The fix must not have bought boundary-safety by refusing to break: an overflowing run still has to wrap.
    /// </summary>
    [Fact]
    public void OverflowingText_StillBreaks()
    {
        FontEngine.Initialize(0);

        var text = "abc" + string.Concat(Enumerable.Repeat(EMOJI, 10));

        Assert.True(
            TextRenderer.FindLineBreak(text, 33) < text.Length,
            "the wrapper took the whole run instead of breaking it");
    }
}
