namespace Brigid.Rendering;

/// <summary>
///     The measurement surface text layout needs, abstracted from <see cref="FontEngine" /> so layout logic (markdown
///     wrapping in particular) is unit-testable without fonts or a graphics device. <see cref="FontEngine" /> is the
///     production implementation.
/// </summary>
public interface ITextMeasurer
{
    /// <summary>Pixel width of <paramref name="text" /> at an explicit pixel size and style (single line).</summary>
    int MeasureWidth(string text, int size, FontStyle style = FontStyle.Regular);

    /// <summary>The line height in virtual px of <paramref name="style" /> at pixel size <paramref name="size" />.</summary>
    int GetLineHeight(int size, FontStyle style = FontStyle.Regular);
}
