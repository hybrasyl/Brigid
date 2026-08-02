#region
using Brigid.Controls.Generic;
using Brigid.Controls.World.Hud;
using Brigid.Rendering;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     Guards the chat panel's no-wrap goal: the autofit size must make the worst realistic chat line fit the chat
///     area, for <em>every</em> selectable face. Faces differ by up to ~40% in advance width at the same pixel size, so
///     this is the check that a font addition/swap cannot quietly reintroduce wrapping.
/// </summary>
[Collection("FontEngine")]
public class ChatAutofitTests
{
    //ChattingRect is 432px wide in setoa.dat — identical in _nbk_s, _nbk_l and _nbk_l's expanded variant — less the
    //scrollbar gutter. The 432 is hardcoded because the real rect comes from game data the test suite has no access
    //to; if the shipped art ever changes, that number is the thing to revisit.
    private const int CHAT_WIDTH = 432 - ScrollBarControl.DEFAULT_WIDTH;

    //referenced, not re-declared: a local copy would keep this test green against the old numbers after the shipped
    //ones move, which is precisely the drift it exists to catch
    private const int BUDGET = ChatTextStyle.BudgetChars;
    private const float NATURAL = ChatTextStyle.Spacing;

    //how much of the available width a correctly-sized line must occupy. Advances are whole pixels, so the reachable
    //widths are multiples of BUDGET; the best available here is 414 of 416. Anything materially below means autofit
    //dropped a rung and is wasting a visible band down the right-hand side.
    private const double MIN_FILL = 0.95;

    [Fact]
    public void EveryFace_AutofitsTheWorstCaseChatLine()
    {
        FontEngine.Initialize(0);
        var faceCount = FontEngine.Instance.FontCount;

        Assert.True(faceCount > 0, "no faces loaded");

        var probe = new string('M', BUDGET);
        var failures = new List<string>();

        using var face = new ActiveFaceScope();

        for (var i = 0; i < faceCount; i++)
        {
            FontEngine.Instance.SetActiveFont(i);

            var size = FontEngine.Instance.LargestSizeFitting(BUDGET, CHAT_WIDTH, extraSpacing: NATURAL);
            var width = FontEngine.Instance.MeasureWidth(probe, size, FontStyle.Regular, NATURAL);

            if (width > CHAT_WIDTH)
                failures.Add($"face {i}: autofit chose {size}px, which measures {width}px > {CHAT_WIDTH}px");

            //the point of autofit: fill the line, don't leave a band of dead space
            if (((double)width / CHAT_WIDTH) < MIN_FILL)
                failures.Add(
                    $"face {i}: autofit chose {size}px using only {width}px of {CHAT_WIDTH}px "
                    + $"({100.0 * width / CHAT_WIDTH:0.0}%)");

            //bottoming out means even 8px could not fit it — the budget or the rect would have to be wrong
            if (size <= FontEngine.MIN_AUTOFIT_SIZE)
                failures.Add($"face {i}: autofit bottomed out at {size}px");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>
    ///     The wrapper accumulates width one character at a time while everything else measures whole runs. A single
    ///     character has no inter-character gap, so per-character measurement silently drops the tracking that both a
    ///     string measurement and the draw apply between glyphs — which made every line break ~1px per character early.
    ///     Autofit is meaningless if the two disagree: it would size text against a width the wrapper never uses.
    ///     <para>
    ///         The assertion is deliberately an <em>exact prefix</em> one, and it sweeps both spacings. Asserting
    ///         "at the full measured width, take the whole run" cannot catch a broken accumulation: the whole-string
    ///         fast path answers it without ever entering the loop. And sweeping only <see cref="NATURAL" /> cannot
    ///         catch it either, because the per-glyph term is <c>DEFAULT_TRACKING + extraSpacing</c>, which at natural
    ///         spacing is identically zero — the one value at which the thing under test is a no-op. Both of those
    ///         were true of the original version of this test, verified by mutation: disabling the tracking add-back
    ///         left it green.
    ///     </para>
    /// </summary>
    [Fact]
    public void WrapAccumulation_AgreesWithWholeStringMeasurement()
    {
        FontEngine.Initialize(0);

        var probe = new string('M', BUDGET);
        var failures = new List<string>();

        using var face = new ActiveFaceScope();

        for (var i = 0; i < FontEngine.Instance.FontCount; i++)
        {
            FontEngine.Instance.SetActiveFont(i);

            foreach (var size in new[] { FontEngine.RENDER_SIZE, 13, 11 })
                //NATURAL is what chat uses; 0 is the global default tracking every other wrap in the app runs at, and
                //is the only one of the two at which the per-glyph tracking term is non-zero
                foreach (var spacing in new[] { NATURAL, 0f })
                {
                    //an exact prefix: the width of k glyphs must admit exactly k glyphs and no more. Over-estimating
                    //the accumulation breaks early (returns < k), under-estimating runs long (> k) — both caught.
                    foreach (var k in new[] { BUDGET / 3, BUDGET / 2, BUDGET - 1 })
                    {
                        var prefixWidth = FontEngine.Instance.MeasureWidth(probe[..k], size, FontStyle.Regular, spacing);
                        var broke = TextRenderer.FindLineBreak(probe, prefixWidth, size: size, extraSpacing: spacing);

                        if (broke != k)
                            failures.Add(
                                $"face {i} size {size} spacing {spacing}: {k} glyphs measure {prefixWidth}px, "
                                + $"but the wrapper fit {broke} of them into exactly that width");
                    }

                    //and the whole run must still be taken at its own measured width
                    var whole = FontEngine.Instance.MeasureWidth(probe, size, FontStyle.Regular, spacing);

                    if (TextRenderer.FindLineBreak(probe, whole, size: size, extraSpacing: spacing) != BUDGET)
                        failures.Add($"face {i} size {size} spacing {spacing}: measured {whole}px but the wrapper still broke");
                }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    //the engine is a static singleton; leaving another face active leaks into every test sharing this collection
    private sealed class ActiveFaceScope : IDisposable
    {
        private readonly int Original = FontEngine.Instance.ActiveFontIndex;

        public void Dispose() => FontEngine.Instance.SetActiveFont(Original);
    }

    /// <summary>
    ///     Every text rect in the Dark Ages prefabs is exactly CHAR_HEIGHT tall — s_Str 10..22, s_EXP 62..74, SZ_ZONE
    ///     464..476, SystemMessage 316..328. So the size ordinary UI text draws at must ink no taller than that cell, or
    ///     glyphs clip against the box and descenders (commas, p, g) spill onto the surrounding art. This is per-face:
    ///     at RENDER_SIZE, Noto and Iosevka ink exactly 12 while Anonymous Pro inks 13 and Comic Shanns 16.
    /// </summary>
    [Fact]
    public void UiSize_InksWithinTheLayoutCell_ForEveryFace()
    {
        FontEngine.Initialize(0);

        var failures = new List<string>();

        using var face = new ActiveFaceScope();

        for (var i = 0; i < FontEngine.Instance.FontCount; i++)
        {
            FontEngine.Instance.SetActiveFont(i);

            var size = FontEngine.Instance.UiSize;
            var ink = FontEngine.Instance.InkHeight(size);

            if (ink > TextRenderer.CHAR_HEIGHT)
                failures.Add($"face {i}: UiSize {size} inks {ink}px, taller than the {TextRenderer.CHAR_HEIGHT}px cell");

            if (size > FontEngine.RENDER_SIZE)
                failures.Add($"face {i}: UiSize {size} exceeds RENDER_SIZE {FontEngine.RENDER_SIZE}");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Autofit_NeverExceedsTheDefaultRenderSize()
    {
        FontEngine.Initialize(0);

        //a very wide budget must not grow text: line height at RENDER_SIZE already exceeds the CHAR_HEIGHT line grid,
        //so autofit is deliberately shrink-only until the grid itself is variable.
        var size = FontEngine.Instance.LargestSizeFitting(1, 10_000);

        Assert.Equal(FontEngine.Instance.UiSize, size);
    }

    [Fact]
    public void Autofit_DegradesToTheFloor_RatherThanReturningNonsense()
    {
        FontEngine.Initialize(0);

        Assert.Equal(FontEngine.MIN_AUTOFIT_SIZE, FontEngine.Instance.LargestSizeFitting(500, 10));
        Assert.Equal(FontEngine.MIN_AUTOFIT_SIZE, FontEngine.Instance.LargestSizeFitting(0, 400));
        Assert.Equal(FontEngine.MIN_AUTOFIT_SIZE, FontEngine.Instance.LargestSizeFitting(69, 0));
    }
}
