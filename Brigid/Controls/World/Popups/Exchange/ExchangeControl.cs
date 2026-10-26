#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Exchange;

/// <summary>
///     The exchange window (BRIG-126 step 3b), a themed <see cref="CenteredModalPanel" /> in place of the legacy
///     <c>_nexch</c> prefab. A thin view of the <c>WorldState.Exchange</c> state: two columns, yours (<c>my</c>) on the
///     left and the other player's (<c>their</c>) on the right. Each column has the name, a list of four item rows with a
///     vertical and a horizontal scroll bar, and the gold. "Accepted" shows over a column when that side accepted: over
///     yours when you click Accept, and over either column when the server confirms that side. Accept and Cancel are on the
///     bottom bar.
///     <para>
///         The panel is not modal: the player drags items from the inventory onto it, and the keys that it does not use go
///         on to the screen (D10). Escape cancels the exchange. The screen sends the packets: <see cref="OnOk" /> accepts,
///         <see cref="OnCancel" /> cancels, and <see cref="OnGoldClicked" /> opens the gold prompt.
///     </para>
/// </summary>
public sealed class ExchangeControl : CenteredModalPanel
{
    private const int MAX_VISIBLE_ITEMS = 4;
    private const int ITEM_ROW_HEIGHT = 32;

    private const int COLUMN_W = 240;
    private const int COLUMN_GAP = 10;
    private const int HEADER_H = 16;
    private const int ACCEPTED_W = 70;
    private const int LIST_INSET = 1;
    private const int LIST_W = COLUMN_W - BAR;
    private const int LIST_H = MAX_VISIBLE_ITEMS * ITEM_ROW_HEIGHT + 2 * LIST_INSET;
    private const int LIST_Y = HEADER_H + 2;
    private const int GOLD_Y = LIST_Y + LIST_H + BAR + 6;
    private const int GOLD_H = 18;
    private const int GOLD_BUTTON_W = 56;
    private const int ACTION_W = 64;
    private const int BAR = ScrollBarControl.DEFAULT_WIDTH;
    private const int CONTENT_W = 2 * COLUMN_W + COLUMN_GAP;
    private const int CONTENT_H = GOLD_Y + GOLD_H;

    private readonly Side Mine;
    private readonly Side Theirs;

    /// <summary>Accept (the legacy OK). It is disabled after a click, until the next exchange starts.</summary>
    public TextButton OkButton { get; }

    public TextButton CancelButton { get; }

    /// <summary>The button that sets your gold. It is disabled after you accept.</summary>
    public TextButton GoldButton { get; }

    public uint OtherUserId => WorldState.Exchange.OtherUserId;

    public ExchangeControl(Rectangle viewportBounds)
        : base("Exchange", CONTENT_W + 2 * PADDING, TITLE_H + 1 + CONTENT_H + BOTTOM_H, useTheme: true)
    {
        Name = "exchange";
        SetViewportBounds(viewportBounds);

        var content = ContentBounds;

        Mine = new Side(false)
        {
            Name = "my",
            X = content.X,
            Y = content.Y
        };
        AddChild(Mine);

        Theirs = new Side(true)
        {
            Name = "their",
            X = content.X + COLUMN_W + COLUMN_GAP,
            Y = content.Y
        };
        AddChild(Theirs);

        GoldButton = new TextButton("Gold", GOLD_BUTTON_W, GOLD_H)
        {
            Name = "gold_button",
            X = COLUMN_W - GOLD_BUTTON_W,
            Y = GOLD_Y
        };
        GoldButton.Clicked += () => OnGoldClicked?.Invoke();
        Mine.AddChild(GoldButton);

        //Cancel takes the place of Close, so that closing the window always cancels the exchange
        CloseButton.Visible = false;
        CancelButton = AddBottomBarButton("Cancel", ACTION_W, () => OnCancel?.Invoke(), "cancel");
        OkButton = AddBottomBarButton("Accept", ACTION_W, Accept, "accept");

        WorldState.Exchange.Started += OnExchangeStarted;
        WorldState.Exchange.ItemAdded += OnExchangeItemAdded;
        WorldState.Exchange.GoldSet += OnExchangeGoldSet;
        WorldState.Exchange.Accepted += OnExchangeAccepted;
        WorldState.Exchange.Closed += OnExchangeClosed;
    }

    //the exchange is used beside the world: Enter for chat and the other keys above the screen's stack guard still work
    protected override bool LetsKeysThrough => true;

    public event CancelHandler? OnCancel;
    public event OkHandler? OnOk;

    /// <summary>Raised when the player clicks the gold button. The screen opens the gold prompt over the panel.</summary>
    public event Action? OnGoldClicked;

    /// <summary>The rows of one side, in the order they show. Internal for the tests.</summary>
    internal IReadOnlyList<ExchangeItemControl> Rows(bool rightSide) => (rightSide ? Theirs : Mine).Rows;

    /// <summary>The name and the gold text of one side, and whether it shows "Accepted". Internal for the tests.</summary>
    internal (string Name, string Gold, bool Accepted) SideText(bool rightSide)
    {
        var side = rightSide ? Theirs : Mine;

        return (side.NameLabel.Text, side.GoldLabel.Text, side.AcceptedLabel.Visible);
    }

    /// <summary>Scrolls the item list of one side by wheel notches, as the wheel over the list does. Internal for the tests.</summary>
    internal void WheelList(bool rightSide, int delta) => (rightSide ? Theirs : Mine).Wheel(delta);

    public override void Dispose()
    {
        //a row's icon is the renderer's shared, cached texture, and an image disposes its texture; clear the rows first
        Mine.Reset();
        Theirs.Reset();

        WorldState.Exchange.Started -= OnExchangeStarted;
        WorldState.Exchange.ItemAdded -= OnExchangeItemAdded;
        WorldState.Exchange.GoldSet -= OnExchangeGoldSet;
        WorldState.Exchange.Accepted -= OnExchangeAccepted;
        WorldState.Exchange.Closed -= OnExchangeClosed;

        base.Dispose();
    }

    //your accept shows at once, because Hybrasyl confirms your side only when the exchange completes
    private void Accept()
    {
        OkButton.SetEnabled(false);
        GoldButton.SetEnabled(false);
        Mine.AcceptedLabel.Visible = true;
        OnOk?.Invoke();
    }

    private void OnExchangeClosed(string? message)
    {
        Mine.Reset();
        Theirs.Reset();
        Hide();
    }

    private void OnExchangeGoldSet(bool rightSide)
    {
        var amount = rightSide ? WorldState.Exchange.OtherGold : WorldState.Exchange.MyGold;
        (rightSide ? Theirs : Mine).SetGold(amount);
    }

    private void OnExchangeItemAdded(bool rightSide, byte index) => (rightSide ? Theirs : Mine).ItemsChanged();

    private void OnExchangeAccepted(bool rightSide) => (rightSide ? Theirs : Mine).AcceptedLabel.Visible = true;

    private void OnExchangeStarted()
    {
        Mine.Reset();
        Theirs.Reset();
        Mine.NameLabel.Text = WorldState.PlayerName;
        Theirs.NameLabel.Text = WorldState.Exchange.OtherUserName;
        OkButton.SetEnabled(true);
        GoldButton.SetEnabled(true);
        Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        //Escape cancels the exchange, and the screen closes the window when the exchange closes. The base Escape (Hide)
        //does not run, so the window stays until the exchange closes
        if (e.Key == Keys.Escape)
        {
            OnCancel?.Invoke();
            e.Handled = true;

            return;
        }

        base.OnKeyDown(e);
    }

    //the wheel scrolls the list under the mouse
    public override void OnMouseScroll(MouseScrollEvent e)
    {
        foreach (var side in (Side[]) [Mine, Theirs])
            if (side.ListContains(e.ScreenX, e.ScreenY))
                side.Wheel(e.Delta);

        e.Handled = true;
    }

    /// <summary>
    ///     One column, in its own coordinates: the name, the "Accepted" mark, the item list with its two bars, and the gold.
    ///     It takes no mouse input itself; its children do.
    /// </summary>
    private sealed class Side : UIPanel
    {
        private readonly bool RightSide;
        private readonly ItemList List;
        private readonly ScrollModel VerticalModel = new();
        private readonly ScrollModel HorizontalModel = new();

        //kept with the other scroll fields, as ScrollView keeps its binder; the subscriptions keep a binder alive
        private readonly ScrollBarBinder VerticalBinder;
        private readonly ScrollBarBinder HorizontalBinder;

        public readonly UILabel NameLabel;
        public readonly UILabel AcceptedLabel;
        public readonly UILabel GoldLabel;
        public readonly ExchangeItemControl[] Rows = new ExchangeItemControl[MAX_VISIBLE_ITEMS];

        public Side(bool rightSide)
        {
            RightSide = rightSide;
            Width = COLUMN_W;
            Height = CONTENT_H;
            IsPassThrough = true;

            NameLabel = new UILabel
            {
                Name = "name",
                Width = COLUMN_W - ACCEPTED_W,
                Height = HEADER_H,
                VerticalAlignment = VerticalAlignment.Center,
                PaddingLeft = 0,
                IsHitTestVisible = false
            };
            NameLabel.SetForegroundRole(ThemeRoles.TextTitle);
            AddChild(NameLabel);

            AcceptedLabel = new UILabel
            {
                Name = "accepted",
                X = COLUMN_W - ACCEPTED_W,
                Width = ACCEPTED_W,
                Height = HEADER_H,
                Text = "Accepted",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                PaddingRight = 0,
                Visible = false,
                IsHitTestVisible = false
            };
            AcceptedLabel.SetForegroundRole(ThemeRoles.TextSelected);
            AddChild(AcceptedLabel);

            List = new ItemList
            {
                Name = "list",
                Y = LIST_Y,
                Width = LIST_W,
                Height = LIST_H,
                IsPassThrough = true
            };
            AddChild(List);

            //the rows sit inside the list's border, which clips them, so that a moved row does not draw over the border
            var rowArea = new UIPanel
            {
                Name = "rows",
                X = LIST_INSET,
                Y = LIST_INSET,
                Width = LIST_W - 2 * LIST_INSET,
                Height = LIST_H - 2 * LIST_INSET,
                IsPassThrough = true
            };
            List.AddChild(rowArea);

            for (var i = 0; i < MAX_VISIBLE_ITEMS; i++)
            {
                var row = new ExchangeItemControl
                {
                    Name = $"row_{i}",
                    Y = i * ITEM_ROW_HEIGHT,
                    MinWidth = rowArea.Width
                };

                row.SetBaseX(1);
                Rows[i] = row;
                rowArea.AddChild(row);
            }

            var verticalBar = new ScrollBarControl
            {
                Name = "vertical",
                X = LIST_W,
                Y = LIST_Y,
                Height = LIST_H
            };
            AddChild(verticalBar);
            VerticalBinder = new ScrollBarBinder(VerticalModel, verticalBar);
            VerticalModel.Changed += _ => Refresh();

            var horizontalBar = new ScrollBarControl
            {
                Name = "horizontal",
                Orientation = ScrollOrientation.Horizontal,
                Y = LIST_Y + LIST_H,
                Width = LIST_W,
                Height = BAR
            };
            AddChild(horizontalBar);
            HorizontalBinder = new ScrollBarBinder(HorizontalModel, horizontalBar);
            HorizontalModel.Changed += ApplyHorizontalOffset;

            GoldLabel = new UILabel
            {
                Name = "gold",
                Y = GOLD_Y,
                Width = COLUMN_W - GOLD_BUTTON_W - 6,
                Height = GOLD_H,
                VerticalAlignment = VerticalAlignment.Center,
                PaddingLeft = 0,
                IsHitTestVisible = false
            };
            GoldLabel.SetForegroundRole(ThemeRoles.Text);
            AddChild(GoldLabel);
        }

        public void Reset()
        {
            foreach (var row in Rows)
                row.ClearItem();

            AcceptedLabel.Visible = false;
            SetGold(0);

            VerticalModel.SetMetrics(0, MAX_VISIBLE_ITEMS);
            VerticalModel.ScrollToStart();
            HorizontalModel.SetMetrics(0, 0);
            HorizontalModel.ScrollToStart();
            ApplyHorizontalOffset(0);
        }

        public void SetGold(int amount) => GoldLabel.Text = $"Gold: {amount:N0}";

        public void ItemsChanged()
        {
            VerticalModel.SetMetrics(WorldState.Exchange.GetItemCount(RightSide), MAX_VISIBLE_ITEMS);
            Refresh();
        }

        public bool ListContains(int screenX, int screenY) => List.ContainsPoint(screenX, screenY);

        public void Wheel(int delta)
        {
            if (VerticalModel.CanScroll)
                VerticalModel.WheelBy(delta);
        }

        //shows the items from the vertical offset, then sizes the horizontal bar to the widest shown name
        private void Refresh()
        {
            var totalCount = WorldState.Exchange.GetItemCount(RightSide);

            for (var i = 0; i < MAX_VISIBLE_ITEMS; i++)
            {
                var dataIndex = VerticalModel.Offset + i;

                if ((dataIndex < totalCount) && WorldState.Exchange.GetItem(RightSide, (byte)dataIndex) is { } data)
                {
                    Rows[i].SetItem(data.Sprite, data.Color, data.Name ?? string.Empty);
                    Rows[i].HorizontalOffset = HorizontalModel.Offset;
                } else
                    Rows[i].ClearItem();
            }

            var widest = 0;

            foreach (var row in Rows)
                if (row.Visible)
                    widest = Math.Max(widest, row.EntryWidth);

            //pixel-granular: the offset can go as far as the widest name overflows the list (0 when it fits)
            HorizontalModel.SetMetrics(widest, LIST_W - 2 * LIST_INSET - 2);
        }

        private void ApplyHorizontalOffset(int offset)
        {
            foreach (var row in Rows)
                row.HorizontalOffset = offset;
        }
    }

    //the well of an item list: a border in the divider colour, which also clips the rows that the horizontal bar moves
    private sealed class ItemList : UIPanel
    {
        protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
            => DrawBorderClipped(spriteBatch, bounds, ThemeColor(ThemeRoles.Divider));
    }
}
