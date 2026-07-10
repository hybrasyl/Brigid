#region
using Brigid.Controls.Components;
using Brigid.Rendering;
#endregion

namespace Brigid.Controls.Scrolling;

/// <summary>
///     Adapts a word-wrapped <see cref="UILabel" /> as a <b>line-granular</b> <see cref="IScrollSource" />: extent and
///     viewport are measured in <c>CHAR_HEIGHT</c> lines, and <see cref="ApplyOffset" /> converts a line offset back to
///     the label's pixel <see cref="UILabel.ScrollOffset" />. Line units (not pixels) so a bound bar's ±1 arrow scrolls
///     exactly one line. Keeps <see cref="UILabel" /> itself free of any scroll-infrastructure dependency.
/// </summary>
public sealed class LabelScrollSource(UILabel label) : IScrollSource
{
    public int ContentExtent => label.ContentHeight / TextRenderer.CHAR_HEIGHT;

    public int ViewportExtent => Math.Max(1, (label.Height - label.PaddingTop - label.PaddingBottom) / TextRenderer.CHAR_HEIGHT);

    public int Step => 1;

    public void ApplyOffset(int offset) => label.ScrollOffset = offset * TextRenderer.CHAR_HEIGHT;
}
