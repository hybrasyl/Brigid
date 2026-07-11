namespace Brigid.Controls.Scrolling;

/// <summary>
///     Contract a scrollable content surface exposes so a <see cref="ScrollView" /> can drive it through a
///     <see cref="ScrollModel" />. Implemented by list views (extent = item count) and text surfaces (extent = line
///     count). All three metrics are expressed in the same unit; <see cref="ScrollView" /> reads them to size the bar
///     and calls <see cref="ApplyOffset" /> to push the resolved position back into the surface.
/// </summary>
public interface IScrollSource
{
    /// <summary>
    ///     Total content size in the source's own units (item count, or wrapped-line count).
    /// </summary>
    int ContentExtent { get; }

    /// <summary>
    ///     Visible size in the same units (visible rows, or visible lines).
    /// </summary>
    int ViewportExtent { get; }

    /// <summary>
    ///     Wheel / arrow granularity in units per notch — usually 1.
    /// </summary>
    int Step { get; }

    /// <summary>
    ///     Applies a resolved scroll offset to the surface and triggers whatever redraw/refresh it needs.
    /// </summary>
    void ApplyOffset(int offset);
}
