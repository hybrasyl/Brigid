#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
#endregion

namespace Brigid.Controls.Scrolling;

/// <summary>
///     Composite scroll host: a panel that owns a <see cref="ScrollModel" /> and an attached
///     <see cref="ScrollBarControl" />, laid out along the right edge (vertical) or bottom edge (horizontal). It binds
///     the two together and — the point of the abstraction — <b>handles the mouse wheel over its whole content area</b>
///     (events bubble up from content children), so consumers no longer copy-paste an <c>OnMouseScroll</c> clamp per
///     panel. Scrollable content is added as normal children via <c>AddChild</c>; the bound <see cref="IScrollSource" />
///     supplies the metrics and receives the resolved offset.
///     <para>
///         The <see cref="ScrollModel" />↔bar binding lives here, not in <see cref="ScrollBarControl" />, keeping the
///         widget a dumb, decoupled control. A re-entrancy guard prevents the bar→model→bar sync from looping.
///     </para>
/// </summary>
public sealed class ScrollView : UIPanel
{
    /// <summary>
    ///     The shared scroll math. Public so hosts can page/scroll programmatically or read <see cref="ScrollModel.Offset" />.
    /// </summary>
    public ScrollModel Model { get; } = new();

    /// <summary>
    ///     The attached bar widget, auto-created and laid out. Public for fine positioning/visibility tweaks by hosts.
    /// </summary>
    public ScrollBarControl Bar { get; }

    /// <summary>
    ///     When true, the bar's <c>Value</c> is the mirror of the model offset (thumb at the far end when offset is 0)
    ///     and the model's wheel/page directions flip. Used by bottom-anchored surfaces like chat and system messages.
    ///     Backed by <see cref="ScrollModel.Inverted" /> so the bar mapping and every input path share one flag. Set
    ///     before <see cref="SetSource" /> so the initial thumb position is correct.
    /// </summary>
    public bool InvertBar
    {
        get => Model.Inverted;
        set => Model.Inverted = value;
    }

    private readonly ScrollBarBinder Binder;
    private readonly ScrollOrientation Orientation;
    private IScrollSource? Source;

    public ScrollView(ScrollOrientation orientation = ScrollOrientation.Vertical)
    {
        Orientation = orientation;

        Bar = new ScrollBarControl
        {
            Name = "ScrollBar",
            Orientation = orientation,
            //always draw on top so content children (added after the bar) can't overpaint the strip
            ZIndex = int.MaxValue
        };

        Binder = new ScrollBarBinder(Model, Bar);
        Model.Changed += OnOffsetChanged;

        AddChild(Bar);
    }

    /// <summary>
    ///     Content width available to child surfaces — the panel width minus the vertical bar strip, or the full width
    ///     when the bar is hidden (e.g. a non-scrolling style).
    /// </summary>
    public int ContentWidth => (Orientation == ScrollOrientation.Vertical) && Bar.Visible ? Width - ScrollBarControl.DEFAULT_WIDTH : Width;

    /// <summary>
    ///     Content height available to child surfaces — the panel height minus the horizontal bar strip, or the full
    ///     height when the bar is hidden.
    /// </summary>
    public int ContentHeight => (Orientation == ScrollOrientation.Horizontal) && Bar.Visible ? Height - ScrollBarControl.DEFAULT_WIDTH : Height;

    /// <summary>
    ///     Binds the scrollable surface and does an initial <see cref="Sync" />. Call once after construction.
    /// </summary>
    public void SetSource(IScrollSource source)
    {
        Source = source;
        Sync();
    }

    /// <summary>
    ///     Positions the bar against the far edge for the current size. Call after setting <c>Width</c>/<c>Height</c>;
    ///     <see cref="Sync" /> also calls it defensively.
    /// </summary>
    public void Layout()
    {
        if (Orientation == ScrollOrientation.Horizontal)
        {
            Bar.X = 0;
            Bar.Width = Width;
            Bar.Height = ScrollBarControl.DEFAULT_WIDTH;
            Bar.Y = Height - ScrollBarControl.DEFAULT_WIDTH;
        } else
        {
            Bar.Y = 0;
            Bar.Height = Height;
            Bar.Width = ScrollBarControl.DEFAULT_WIDTH;
            Bar.X = Width - ScrollBarControl.DEFAULT_WIDTH;
        }
    }

    /// <summary>
    ///     Re-reads metrics from the bound source (content size, viewport, step), re-clamps the offset, and refreshes
    ///     the bar. Call whenever the content or visible area changes.
    /// </summary>
    public void Sync()
    {
        if (Source is null)
            return;

        Layout();
        Model.Configure(Source); //fires MetricsChanged → the binder refreshes the bar
    }

    /// <summary>
    ///     Jumps to the top/start of the content. Call on a fresh show so a reused view doesn't retain the prior
    ///     scroll position.
    /// </summary>
    public void ScrollToStart() => Model.ScrollToStart();

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        if ((Source is null) || !Model.CanScroll)
            return;

        //consume the wheel whenever the surface is scrollable — even a no-op notch at the clamp — so it doesn't bubble
        //to a panel behind this popup (matches the legacy per-panel handlers this replaces)
        Model.WheelBy(e.Delta);
        e.Handled = true;
    }

    //push the resolved offset into the content surface; the bar side of the sync is owned by the binder
    private void OnOffsetChanged(int offset) => Source?.ApplyOffset(offset);
}
