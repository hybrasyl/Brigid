namespace Brigid.Controls.Scrolling;

/// <summary>
///     Pure scroll math shared by every scrollable surface: clamps an offset between 0 and <see cref="Max" />, applies
///     wheel / arrow / page steps, and raises <see cref="Changed" /> <b>only when the offset actually moves</b>. This is
///     the single home for the <c>Math.Clamp(offset ± delta, 0, max)</c> logic that was copy-pasted across ~12
///     <c>OnMouseScroll</c> overrides.
///     <para>
///         Granularity-agnostic: a "unit" is a list item for list views, or a text line (<c>CHAR_HEIGHT</c>) for text
///         surfaces. Owns no rendering and is not a <c>UIElement</c> — a host (<see cref="ScrollView" />) binds it to a
///         <c>ScrollBarControl</c> and an <see cref="IScrollSource" />. When a bar is attached, prefer coarse (item /
///         line) units so the bar's ±1 arrow step maps to one item / line.
///     </para>
/// </summary>
public sealed class ScrollModel
{
    /// <summary>
    ///     Total content size, in units. Never negative.
    /// </summary>
    public int Extent { get; private set; }

    /// <summary>
    ///     Wheel / arrow granularity in units per notch. Usually 1 (one item / line); set higher for pixel-unit
    ///     sources that still want a line-sized wheel.
    /// </summary>
    public int Step { get; set; } = 1;

    /// <summary>
    ///     Bottom-anchored mapping (chat / system messages): flips the wheel and page directions so a "scroll up"
    ///     gesture moves toward higher offsets. Kept on the model — rather than only in the host's bar mapping — so
    ///     that <b>every</b> input path (content wheel, bar-strip wheel, paging) agrees on direction under inversion.
    /// </summary>
    public bool Inverted { get; set; }

    /// <summary>
    ///     Visible size, in units. Never negative.
    /// </summary>
    public int Viewport { get; private set; }

    /// <summary>
    ///     Current scroll position, clamped to <c>[0, Max]</c>. Mutated only through the Scroll/Set methods so
    ///     <see cref="Changed" /> stays authoritative.
    /// </summary>
    public int Offset { get; private set; }

    /// <summary>
    ///     Largest valid <see cref="Offset" /> — 0 when the content fits.
    /// </summary>
    public int Max => Math.Max(0, Extent - Viewport);

    /// <summary>
    ///     True when the content is taller/wider than the viewport and can move.
    /// </summary>
    public bool CanScroll => Extent > Viewport;

    private int PageStep => Math.Max(1, Viewport - 1);

    /// <summary>
    ///     Raised with the new <see cref="Offset" /> whenever it changes. Not raised for no-op moves.
    /// </summary>
    public event ScrollOffsetChangedHandler? Changed;

    /// <summary>
    ///     Updates the content/viewport metrics (e.g. after the item list or the visible-row count changes) and
    ///     re-clamps <see cref="Offset" /> into the new range. Returns true if the offset was pulled in as a result.
    /// </summary>
    public bool SetMetrics(int extent, int viewport)
    {
        Extent = Math.Max(0, extent);
        Viewport = Math.Max(0, viewport);

        return Reclamp();
    }

    /// <summary>
    ///     Pulls <see cref="Step" /> and the metrics straight off a source in one call — the single bind path shared by
    ///     <see cref="ScrollView" /> and bare-model consumers, so no site forgets to copy <see cref="Step" />.
    /// </summary>
    public bool Configure(IScrollSource source)
    {
        Step = source.Step;

        return SetMetrics(source.ContentExtent, source.ViewportExtent);
    }

    /// <summary>
    ///     Jumps to the start of the content (offset 0). Returns true if it moved.
    /// </summary>
    public bool ScrollToStart() => ScrollTo(0);

    /// <summary>
    ///     Jumps to the end of the content (offset <see cref="Max" />). Returns true if it moved.
    /// </summary>
    public bool ScrollToEnd() => ScrollTo(Max);

    /// <summary>
    ///     Moves to an absolute offset, clamped to <c>[0, Max]</c>. Returns true if the offset moved.
    /// </summary>
    public bool ScrollTo(int offset)
    {
        var clamped = Math.Clamp(offset, 0, Max);

        if (clamped == Offset)
            return false;

        Offset = clamped;
        Changed?.Invoke(Offset);

        return true;
    }

    /// <summary>
    ///     Moves by a signed number of units. Returns true if the offset moved.
    /// </summary>
    public bool ScrollBy(int deltaUnits) => ScrollTo(Offset + deltaUnits);

    /// <summary>
    ///     Applies a mouse-wheel notch. Positive <paramref name="wheelDelta" /> scrolls toward the start (matching the
    ///     legacy <c>Value - e.Delta</c> convention), multiplied by <see cref="Step" />; <see cref="Inverted" /> flips
    ///     the direction. Returns true if it moved.
    /// </summary>
    public bool WheelBy(int wheelDelta) => ScrollTo(Offset + ((Inverted ? 1 : -1) * wheelDelta * Step));

    /// <summary>
    ///     Pages by roughly one viewport in the given direction (-1 = toward start, +1 = toward end); <see cref="Inverted" />
    ///     flips it. Returns true if the offset moved.
    /// </summary>
    public bool PageBy(int direction) => ScrollTo(Offset + ((Inverted ? -1 : 1) * direction * PageStep));

    private bool Reclamp()
    {
        var clamped = Math.Clamp(Offset, 0, Max);

        if (clamped == Offset)
            return false;

        Offset = clamped;
        Changed?.Invoke(Offset);

        return true;
    }
}
