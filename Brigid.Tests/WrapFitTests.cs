#region
using Brigid.Rendering;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     Wrapped lines must fit the width they were wrapped to, at every window scale, in every shipped face.
/// </summary>
/// <remarks>
///     The regression is the 2x window (1280x960): FindLineBreak summed per-glyph widths that were each rounded to a
///     whole virtual pixel, and off 1x a glyph's advance divides to a half pixel that rounds to even. The sum came out
///     up to 23px short of the line measured whole, so board text ran under the scrollbar and past the window edge.
/// </remarks>
[Collection("FontEngine")]
public class WrapFitTests
{
    private const string BODY =
        "Welcome to the board. This is a longer post body with ordinary words, some punctuation, numbers like 1234 and "
        + "names such as Mileth, Suomi and Rucesion, written to wrap across many lines of a board window.";

    public static TheoryData<float> Scales => [1f, 1.5f, 2f, 3f];

    [Theory]
    [MemberData(nameof(Scales))]
    public void WrappedLines_FitTheWidth_AndAreNotBrokenEarly(float scale)
    {
        FontEngine.Initialize(0);
        var engine = FontEngine.Instance;
        var originalFace = engine.ActiveFontIndex;

        try
        {
            engine.SetLayoutScale(scale, scale);

            for (var face = 0; face < engine.FontCount; face++)
            {
                engine.SetActiveFont(face);

                foreach (var width in new[] { 200, 260, 330, 400 })
                {
                    var lines = TextRenderer.WrapText(BODY, width);

                    for (var i = 0; i < lines.Count; i++)
                    {
                        var line = lines[i].TrimEnd();
                        var drawn = TextRenderer.MeasureWidth(line);

                        Assert.True(drawn <= width, $"face {face} at {scale}x, width {width}: \"{line}\" draws {drawn}px");

                        if (i + 1 >= lines.Count)
                            continue;

                        var nextWord = lines[i + 1].TrimStart().Split(' ')[0];
                        var withNext = TextRenderer.MeasureWidth($"{line} {nextWord}");

                        Assert.True(withNext > width, $"face {face} at {scale}x, width {width}: \"{line}\" had room for \"{nextWord}\"");
                    }
                }
            }
        } finally
        {
            engine.SetLayoutScale(1f, 1f);
            engine.SetActiveFont(originalFace);
        }
    }
}
