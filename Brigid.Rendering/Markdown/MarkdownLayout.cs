#region
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Rendering.Markdown;

/// <summary>
///     Semantic role of a laid-out markdown span. The renderer maps kinds to colors; size and style already live on
///     the span itself.
/// </summary>
public enum MarkdownSpanKind
{
    Body,
    Heading,
    Code,
    ListMarker
}

/// <summary>One positioned, uniformly-styled run of text in document-local space (origin at the content top-left).</summary>
public readonly record struct MarkdownSpan(
    string Text,
    int X,
    int Y,
    int Size,
    FontStyle Style,
    MarkdownSpanKind Kind);

/// <summary>
///     A laid-out markdown document produced by <see cref="MarkdownLayoutEngine.Layout" />: positioned text spans plus
///     decoration rectangles, all in document-local space at a fixed wrap width. Rendering is a straight replay —
///     no measurement or markdown knowledge required.
/// </summary>
public sealed class MarkdownLayout
{
    /// <summary>Positioned text spans in document order.</summary>
    public required IReadOnlyList<MarkdownSpan> Spans { get; init; }

    /// <summary>Background rectangles behind fenced code blocks and inline code spans.</summary>
    public required IReadOnlyList<Rectangle> CodeBackgrounds { get; init; }

    /// <summary>Horizontal-rule rectangles (thematic breaks).</summary>
    public required IReadOnlyList<Rectangle> Rules { get; init; }

    /// <summary>Total laid-out height in px.</summary>
    public required int ContentHeight { get; init; }

    /// <summary>
    ///     Plain text of the document's leading level-1 heading when title extraction was requested (the heading is
    ///     then omitted from <see cref="Spans" />); null otherwise.
    /// </summary>
    public string? Title { get; init; }
}
