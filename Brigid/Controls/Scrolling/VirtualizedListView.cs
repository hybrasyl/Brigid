#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Scrolling;

/// <summary>
///     What a windowed row slot should display: a real <see cref="VirtualRowKind.Item" />, the trailing virtual row
///     (e.g. "Load More"), or nothing (<see cref="VirtualRowKind.Empty" />).
/// </summary>
public enum VirtualRowKind
{
    Item,
    Trailing,
    Empty
}

/// <summary>
///     The windowed slot descriptor handed to a list's row-bind callback. <see cref="Item" />/<see cref="Index" />/
///     <see cref="Selected" /> are only meaningful when <see cref="Kind" /> is <see cref="VirtualRowKind.Item" />.
/// </summary>
public readonly struct VirtualRow<TItem>
{
    public required VirtualRowKind Kind { get; init; }
    public TItem Item { get; init; }
    public int Index { get; init; }
    public bool Selected { get; init; }
}

/// <summary>
///     A scrolling, row-pooled list: owns a fixed pool of <typeparamref name="TRow" /> controls, a
///     <see cref="ScrollModel" /> + <see cref="ScrollBarControl" />, and the wheel handling — the shared machinery the
///     board/mail/article/world-list panels each used to hand-roll. The consumer supplies a row factory and a bind
///     callback; selection + click/double-click activation are opt-in (<see cref="Selectable" />) for list panels whose
///     rows are passive labels, and left off for panels whose rows handle their own input (e.g. world-list whisper).
///     An optional trailing virtual row models the "Load More" affordance.
///     <para>
///         Rows are held directly (not inside a <see cref="ScrollView" />) so clicks on passive rows bubble to this
///         panel's hit-test rather than being absorbed by an intermediate container. The bar is kept always-visible
///         (rendering disabled when the content fits), matching the legacy list panels — no auto-hide.
///     </para>
/// </summary>
public sealed class VirtualizedListView<TItem, TRow> : UIPanel
    where TRow : UIElement
{
    private const int TrailingRowHit = -2;

    private readonly ScrollBarControl Bar;
    private readonly ScrollBarBinder Binder;
    private readonly Action<TRow, VirtualRow<TItem>> BindRow;
    private readonly int MaxVisibleRows;
    private readonly ScrollModel Model = new();
    private readonly int RowHeight;
    private readonly TRow[] Rows;

    private bool Dirty;
    private IReadOnlyList<TItem> Items = [];

    /// <summary>
    ///     Optional predicate gating click/double-click selection — returns false to suppress it (e.g. while the host
    ///     panel is mid-slide). Wheel is never gated. When unset, selection is always live.
    /// </summary>
    public Func<bool>? InteractionGate { get; init; }

    /// <summary>Content width available to a row — list width minus the bar strip and any insets.</summary>
    public int ContentWidth { get; }

    /// <summary>Rows that fit in the viewport — the pool size.</summary>
    public int VisibleRows => MaxVisibleRows;

    /// <summary>When true, the list tracks selection and raises the click/activate events on passive rows.</summary>
    public bool Selectable { get; init; }

    /// <summary>When true, one extra virtual "trailing" row follows the last item (e.g. "Load More").</summary>
    public bool HasTrailingRow { get; private set; }

    public int SelectedIndex { get; private set; } = -1;

    public int ItemCount => Items.Count;

    public event ListRowHandler? SelectionChanged;
    public event ListRowHandler? ItemActivated;
    public event ListTrailingHandler? TrailingActivated;

    /// <param name="bounds">List bounds within the parent, including the bar strip.</param>
    /// <param name="rowFactory">Builds one pooled row, given the content width it should size to.</param>
    /// <param name="bindRow">Configures a pooled row for a windowed slot (text/colour/visibility live here).</param>
    /// <param name="rowInsetX">Left inset for rows inside the content area.</param>
    /// <param name="rowInsetRight">Extra right inset between the rows and the bar.</param>
    public VirtualizedListView(
        Rectangle bounds,
        int rowHeight,
        Func<int, TRow> rowFactory,
        Action<TRow, VirtualRow<TItem>> bindRow,
        int rowInsetX = 0,
        int rowInsetRight = 0)
    {
        X = bounds.X;
        Y = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;
        RowHeight = rowHeight;
        BindRow = bindRow;
        ContentWidth = Math.Max(0, bounds.Width - ScrollBarControl.DEFAULT_WIDTH - rowInsetX - rowInsetRight);
        MaxVisibleRows = rowHeight > 0 ? bounds.Height / rowHeight : 0;

        Bar = new ScrollBarControl
        {
            Name = "ScrollBar",
            ZIndex = int.MaxValue,
            X = bounds.Width - ScrollBarControl.DEFAULT_WIDTH,
            Y = 0,
            Height = bounds.Height
        };

        Binder = new ScrollBarBinder(Model, Bar);
        Model.Changed += _ => Dirty = true;
        AddChild(Bar);

        Rows = new TRow[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var row = rowFactory(ContentWidth);
            row.X = rowInsetX;
            row.Y = i * rowHeight;
            Rows[i] = row;
            AddChild(row);
        }
    }

    /// <summary>
    ///     Replaces the backing items, re-derives scroll metrics, and (by default) resets the scroll position to the
    ///     top. Selection is left untouched — callers reset it explicitly when the semantics call for it.
    /// </summary>
    public void SetItems(IReadOnlyList<TItem> items, bool hasTrailingRow = false, bool resetScroll = true)
    {
        Items = items;
        HasTrailingRow = hasTrailingRow;

        if (resetScroll)
            Model.ScrollToStart();

        SyncMetrics();
    }

    /// <summary>
    ///     Re-derives scroll metrics and repaints after an in-place edit that kept the same items reference (e.g. a
    ///     removed or appended entry, or a page appended). Optionally updates whether the trailing row shows.
    /// </summary>
    public void Refresh(bool? hasTrailingRow = null)
    {
        if (hasTrailingRow.HasValue)
            HasTrailingRow = hasTrailingRow.Value;

        SyncMetrics();
    }

    /// <summary>Sets the selected item index (clamped to a valid item, or cleared to -1).</summary>
    public void SetSelectedIndex(int index)
    {
        SelectedIndex = (index >= 0) && (index < Items.Count) ? index : -1;
        Dirty = true;
    }

    /// <summary>Scrolls to an absolute item offset (used e.g. to centre the local player in the world list).</summary>
    public void ScrollTo(int offset) => Model.ScrollTo(offset);

    private void SyncMetrics()
    {
        var extent = Items.Count + (HasTrailingRow ? 1 : 0);
        Model.SetMetrics(extent, MaxVisibleRows);
        Binder.Refresh();
        Dirty = true;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        if (Dirty)
        {
            RebindRows();
            Dirty = false;
        }

        base.Draw(spriteBatch);
    }

    private void RebindRows()
    {
        var offset = Model.Offset;

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var entryIndex = offset + i;

            var slot = entryIndex < Items.Count
                ? new VirtualRow<TItem>
                {
                    Kind = VirtualRowKind.Item,
                    Item = Items[entryIndex],
                    Index = entryIndex,
                    Selected = Selectable && (entryIndex == SelectedIndex)
                }
                : HasTrailingRow && (entryIndex == Items.Count)
                    ? new VirtualRow<TItem> { Kind = VirtualRowKind.Trailing }
                    : new VirtualRow<TItem> { Kind = VirtualRowKind.Empty };

            BindRow(Rows[i], slot);
        }
    }

    // ── scroll ──

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        if (!Model.CanScroll)
            return;

        Model.WheelBy(e.Delta);
        e.Handled = true;
    }

    // ── selection / activation (opt-in) ──

    private bool InteractionSuppressed => InteractionGate is not null && !InteractionGate();

    public override void OnClick(ClickEvent e)
    {
        if (!Selectable || InteractionSuppressed || (e.Button != MouseButton.Left))
        {
            base.OnClick(e);

            return;
        }

        var index = HitRow(e.ScreenX, e.ScreenY);

        if (index == TrailingRowHit)
            TrailingActivated?.Invoke();
        else if (index >= 0)
        {
            SelectedIndex = index;
            Dirty = true;
            SelectionChanged?.Invoke(index);
        }

        e.Handled = true;
    }

    public override void OnDoubleClick(DoubleClickEvent e)
    {
        if (!Selectable || InteractionSuppressed || (e.Button != MouseButton.Left))
        {
            base.OnDoubleClick(e);

            return;
        }

        var index = HitRow(e.ScreenX, e.ScreenY);

        if (index >= 0)
        {
            SelectedIndex = index;
            Dirty = true;
            SelectionChanged?.Invoke(index);
            ItemActivated?.Invoke(index);
        }

        e.Handled = true;
    }

    //real item index, TrailingRowHit for the trailing row, or -1 for no row. Bounds match the legacy panels (full
    //rect width, though the bar child intercepts strip clicks before they reach here).
    private int HitRow(int screenX, int screenY)
    {
        var localX = screenX - ScreenX;
        var localY = screenY - ScreenY;

        if ((localX < 0) || (localX >= Width) || (localY < 0) || (localY >= Height))
            return -1;

        var row = localY / RowHeight;

        if (row >= MaxVisibleRows)
            return -1;

        var entryIndex = Model.Offset + row;

        if (HasTrailingRow && (entryIndex == Items.Count))
            return TrailingRowHit;

        return entryIndex < Items.Count ? entryIndex : -1;
    }
}
