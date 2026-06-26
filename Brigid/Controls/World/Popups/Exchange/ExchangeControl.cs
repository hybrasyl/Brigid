#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Exchange;

/// <summary>
///     Exchange/trade panel using _nexch prefab. Thin view that subscribes to
///     <see cref="Exchange" /> state events. Two-sided layout with up to 4 items per side.
/// </summary>
public sealed class ExchangeControl : PrefabPanel
{
    private const int MAX_VISIBLE_ITEMS = 4;
    private const int ITEM_ROW_HEIGHT = 32;

    private readonly ScrollBarControl MyHorizontalScroll;
    private readonly ScrollBarControl MyVerticalScroll;
    private readonly UIPanel MyItemsContainer;
    private readonly Rectangle MyExchangeRect;
    private readonly UILabel? MyIdLabel;

    private readonly ExchangeItemControl[] MyItems = new ExchangeItemControl[MAX_VISIBLE_ITEMS];
    private readonly UILabel? MyMoneyLabel;
    private readonly Rectangle ViewportBounds;

    private readonly UIImage? YourAckImage;
    private readonly ScrollBarControl YourHorizontalScroll;
    private readonly ScrollBarControl YourVerticalScroll;
    private readonly UIPanel YourItemsContainer;
    private readonly Rectangle YourExchangeRect;
    private readonly UILabel? YourIdLabel;
    private readonly ExchangeItemControl[] YourItems = new ExchangeItemControl[MAX_VISIBLE_ITEMS];
    private readonly UILabel? YourMoneyLabel;

    private int MyHorizontalOffset;
    private int MyVerticalOffset;
    private int YourHorizontalOffset;
    private int YourVerticalOffset;

    public UIButton? CancelButton { get; }
    public UIButton? OkButton { get; }

    public uint OtherUserId => WorldState.Exchange.OtherUserId;

    /// <summary>
    ///     The player's own name, used for the left side label. Set from WorldScreen after DisplayAisling.
    /// </summary>
    private static string PlayerName => WorldState.PlayerName;

    public ExchangeControl(Rectangle viewportBounds)
        : base("_nexch")
    {
        Name = "Exchange";
        Visible = false;
        UsesControlStack = true;
        ViewportBounds = viewportBounds;

        OkButton = CreateButton("OK");
        CancelButton = CreateButton("Cancel");

        if (OkButton is not null)
            OkButton.Clicked += () =>
            {
                OkButton.Enabled = false;
                OnOk?.Invoke();
            };

        if (CancelButton is not null)
            CancelButton.Clicked += () => OnCancel?.Invoke();

        MyIdLabel = CreateLabel("MyID");
        YourIdLabel = CreateLabel("YourID");
        MyMoneyLabel = CreateLabel("MyMoney");
        YourMoneyLabel = CreateLabel("YourMoney");

        MyExchangeRect = GetRect("MyExchange");
        YourExchangeRect = GetRect("YourExchange");

        YourAckImage = CreateImage("YourACK");

        YourAckImage?.Visible = false;

        //container panels for item clipping — sized to exclude scrollbar area
        var clipWidth = MyExchangeRect.Width - ScrollBarControl.DEFAULT_WIDTH - 4;

        MyItemsContainer = new UIPanel
        {
            Name = "MyItemsContainer",
            X = MyExchangeRect.X,
            Y = MyExchangeRect.Y,
            Width = clipWidth,
            Height = MyExchangeRect.Height,
            IsPassThrough = true
        };

        AddChild(MyItemsContainer);

        YourItemsContainer = new UIPanel
        {
            Name = "YourItemsContainer",
            X = YourExchangeRect.X,
            Y = YourExchangeRect.Y,
            Width = clipWidth,
            Height = YourExchangeRect.Height,
            IsPassThrough = true
        };

        AddChild(YourItemsContainer);

        //create item controls as children of the container panels
        CreateItemControls(MyItems, MyItemsContainer);
        CreateItemControls(YourItems, YourItemsContainer);

        //vertical scrollbars — right edge of each exchange rect
        MyVerticalScroll = new ScrollBarControl
        {
            Name = "MyVerticalScroll",
            X = MyExchangeRect.Right - ScrollBarControl.DEFAULT_WIDTH,
            Y = MyExchangeRect.Y,
            Height = MyExchangeRect.Height - ScrollBarControl.DEFAULT_WIDTH
        };

        MyVerticalScroll.OnValueChanged += v =>
        {
            MyVerticalOffset = v;
            RefreshVisibleItems(false);
        };

        AddChild(MyVerticalScroll);

        YourVerticalScroll = new ScrollBarControl
        {
            Name = "YourVerticalScroll",
            X = YourExchangeRect.Right - ScrollBarControl.DEFAULT_WIDTH,
            Y = YourExchangeRect.Y,
            Height = YourExchangeRect.Height - ScrollBarControl.DEFAULT_WIDTH
        };

        YourVerticalScroll.OnValueChanged += v =>
        {
            YourVerticalOffset = v;
            RefreshVisibleItems(true);
        };

        AddChild(YourVerticalScroll);

        //horizontal scrollbars — bottom edge of each exchange rect
        MyHorizontalScroll = new ScrollBarControl
        {
            Name = "MyHorizontalScroll",
            Orientation = ScrollOrientation.Horizontal,
            X = MyExchangeRect.X,
            Y = MyExchangeRect.Bottom - ScrollBarControl.DEFAULT_WIDTH,
            Width = MyExchangeRect.Width - ScrollBarControl.DEFAULT_WIDTH,
            Height = ScrollBarControl.DEFAULT_WIDTH
        };

        MyHorizontalScroll.OnValueChanged += v =>
        {
            MyHorizontalOffset = v;
            ApplyHorizontalOffset(MyItems, v);
        };

        AddChild(MyHorizontalScroll);

        YourHorizontalScroll = new ScrollBarControl
        {
            Name = "YourHorizontalScroll",
            Orientation = ScrollOrientation.Horizontal,
            X = YourExchangeRect.X,
            Y = YourExchangeRect.Bottom - ScrollBarControl.DEFAULT_WIDTH,
            Width = YourExchangeRect.Width - ScrollBarControl.DEFAULT_WIDTH,
            Height = ScrollBarControl.DEFAULT_WIDTH
        };

        YourHorizontalScroll.OnValueChanged += v =>
        {
            YourHorizontalOffset = v;
            ApplyHorizontalOffset(YourItems, v);
        };

        AddChild(YourHorizontalScroll);

        //subscribe to state events
        WorldState.Exchange.Started += OnExchangeStarted;
        WorldState.Exchange.ItemAdded += OnExchangeItemAdded;
        WorldState.Exchange.GoldSet += OnExchangeGoldSet;
        WorldState.Exchange.OtherAccepted += OnExchangeOtherAccepted;
        WorldState.Exchange.Closed += OnExchangeClosed;
    }

    private void ClearAllItems()
    {
        foreach (var item in MyItems)
            item.ClearItem();

        foreach (var item in YourItems)
            item.ClearItem();
    }

    private static void CreateItemControls(ExchangeItemControl[] items, UIPanel container)
    {
        var itemWidth = container.Width;

        for (var i = 0; i < MAX_VISIBLE_ITEMS; i++)
        {
            var control = new ExchangeItemControl
            {
                Name = $"ExchangeItem{i}",
                Y = i * ITEM_ROW_HEIGHT,
                Width = itemWidth
            };

            control.SetBaseX(0);
            items[i] = control;
            container.AddChild(control);
        }
    }

    public override void Dispose()
    {
        WorldState.Exchange.Started -= OnExchangeStarted;
        WorldState.Exchange.ItemAdded -= OnExchangeItemAdded;
        WorldState.Exchange.GoldSet -= OnExchangeGoldSet;
        WorldState.Exchange.OtherAccepted -= OnExchangeOtherAccepted;
        WorldState.Exchange.Closed -= OnExchangeClosed;

        base.Dispose();
    }

    /// <summary>
    ///     Returns true if the given screen coordinates are within the MyMoney label area.
    /// </summary>
    public bool IsMyMoneyClicked(int mouseX, int mouseY) => MyMoneyLabel?.ContainsPoint(mouseX, mouseY) ?? false;

    public event CancelHandler? OnCancel;

    private void OnExchangeClosed(string? message)
    {
        ClearAllItems();
        ResetAllScrollbars();
        Hide();
    }

    private void OnExchangeGoldSet(bool rightSide)
    {
        var amount = rightSide ? WorldState.Exchange.OtherGold : WorldState.Exchange.MyGold;
        var label = rightSide ? YourMoneyLabel : MyMoneyLabel;
        label?.Text = amount.ToString("N0");
    }

    private void OnExchangeItemAdded(bool rightSide, byte index)
    {
        UpdateVerticalScrollbar(rightSide);
        UpdateHorizontalScrollbar(rightSide);
        RefreshVisibleItems(rightSide);
    }

    private void OnExchangeOtherAccepted() => YourAckImage?.Visible = true;

    private void OnExchangeStarted()
    {
        ClearAllItems();
        ResetAllScrollbars();

        MyIdLabel?.Text = PlayerName;
        YourIdLabel?.Text = WorldState.Exchange.OtherUserName;
        MyMoneyLabel?.Text = "0";
        YourMoneyLabel?.Text = "0";
        YourAckImage?.Visible = false;

        OkButton?.Enabled = true;

        //center vertically in viewport
        Y = ViewportBounds.Y + (ViewportBounds.Height - Height) / 2;

        Show();
    }

    public event OkHandler? OnOk;

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            OnCancel?.Invoke();
            e.Handled = true;
        }
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        var mouseX = e.ScreenX;
        var mouseY = e.ScreenY;

        //determine which exchange rect the mouse is over
        if (MyItemsContainer.ContainsPoint(mouseX, mouseY))
        {
            if (MyVerticalScroll.TotalItems <= MyVerticalScroll.VisibleItems)
            {
                e.Handled = true;

                return;
            }

            var newValue = Math.Clamp(MyVerticalScroll.Value - e.Delta, 0, MyVerticalScroll.MaxValue);

            if (newValue != MyVerticalScroll.Value)
            {
                MyVerticalScroll.Value = newValue;
                MyVerticalOffset = newValue;
                RefreshVisibleItems(false);
            }

            e.Handled = true;
        } else if (YourItemsContainer.ContainsPoint(mouseX, mouseY))
        {
            if (YourVerticalScroll.TotalItems <= YourVerticalScroll.VisibleItems)
            {
                e.Handled = true;

                return;
            }

            var newValue = Math.Clamp(YourVerticalScroll.Value - e.Delta, 0, YourVerticalScroll.MaxValue);

            if (newValue != YourVerticalScroll.Value)
            {
                YourVerticalScroll.Value = newValue;
                YourVerticalOffset = newValue;
                RefreshVisibleItems(true);
            }

            e.Handled = true;
        }
    }

    private static void ApplyHorizontalOffset(ExchangeItemControl[] items, int offset)
    {
        foreach (var item in items)
            item.HorizontalOffset = offset;
    }

    private void RefreshVisibleItems(bool rightSide)
    {
        var items = rightSide ? YourItems : MyItems;
        var offset = rightSide ? YourVerticalOffset : MyVerticalOffset;
        var horizontalOffset = rightSide ? YourHorizontalOffset : MyHorizontalOffset;
        var totalCount = WorldState.Exchange.GetItemCount(rightSide);

        for (var i = 0; i < MAX_VISIBLE_ITEMS; i++)
        {
            var dataIndex = (byte)(offset + i);

            if (dataIndex < totalCount)
            {
                var data = WorldState.Exchange.GetItem(rightSide, dataIndex);

                if (data.HasValue)
                {
                    items[i]
                        .SetItem(data.Value.Sprite, data.Value.Color, data.Value.Name ?? string.Empty);

                    items[i].HorizontalOffset = horizontalOffset;

                    continue;
                }
            }

            items[i]
                .ClearItem();
        }

        UpdateHorizontalScrollbar(rightSide);
    }

    private void ResetAllScrollbars()
    {
        MyVerticalOffset = 0;
        YourVerticalOffset = 0;
        MyHorizontalOffset = 0;
        YourHorizontalOffset = 0;

        MyVerticalScroll.Value = 0;
        MyVerticalScroll.TotalItems = 0;
        MyVerticalScroll.VisibleItems = MAX_VISIBLE_ITEMS;
        MyVerticalScroll.MaxValue = 0;

        YourVerticalScroll.Value = 0;
        YourVerticalScroll.TotalItems = 0;
        YourVerticalScroll.VisibleItems = MAX_VISIBLE_ITEMS;
        YourVerticalScroll.MaxValue = 0;

        MyHorizontalScroll.Value = 0;
        MyHorizontalScroll.TotalItems = 0;
        MyHorizontalScroll.VisibleItems = 0;
        MyHorizontalScroll.MaxValue = 0;

        YourHorizontalScroll.Value = 0;
        YourHorizontalScroll.TotalItems = 0;
        YourHorizontalScroll.VisibleItems = 0;
        YourHorizontalScroll.MaxValue = 0;

        ApplyHorizontalOffset(MyItems, 0);
        ApplyHorizontalOffset(YourItems, 0);
    }

    private void UpdateHorizontalScrollbar(bool rightSide)
    {
        var items = rightSide ? YourItems : MyItems;
        var scrollbar = rightSide ? YourHorizontalScroll : MyHorizontalScroll;
        var rect = rightSide ? YourExchangeRect : MyExchangeRect;
        var visibleWidth = rect.Width - ScrollBarControl.DEFAULT_WIDTH - 10;

        var maxEntryWidth = 0;

        foreach (var item in items)
            if (item.Visible && (item.EntryWidth > maxEntryWidth))
                maxEntryWidth = item.EntryWidth;

        var overflow = maxEntryWidth - visibleWidth;

        if (overflow > 0)
        {
            scrollbar.TotalItems = maxEntryWidth;
            scrollbar.VisibleItems = visibleWidth;
            scrollbar.MaxValue = overflow;
        } else
        {
            scrollbar.TotalItems = 0;
            scrollbar.VisibleItems = 0;
            scrollbar.MaxValue = 0;
            scrollbar.Value = 0;

            //reset offset when no overflow
            if (rightSide)
            {
                YourHorizontalOffset = 0;
                ApplyHorizontalOffset(YourItems, 0);
            } else
            {
                MyHorizontalOffset = 0;
                ApplyHorizontalOffset(MyItems, 0);
            }
        }
    }

    private void UpdateVerticalScrollbar(bool rightSide)
    {
        var totalCount = WorldState.Exchange.GetItemCount(rightSide);
        var scrollbar = rightSide ? YourVerticalScroll : MyVerticalScroll;

        scrollbar.TotalItems = totalCount;
        scrollbar.VisibleItems = MAX_VISIBLE_ITEMS;
        scrollbar.MaxValue = Math.Max(0, totalCount - MAX_VISIBLE_ITEMS);
    }
}