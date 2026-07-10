#region
using Brigid.Rendering;
using Brigid.Rendering.Markdown;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     Layout tests for <see cref="MarkdownLayoutEngine" /> using a deterministic fake measurer:
///     every character is 10px wide regardless of size/style, and line height is size + 5.
/// </summary>
public class MarkdownLayoutEngineTests
{
    private const int CHAR_WIDTH = 10;

    private sealed class FakeMeasurer : ITextMeasurer
    {
        public int MeasureWidth(string text, int size, FontStyle style = FontStyle.Regular) => text.Length * CHAR_WIDTH;

        public int GetLineHeight(int size, FontStyle style = FontStyle.Regular) => size + 5;
    }

    private static MarkdownLayout Layout(string markdown, int width = 1000, bool extractTitle = false)
        => MarkdownLayoutEngine.Layout(markdown, width, new FakeMeasurer(), extractTitle);

    [Fact]
    public void Paragraph_WrapsAtWidth()
    {
        //"aaaa bbbb cccc": each word 40px + 10px space; at width 100 two words fit per line
        var layout = Layout("aaaa bbbb cccc", width: 100);

        var lines = layout.Spans.Select(s => s.Y).Distinct().Count();
        Assert.Equal(2, lines);

        //second line starts back at x=0
        var lastLine = layout.Spans.MaxBy(s => s.Y);
        Assert.Equal(0, lastLine.X);
        Assert.Equal("cccc", lastLine.Text);
    }

    [Fact]
    public void Paragraph_MergesSameStyleWordsIntoOneSpan()
    {
        var layout = Layout("hello plain world");

        var span = Assert.Single(layout.Spans);
        Assert.Equal("hello plain world", span.Text);
        Assert.Equal(FontStyle.Regular, span.Style);
        Assert.Equal(MarkdownSpanKind.Body, span.Kind);
    }

    [Fact]
    public void Emphasis_NestsAndCombinesFlags()
    {
        var layout = Layout("plain **bold _both_** after");

        Assert.Contains(layout.Spans, s => (s.Text == "bold") && (s.Style == FontStyle.Bold));
        Assert.Contains(layout.Spans, s => (s.Text == "both") && (s.Style == FontStyle.BoldItalic));
        Assert.Contains(layout.Spans, s => s.Text.Contains("plain") && (s.Style == FontStyle.Regular));
    }

    [Fact]
    public void InlineCode_IsMonoWithBackground()
    {
        var layout = Layout("use `foo()` here");

        var code = Assert.Single(layout.Spans, s => s.Kind == MarkdownSpanKind.Code);
        Assert.Equal("foo()", code.Text);
        Assert.True(code.Style.HasFlag(FontStyle.Mono));

        var bg = Assert.Single(layout.CodeBackgrounds);
        Assert.True(bg.X < code.X);
        Assert.True(bg.Width > "foo()".Length * CHAR_WIDTH);
    }

    [Fact]
    public void Headings_UseLevelSizesAndBold()
    {
        var layout = Layout("# One\n\n## Two\n\n### Three\n\n#### Four");

        var sizes = layout.Spans.Where(s => s.Kind == MarkdownSpanKind.Heading).Select(s => s.Size).ToArray();
        Assert.Equal(4, sizes.Length);
        Assert.True(sizes[0] > sizes[1]);
        Assert.True(sizes[1] > sizes[2]);
        Assert.Equal(MarkdownLayoutEngine.BODY_SIZE, sizes[3]);
        Assert.All(layout.Spans, s => Assert.True(s.Style.HasFlag(FontStyle.Bold)));
    }

    [Fact]
    public void TitleExtraction_LiftsLeadingH1OutOfBody()
    {
        var layout = Layout("# The Title\n\nBody text.", extractTitle: true);

        Assert.Equal("The Title", layout.Title);
        Assert.DoesNotContain(layout.Spans, s => s.Text.Contains("Title"));
        Assert.Contains(layout.Spans, s => s.Text.Contains("Body"));
    }

    [Fact]
    public void TitleExtraction_IgnoresNonLeadingOrLowerHeadings()
    {
        var layout = Layout("Body first.\n\n# Late Heading", extractTitle: true);

        Assert.Null(layout.Title);
        Assert.Contains(layout.Spans, s => s.Text.Contains("Late"));
    }

    [Fact]
    public void OrderedList_NumbersFromStart()
    {
        var layout = Layout("3. third\n4. fourth");

        var markers = layout.Spans.Where(s => s.Kind == MarkdownSpanKind.ListMarker).Select(s => s.Text).ToArray();
        Assert.Equal(["3.", "4."], markers);
    }

    [Fact]
    public void UnorderedList_UsesBulletsAndIndentsContent()
    {
        var layout = Layout("- item one\n- item two");

        var markers = layout.Spans.Where(s => s.Kind == MarkdownSpanKind.ListMarker).ToArray();
        Assert.Equal(2, markers.Length);
        Assert.All(markers, m => Assert.Equal("•", m.Text));

        var content = layout.Spans.First(s => s.Text.Contains("item one"));
        Assert.True(content.X > markers[0].X);
        Assert.Equal(markers[0].Y, content.Y);
    }

    [Fact]
    public void ThematicBreak_EmitsRule()
    {
        var layout = Layout("above\n\n---\n\nbelow");

        var rule = Assert.Single(layout.Rules);
        var above = layout.Spans.First(s => s.Text == "above");
        var below = layout.Spans.First(s => s.Text == "below");
        Assert.True(rule.Y > above.Y);
        Assert.True(rule.Y < below.Y);
    }

    [Fact]
    public void FencedCodeBlock_IsVerbatimMonoWithBackground()
    {
        var layout = Layout("```\nvar x = **not bold**;\n```");

        var code = layout.Spans.Where(s => s.Kind == MarkdownSpanKind.Code).ToArray();
        var line = Assert.Single(code);
        Assert.Equal("var x = **not bold**;", line.Text);
        Assert.Equal(FontStyle.Mono, line.Style);

        var bg = Assert.Single(layout.CodeBackgrounds);
        Assert.True(bg.Height > 0);
        Assert.True(bg.Y <= line.Y);
        Assert.True(bg.Bottom >= line.Y);
    }

    [Fact]
    public void FencedCodeBlock_CharacterWrapsLongLines()
    {
        //40 chars = 400px, available = width - textX - pad; at width 200 the line must split
        var layout = Layout($"```\n{new string('x', 40)}\n```", width: 200);

        var code = layout.Spans.Where(s => s.Kind == MarkdownSpanKind.Code).ToArray();
        Assert.True(code.Length > 1);
        Assert.Equal(40, code.Sum(s => s.Text.Length));
    }

    [Fact]
    public void Link_RendersTextWithoutUrl()
    {
        var layout = Layout("see [the docs](https://example.invalid/x) now");

        Assert.Contains(layout.Spans, s => s.Text.Contains("the docs"));
        Assert.DoesNotContain(layout.Spans, s => s.Text.Contains("example.invalid"));
    }

    [Fact]
    public void QuoteBlock_DegradesToIndentedText()
    {
        var layout = Layout("> quoted words");

        var span = layout.Spans.First(s => s.Text.Contains("quoted"));
        Assert.True(span.X > 0);
    }

    [Fact]
    public void HardBreak_ForcesNewLine()
    {
        var layout = Layout("first\\\nsecond");

        var first = layout.Spans.First(s => s.Text == "first");
        var second = layout.Spans.First(s => s.Text == "second");
        Assert.True(second.Y > first.Y);
        Assert.Equal(first.X, second.X);
    }

    [Fact]
    public void EmptyDocument_ProducesEmptyLayout()
    {
        var layout = Layout("");

        Assert.Empty(layout.Spans);
        Assert.Equal(0, layout.ContentHeight);
    }

    [Fact]
    public void ContentHeight_CoversAllSpans()
    {
        var layout = Layout("# Head\n\npara one\n\npara two\n\n- a\n- b");

        Assert.True(layout.ContentHeight >= layout.Spans.Max(s => s.Y));
        Assert.True(layout.Spans.Count > 0);
    }

    [Fact]
    public void ContentHeight_CoversTrailingDecorations()
    {
        var rule = Layout("text\n\n---");
        Assert.True(rule.ContentHeight >= rule.Rules[0].Bottom);

        var code = Layout("text\n\n```\ncode\n```");
        Assert.True(code.ContentHeight >= code.CodeBackgrounds[0].Bottom);
    }

    [Fact]
    public void NestedList_IndentsDeeper()
    {
        var layout = Layout("- outer\n  - inner");

        var outer = layout.Spans.First(s => s.Text.Contains("outer"));
        var inner = layout.Spans.First(s => s.Text.Contains("inner"));
        Assert.True(inner.X > outer.X);
        Assert.True(inner.Y > outer.Y);
    }

    [Fact]
    public void UnbreakableWord_OverflowsWithoutLoopingOrDropping()
    {
        //one 20-char word (200px) at width 100: placed on its own line, overflowing right — never dropped
        var layout = Layout($"short {new string('w', 20)} after", width: 100);

        Assert.Contains(layout.Spans, s => s.Text == new string('w', 20));
        Assert.Contains(layout.Spans, s => s.Text == "after");
    }

    [Fact]
    public void DeeplyNestedInput_DegradesToPlainTextInsteadOfThrowing()
    {
        //Markdig's internal depth guard throws ArgumentException past ~128 nesting levels
        var hostile = string.Concat(Enumerable.Repeat("> ", 500)) + "deep";

        var layout = Layout(hostile);

        Assert.NotEmpty(layout.Spans);
        Assert.Contains(layout.Spans, s => s.Text.Contains("deep"));
    }

    [Fact]
    public void HugeSingleLineCodeBlock_LaysOutCompletely()
    {
        //regression pin for the O(n²) FitChars: 64KB on one line must lay out (fast) with no char lost
        const int LENGTH = 65536;
        var layout = Layout($"```\n{new string('x', LENGTH)}\n```", width: 500);

        Assert.Equal(LENGTH, layout.Spans.Where(s => s.Kind == MarkdownSpanKind.Code).Sum(s => s.Text.Length));
    }

    [Fact]
    public void HtmlEntities_TranscodeToVisibleText()
    {
        var layout = Layout("a &amp; b &#65; c");

        var text = string.Concat(layout.Spans.Select(s => s.Text));
        Assert.Contains("&", text);
        Assert.Contains("A", text);
    }

    [Fact]
    public void List_EmptyItemAdvancesY_NoMarkerOverlap()
    {
        //Markdig emits a childless ListItemBlock for a bare '-'; the empty item must still occupy a line
        //(BODY line height in the fake is 15 + 5 = 20px)
        var layout = Layout("- first\n-\n- third");

        var markers = layout.Spans
                            .Where(s => s.Kind == MarkdownSpanKind.ListMarker)
                            .OrderBy(s => s.Y)
                            .ToList();
        Assert.Equal(3, markers.Count);
        Assert.True(markers[1].Y - markers[0].Y >= 20, "marker overlaps the first item");
        Assert.True(markers[2].Y - markers[1].Y >= 20, "marker overlaps the empty item");

        //a trailing empty item's line must be inside ContentHeight (reachable at max scroll)
        var trailing = Layout("- a\n-");
        var lastMarker = trailing.Spans.Where(s => s.Kind == MarkdownSpanKind.ListMarker).MaxBy(s => s.Y);
        Assert.True(lastMarker.Y + 20 <= trailing.ContentHeight, "trailing empty item clipped past ContentHeight");
    }

    [Fact]
    public void OrderedList_WideMarkerPushesContentClear()
    {
        //marker "100." is 4 chars = 40px in the fake — wider than the fixed 18px indent
        var layout = Layout("100. item");

        var marker = layout.Spans.Single(s => s.Kind == MarkdownSpanKind.ListMarker);
        var content = layout.Spans.Single(s => s.Kind == MarkdownSpanKind.Body);

        Assert.Equal("100.", marker.Text);
        Assert.True(content.X >= marker.X + 40, $"content at x={content.X} overlaps the 40px marker");
    }

    [Fact]
    public void CodeBlock_WrapNeverSplitsSurrogatePairs()
    {
        //each UTF-16 unit is 10px in the fake, so a pair is 20px. Width 91 → 79px available: a per-unit
        //wrap would break after 7 units (mid-pair); whole-char advance must wrap on pair boundaries.
        var line = string.Concat(Enumerable.Repeat("😀", 40));
        var layout = Layout($"```\n{line}\n```", width: 91);

        var codeSpans = layout.Spans.Where(s => s.Kind == MarkdownSpanKind.Code).ToList();
        Assert.True(codeSpans.Count > 1, "expected the code line to wrap");

        foreach (var span in codeSpans)
        {
            Assert.False(char.IsLowSurrogate(span.Text[0]), "span starts with a lone low surrogate");
            Assert.False(char.IsHighSurrogate(span.Text[^1]), "span ends with a lone high surrogate");
        }

        Assert.Equal(line, string.Concat(codeSpans.Select(s => s.Text)));
    }
}
