#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Scrolling;

/// <summary>
///     What a windowed row slot should display: a real <see cref="VirtualRowKind.Item" />, or nothing
///     (<see cref="VirtualRowKind.Empty" />).
/// </summary>
public enum VirtualRowKind
{
    Item,
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
///     Reaching the last item (by wheel, bar, or keyboard) raises <see cref="ReachedEnd" /> — the retail-style
///     scroll-back paging hook that replaces any client-invented "Load More" affordance.
///     <para>
///         Rows are held directly (not inside a <see cref="ScrollView" />) so clicks on passive rows bubble to this
///         panel's hit-test rather than being absorbed by an intermediate container. The bar is kept always-visible
///         (rendering disabled when the content fits), matching the legacy list panels — no auto-hide.
///     </para>
/// </summary>
public sealed class VirtualizedListView<TItem, TRow> : UIPanel
    where TRow : UIElement
{
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

    public int SelectedIndex { get; private set; } = -1;

    public int ItemCount => Items.Count;

    public event ListRowHandler? SelectionChanged;
    public event ListRowHandler? ItemActivated;

    /// <summary>
    ///     Raised whenever the scroll position reaches the last item (the content is scrolled to the bottom). Boards/mail
    ///     use this to request the next older page, mirroring retail's continuous scroll-back paging. Not raised when the
    ///     content fits (nothing to scroll) or on a no-op scroll at the bottom — a consumer re-arms it by growing the item
    ///     list, which moves the bottom past the current offset.
    /// </summary>
    public event Action? ReachedEnd;

    /// <param name="bounds">List bounds within the parent, including the bar strip.</param>
    /// <param name="rowFactory">Builds one pooled row, given the content width it should size to and its pool index (for
    ///     naming — <c>Name</c> is init-only so a row can only be named in its factory initializer).</param>
    /// <param name="bindRow">Configures a pooled row for a windowed slot (text/colour/visibility live here).</param>
    /// <param name="rowInsetX">Left inset for rows inside the content area.</param>
    /// <param name="rowInsetRight">Extra right inset between the rows and the bar.</param>
    public VirtualizedListView(
        Rectangle bounds,
        int rowHeight,
        Func<int, int, TRow> rowFactory,
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

        Model.Changed += _ =>
        {
            Dirty = true;

            if ((Model.Max > 0) && (Model.Offset >= Model.Max))
                ReachedEnd?.Invoke();
        };

        AddChild(Bar);

        Rows = new TRow[MaxVisibleRows];

        for (var i = 0; i < MaxVisibleRows; i++)
        {
            var row = rowFactory(ContentWidth, i);
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
    public void SetItems(IReadOnlyList<TItem> items, bool resetScroll = true)
    {
        Items = items;

        if (resetScroll)
            Model.ScrollToStart();

        SyncMetrics();
    }

    /// <summary>
    ///     Re-derives scroll metrics and repaints after an in-place edit that kept the same items reference (e.g. a
    ///     removed or appended entry, or a page appended).
    /// </summary>
    public void Refresh() => SyncMetrics();

    /// <summary>Sets the selected item index (clamped to a valid item, or cleared to -1).</summary>
    public void SetSelectedIndex(int index)
    {
        SelectedIndex = (index >= 0) && (index < Items.Count) ? index : -1;
        Dirty = true;
    }

    /// <summary>
    ///     Keyboard row navigation: moves the selection by <paramref name="delta" /> rows (from either end when
    ///     nothing is selected), keeps it on screen, and raises <see cref="SelectionChanged" />. Unlike
    ///     <see cref="SetSelectedIndex" /> — which is a silent programmatic set — this is a user action, so it
    ///     notifies. Driving the selection to the last row scrolls to the bottom, which re-arms
    ///     <see cref="ReachedEnd" />, so arrow keys and the wheel share one paging trigger.
    /// </summary>
    public void MoveSelection(int delta)
    {
        if (Items.Count == 0)
            return;

        var next = SelectedIndex < 0
            ? delta > 0 ? 0 : Items.Count - 1
            : Math.Clamp(SelectedIndex + delta, 0, Items.Count - 1);

        SetSelectedIndex(next);
        EnsureVisible(next);
        SelectionChanged?.Invoke(next);
    }

    /// <summary>Scrolls to an absolute item offset (used e.g. to centre the local player in the world list).</summary>
    public void ScrollTo(int offset) => Model.ScrollTo(offset);

    /// <summary>
    ///     Scrolls the minimum amount needed to bring <paramref name="index" /> into the visible window. Driving the
    ///     selection to the last item scrolls to the bottom, which raises <see cref="ReachedEnd" /> — so keyboard
    ///     navigation and the mouse wheel share one paging trigger.
    /// </summary>
    public void EnsureVisible(int index)
    {
        if ((index < 0) || (index >= Items.Count))
            return;

        if (index < Model.Offset)
            Model.ScrollTo(index);
        else if (index >= Model.Offset + MaxVisibleRows)
            Model.ScrollTo(index - MaxVisibleRows + 1);
    }

    private void SyncMetrics()
    {
        Model.SetMetrics(Items.Count, MaxVisibleRows); //fires MetricsChanged → the binder refreshes the bar
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

        if (index >= 0)
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

    //real item index, or -1 for no row. Bounds match the legacy panels (full rect width, though the bar child
    //intercepts strip clicks before they reach here).
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

        return entryIndex < Items.Count ? entryIndex : -1;
    }
}
