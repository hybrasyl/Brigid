#region
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Rendering.Markdown;

/// <summary>
///     Lays out a markdown document into positioned styled spans at a fixed wrap width, using an
///     <see cref="ITextMeasurer" /> for all width/height decisions (so layout is unit-testable without fonts).
///     Parsing is Markdig's; this walker supports headings, paragraphs, bold/italic emphasis, ordered/unordered
///     lists, thematic breaks, fenced/indented code blocks, and inline code. Everything else degrades to plain
///     text: links render their text (URLs dropped), block quotes render their children indented, and unknown
///     leaf blocks render their raw lines as body text.
/// </summary>
public static class MarkdownLayoutEngine
{
    /// <summary>Body text pixel size — matches the UI's standard render size.</summary>
    public const int BODY_SIZE = FontEngine.RENDER_SIZE;

    private const int H1_SIZE = 26;
    private const int H2_SIZE = 21;
    private const int H3_SIZE = 17;

    private const int LIST_INDENT = 18;
    private const int MARKER_GAP = 4;
    private const int QUOTE_INDENT = 12;
    private const int ITEM_GAP = 2;
    private const int CODE_PAD = 6;
    private const int INLINE_CODE_PAD = 2;
    private const int RULE_MARGIN = 6;
    private const int RULE_THICKNESS = 1;

    /// <summary>
    ///     Parses and lays out <paramref name="markdown" /> at <paramref name="width" /> px. When
    ///     <paramref name="extractTitle" /> is set and the document opens with a level-1 heading, that heading is
    ///     returned as <see cref="MarkdownLayout.Title" /> instead of being laid out.
    /// </summary>
    public static MarkdownLayout Layout(
        string markdown,
        int width,
        ITextMeasurer measurer,
        bool extractTitle = false)
    {
        markdown ??= string.Empty;
        var builder = new Builder(Math.Max(1, width), measurer);
        MarkdownDocument document;

        try
        {
            document = Markdig.Markdown.Parse(markdown);
        } catch (ArgumentException)
        {
            //Markdig's internal nesting-depth guard throws on pathologically nested input (~128+ levels of
            //quotes/lists). This engine renders server-sent text, which must never crash the client — degrade
            //to laying out the raw text verbatim instead.
            return builder.RunPlainText(markdown);
        }

        return builder.Run(document, extractTitle);
    }

    private static int GetHeadingSize(int level)
        => level switch
        {
            1 => H1_SIZE,
            2 => H2_SIZE,
            3 => H3_SIZE,
            _ => BODY_SIZE
        };

    private enum TokenKind
    {
        Word,
        Space,
        HardBreak
    }

    private readonly record struct Token(TokenKind Kind, string Text, FontStyle Style, bool IsCode);

    private sealed class Builder(int width, ITextMeasurer measurer)
    {
        private readonly List<Rectangle> CodeBackgrounds = [];
        private readonly Dictionary<char, int> MonoCharWidths = [];
        private readonly List<Rectangle> Rules = [];
        private readonly List<MarkdownSpan> Spans = [];
        private int Y;

        private int BlockGap => measurer.GetLineHeight(BODY_SIZE) / 2;

        public MarkdownLayout Run(MarkdownDocument document, bool extractTitle)
        {
            string? title = null;
            IEnumerable<Block> blocks = document;

            if (extractTitle && (document.Count > 0) && document[0] is HeadingBlock { Level: 1 } heading)
            {
                title = PlainText(heading);
                blocks = document.Skip(1);
            }

            LayoutBlocks(blocks, 0, BlockGap);

            return BuildResult(title);
        }

        /// <summary>Fallback for unparseable input: lays out the raw text verbatim as body lines.</summary>
        public MarkdownLayout RunPlainText(string text)
        {
            var tokens = new List<Token>();
            var first = true;

            foreach (var line in text.Split('\n'))
            {
                if (!first)
                    tokens.Add(new Token(TokenKind.HardBreak, string.Empty, FontStyle.Regular, false));

                first = false;
                AddWords(line, FontStyle.Regular, false, tokens);
            }

            LayoutTokens(tokens, BODY_SIZE, MarkdownSpanKind.Body, 0);

            return BuildResult(null);
        }

        private MarkdownLayout BuildResult(string? title)
            => new()
            {
                Spans = Spans,
                CodeBackgrounds = CodeBackgrounds,
                Rules = Rules,
                ContentHeight = Y,
                Title = title
            };

        /// <summary>Lays out sibling blocks, inserting <paramref name="gap" /> px between (not around) them.</summary>
        private void LayoutBlocks(IEnumerable<Block> blocks, int indent, int gap)
        {
            var first = true;

            foreach (var block in blocks)
            {
                if (!first)
                    Y += gap;

                first = false;
                LayoutBlock(block, indent);
            }
        }

        private void LayoutBlock(Block block, int indent)
        {
            switch (block)
            {
                case HeadingBlock h:
                    LayoutTokens(Tokenize(h, FontStyle.Bold), GetHeadingSize(h.Level), MarkdownSpanKind.Heading, indent);

                    break;

                case ParagraphBlock p:
                    LayoutTokens(Tokenize(p, FontStyle.Regular), BODY_SIZE, MarkdownSpanKind.Body, indent);

                    break;

                case ListBlock list:
                    LayoutList(list, indent);

                    break;

                case CodeBlock code:
                    LayoutCodeBlock(code, indent);

                    break;

                case ThematicBreakBlock:
                    Rules.Add(new Rectangle(indent, Y + RULE_MARGIN, width - indent, RULE_THICKNESS));
                    Y += RULE_MARGIN * 2 + RULE_THICKNESS;

                    break;

                case QuoteBlock quote:
                    LayoutBlocks(quote, indent + QUOTE_INDENT, BlockGap);

                    break;

                case ContainerBlock container:
                    LayoutBlocks(container, indent, BlockGap);

                    break;

                case LeafBlock leaf:
                    //unknown leaf (e.g. raw HTML block) — degrade to its raw lines as body text
                    LayoutTokens(Tokenize(leaf, FontStyle.Regular), BODY_SIZE, MarkdownSpanKind.Body, indent);

                    break;
            }
        }

        private void LayoutList(ListBlock list, int indent)
        {
            var ordered = list.IsOrdered;
            var number = ordered && int.TryParse(list.OrderedStart, out var start) ? start : 1;
            var first = true;

            foreach (var item in list)
            {
                if (item is not ListItemBlock listItem)
                    continue;

                if (!first)
                    Y += ITEM_GAP;

                first = false;

                var marker = ordered ? $"{number}." : "•";
                Spans.Add(new MarkdownSpan(marker, indent, Y, BODY_SIZE, FontStyle.Regular, MarkdownSpanKind.ListMarker));
                number++;

                //content clears the measured marker — multi-digit ordered markers ('10.', '999.') exceed the
                //fixed indent and would otherwise draw under the item's first words
                var contentIndent = indent + Math.Max(LIST_INDENT, measurer.MeasureWidth(marker, BODY_SIZE) + MARKER_GAP);

                //item content starts on the marker's line; nested blocks (incl. nested lists) indent further
                var itemTop = Y;
                LayoutBlocks(listItem, contentIndent, ITEM_GAP);

                //an empty item ('- ' alone — Markdig emits a childless ListItemBlock) lays out nothing, so
                //advance past the marker's own line: the next marker must not overlap it, and a trailing empty
                //item's marker must stay inside ContentHeight
                if (Y == itemTop)
                    Y += measurer.GetLineHeight(BODY_SIZE);
            }
        }

        private void LayoutCodeBlock(CodeBlock code, int indent)
        {
            var top = Y;
            Y += CODE_PAD;

            var textX = indent + CODE_PAD;
            var available = Math.Max(1, width - textX - CODE_PAD);
            var lineHeight = measurer.GetLineHeight(BODY_SIZE, FontStyle.Mono);

            for (var i = 0; i < code.Lines.Count; i++)
            {
                var line = code.Lines.Lines[i].Slice.ToString();

                if (line.Length == 0)
                {
                    Y += lineHeight;

                    continue;
                }

                //greedy character wrap for over-wide code lines (no word boundaries assumed); offset-based so
                //the shrinking remainder isn't re-allocated per wrapped line
                var pos = 0;

                while (pos < line.Length)
                {
                    var fit = FitChars(line, pos, available);
                    Spans.Add(new MarkdownSpan(line.Substring(pos, fit), textX, Y, BODY_SIZE, FontStyle.Mono, MarkdownSpanKind.Code));
                    Y += lineHeight;
                    pos += fit;
                }
            }

            Y += CODE_PAD;
            CodeBackgrounds.Add(new Rectangle(indent, top, width - indent, Y - top));
        }

        /// <summary>
        ///     Length of the longest prefix of <paramref name="text" /> starting at <paramref name="start" /> that
        ///     fits <paramref name="available" /> px (at least one char). Accumulates per-character widths — O(n)
        ///     even on a 64KB single-line code block, and exact for monospace faces (code is always
        ///     <see cref="FontStyle.Mono" />). On a proportional fallback face the sum ignores inter-glyph
        ///     tracking, which only ever overestimates — wrapping slightly early, never overflowing. Advances in
        ///     whole characters: a surrogate pair is measured and consumed as one unit, never split across a wrap
        ///     (a lone half would sanitize to U+FFFD and render as a replacement glyph).
        /// </summary>
        private int FitChars(string text, int start, int available)
        {
            var x = 0;
            var fit = 0;

            while (start + fit < text.Length)
            {
                var step = char.IsHighSurrogate(text[start + fit]) && (start + fit + 1 < text.Length) && char.IsLowSurrogate(text[start + fit + 1])
                    ? 2
                    : 1;

                var charWidth = MeasureMonoChar(text, start + fit, step);

                if ((fit > 0) && (x + charWidth > available))
                    break;

                x += charWidth;
                fit += step;
            }

            return fit;
        }

        //memoized per-character mono widths — repeated characters cost one dictionary hit instead of a
        //substring allocation plus a full measure pipeline (a 10KB code block is ~10k lookups, ~80 measures)
        private int MeasureMonoChar(string text, int index, int length)
        {
            if (length == 1)
            {
                var c = text[index];

                if (!MonoCharWidths.TryGetValue(c, out var width))
                {
                    width = measurer.MeasureWidth(text.Substring(index, 1), BODY_SIZE, FontStyle.Mono);
                    MonoCharWidths[c] = width;
                }

                return width;
            }

            //surrogate pair — rare enough that an uncached measure is fine
            return measurer.MeasureWidth(text.Substring(index, length), BODY_SIZE, FontStyle.Mono);
        }

        /// <summary>
        ///     Greedy word-wraps tokens into lines starting at <paramref name="indent" />, merging same-style
        ///     neighbors into single spans and recording inline-code background rects.
        /// </summary>
        private void LayoutTokens(List<Token> tokens, int size, MarkdownSpanKind kind, int indent)
        {
            var lineHeight = measurer.GetLineHeight(size);
            List<(int X, int Width, string Text, FontStyle Style, bool IsCode)> line = [];
            var x = indent;
            string? pendingSpace = null;
            FontStyle pendingSpaceStyle = default;

            foreach (var token in tokens)
                switch (token.Kind)
                {
                    case TokenKind.HardBreak:
                        FlushLine();

                        break;

                    case TokenKind.Space:
                        if (line.Count > 0)
                        {
                            pendingSpace = token.Text;
                            pendingSpaceStyle = token.Style;
                        }

                        break;

                    case TokenKind.Word:
                        var wordWidth = measurer.MeasureWidth(token.Text, size, token.Style);
                        var spaceWidth = pendingSpace is null ? 0 : measurer.MeasureWidth(pendingSpace, size, pendingSpaceStyle);

                        if ((line.Count > 0) && (x + spaceWidth + wordWidth > width))
                        {
                            FlushLine();
                            spaceWidth = 0;
                        }

                        //merge into the previous chunk when the style matches (fewer draw calls, natural kerning)
                        if ((pendingSpace is not null)
                            && (spaceWidth > 0)
                            && (line.Count > 0)
                            && (line[^1].Style == token.Style)
                            && (line[^1].IsCode == token.IsCode))
                        {
                            var last = line[^1];
                            line[^1] = (last.X, last.Width + spaceWidth + wordWidth, last.Text + pendingSpace + token.Text, last.Style, last.IsCode);
                        } else
                        {
                            line.Add((x + spaceWidth, wordWidth, token.Text, token.Style, token.IsCode));
                        }

                        x += spaceWidth + wordWidth;
                        pendingSpace = null;

                        break;
                }

            FlushLine();

            return;

            void FlushLine()
            {
                foreach (var chunk in line)
                {
                    var chunkKind = chunk.IsCode ? MarkdownSpanKind.Code : kind;
                    Spans.Add(new MarkdownSpan(chunk.Text, chunk.X, Y, size, chunk.Style, chunkKind));

                    if (chunk.IsCode)
                        CodeBackgrounds.Add(
                            new Rectangle(
                                chunk.X - INLINE_CODE_PAD,
                                Y,
                                chunk.Width + INLINE_CODE_PAD * 2,
                                lineHeight));
                }

                if (line.Count > 0)
                    Y += lineHeight;

                line.Clear();
                x = indent;
                pendingSpace = null;
            }
        }

        /// <summary>Flattens a leaf block's inline tree into word/space/break tokens with combined style flags.</summary>
        private static List<Token> Tokenize(LeafBlock block, FontStyle baseStyle)
        {
            var tokens = new List<Token>();

            if (block.Inline is not null)
                FlattenInlines(block.Inline, baseStyle, tokens);
            else
                AddRawLines(block, baseStyle, tokens);

            return tokens;
        }

        private static void AddRawLines(LeafBlock block, FontStyle style, List<Token> tokens)
        {
            for (var i = 0; i < block.Lines.Count; i++)
            {
                if (i > 0)
                    tokens.Add(new Token(TokenKind.HardBreak, string.Empty, style, false));

                AddWords(block.Lines.Lines[i].Slice.ToString(), style, false, tokens);
            }
        }

        private static void FlattenInlines(ContainerInline container, FontStyle style, List<Token> tokens)
        {
            foreach (var child in container)
                switch (child)
                {
                    case LiteralInline literal:
                        AddWords(literal.Content.ToString(), style, false, tokens);

                        break;

                    case CodeInline code:
                        AddWords(code.Content, style | FontStyle.Mono, true, tokens);

                        break;

                    case EmphasisInline emphasis:
                        FlattenInlines(emphasis, style | (emphasis.DelimiterCount >= 2 ? FontStyle.Bold : FontStyle.Italic), tokens);

                        break;

                    case LineBreakInline lineBreak:
                        tokens.Add(
                            lineBreak.IsHard
                                ? new Token(TokenKind.HardBreak, string.Empty, style, false)
                                : new Token(TokenKind.Space, " ", style, false));

                        break;

                    case AutolinkInline autolink:
                        AddWords(autolink.Url, style, false, tokens);

                        break;

                    case HtmlEntityInline entity:
                        //&amp; / &copy; / &#65; etc. are user-visible characters, not markup — keep the transcoded text
                        AddWords(entity.Transcoded.ToString(), style, false, tokens);

                        break;

                    //LinkInline and any other container (strikethrough etc. if ever enabled): render inner text
                    case ContainerInline inner:
                        FlattenInlines(inner, style, tokens);

                        break;

                    //HtmlInline, HtmlEntityInline and friends: drop markup, keep nothing
                }
        }

        /// <summary>Splits text into word and single-space tokens (whitespace runs collapse to one space).</summary>
        private static void AddWords(string text, FontStyle style, bool isCode, List<Token> tokens)
        {
            var i = 0;

            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    tokens.Add(new Token(TokenKind.Space, " ", style, isCode));

                    while ((i < text.Length) && char.IsWhiteSpace(text[i]))
                        i++;

                    continue;
                }

                var start = i;

                while ((i < text.Length) && !char.IsWhiteSpace(text[i]))
                    i++;

                tokens.Add(new Token(TokenKind.Word, text[start..i], style, isCode));
            }
        }

        /// <summary>Plain text of a leaf block's inline tree (for title extraction).</summary>
        private static string PlainText(LeafBlock block)
        {
            var tokens = Tokenize(block, FontStyle.Regular);

            return string.Concat(tokens.Select(t => t.Kind == TokenKind.HardBreak ? " " : t.Text)).Trim();
        }
    }
}
