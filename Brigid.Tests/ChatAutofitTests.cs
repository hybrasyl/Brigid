#region
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
    //scrollbar gutter. Hardcoded because the real rect comes from game data the test suite has no access to; if the
    //shipped art ever changes, this number is the thing to revisit.
    private const int CHAT_WIDTH = 432 - ScrollBarWidth;
    private const int ScrollBarWidth = 16;

    //server sends "{name}: {message}" (whispers use {name}" / {name}>); names are 4-12 chars, message caps at 55
    private const int BUDGET = 12 + 2 + 55;

    //chat cancels the global negative tracking and sizes against the faces' natural advances — see ChatPanel
    private const float NATURAL = -FontEngine.DEFAULT_TRACKING;

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
    /// </summary>
    [Fact]
    public void WrapAccumulation_AgreesWithWholeStringMeasurement()
    {
        FontEngine.Initialize(0);

        var probe = new string('M', BUDGET);
        var failures = new List<string>();

        for (var i = 0; i < FontEngine.Instance.FontCount; i++)
        {
            FontEngine.Instance.SetActiveFont(i);

            foreach (var size in new[] { FontEngine.RENDER_SIZE, 13, 11 })
            {
                var whole = FontEngine.Instance.MeasureWidth(probe, size, FontStyle.Regular, NATURAL);

                //given exactly the measured width, the wrapper must take the whole run
                var atExactWidth = TextRenderer.FindLineBreak(probe, whole, size: size, extraSpacing: NATURAL);

                if (atExactWidth != BUDGET)
                    failures.Add($"face {i} size {size}: measured {whole}px but the wrapper broke at {atExactWidth}/{BUDGET}");

                //and one pixel under, it must break — otherwise it is not measuring at all
                if (TextRenderer.FindLineBreak(probe, whole - 1, size: size, extraSpacing: NATURAL) >= BUDGET)
                    failures.Add($"face {i} size {size}: wrapper did not break below the measured width");
            }
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

        Assert.Equal(FontEngine.RENDER_SIZE, size);
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
