#region
using Brigid.Controls.Generic;
#endregion

namespace Brigid.Controls.Scrolling;

/// <summary>
///     Two-way binding between a <see cref="ScrollModel" /> and a <see cref="ScrollBarControl" />: pushes the model's
///     metrics and offset onto the bar, and feeds bar-driven value changes (arrows / thumb drag / page / strip wheel)
///     back into the model — with a re-entrancy guard around the bar writes. Inversion is read from
///     <see cref="ScrollModel.Inverted" />, so bottom-anchored surfaces map the thumb without any extra flag.
///     <para>
///         Shared by <see cref="ScrollView" /> and <see cref="VirtualizedListView{TItem,TRow}" /> so the sync logic —
///         the very copy-paste this refactor exists to remove — lives in exactly one place. Hosts still subscribe to
///         <see cref="ScrollModel.Changed" /> separately for their own repaint (apply-offset / mark-dirty).
///     </para>
/// </summary>
public sealed class ScrollBarBinder
{
    private readonly ScrollBarControl Bar;
    private readonly ScrollModel Model;
    private bool Syncing;

    public ScrollBarBinder(ScrollModel model, ScrollBarControl bar)
    {
        Model = model;
        Bar = bar;
        Bar.OnValueChanged += OnBarValueChanged;
        Model.Changed += OnModelChanged;
    }

    /// <summary>
    ///     Re-pushes the full model state (extent / viewport / max / offset) onto the bar. Call after the metrics change.
    /// </summary>
    public void Refresh()
    {
        Syncing = true;
        Bar.TotalItems = Model.Extent;
        Bar.VisibleItems = Model.Viewport;
        Bar.MaxValue = Model.Max;
        Bar.Value = BarValueFor(Model.Offset);
        Syncing = false;
    }

    private void OnModelChanged(int offset)
    {
        //skip when the model change originated from the bar itself (we're mid-write)
        if (Syncing)
            return;

        Syncing = true;
        Bar.Value = BarValueFor(offset);
        Syncing = false;
    }

    private void OnBarValueChanged(int value)
    {
        if (Syncing)
            return;

        Model.ScrollTo(OffsetForBarValue(value));
    }

    private int BarValueFor(int offset) => Model.Inverted ? Model.Max - offset : offset;

    private int OffsetForBarValue(int value) => Model.Inverted ? Model.Max - value : value;
}
