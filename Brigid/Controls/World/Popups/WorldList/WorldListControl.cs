#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Data.Models;
using Brigid.Models;
using Brigid.Rendering;
using Chaos.DarkAges.Definitions;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.WorldList;

/// <summary>
///     From-scratch, asset-free online-players ("Aislings") browser. Draws its whole frame with primitives (no legacy
///     SPF/EPF prefab), mirroring <see cref="Dialog.BankShopPanel" />: a centered window with a centered title, a
///     "Total" readout, a vertical strip of class-filter buttons on the left, a bordered count column, and a scrollbar'd
///     name/title list on the right, above a bottom bar with a Close button. Double-clicking a name row starts a
///     whisper. Family/friend names are recoloured; a social-status icon is shown per row.
/// </summary>
public sealed class WorldListControl : UIPanel
{
    private const int TAB_COUNT = 9;
    private const int STATUS_ICON_COUNT = 8;
    private const int SCROLLBAR_W = 16;

    //── layout ──
    private const int PADDING = 6;
    private const int TITLE_H = 20;
    private const int TOTAL_H = 16;
    private const int BOTTOM_H = 30;

    private const int FILTER_W = 72;
    private const int FILTER_H = 18;
    private const int FILTER_STRIDE = 20;

    private const int BODY_Y = TITLE_H + TOTAL_H;
    private const int BODY_H = TAB_COUNT * FILTER_STRIDE;
    private const int BOTTOM_Y = BODY_Y + BODY_H;

    private const int BUTTON_X = PADDING;
    private const int COUNT_BOX_X = BUTTON_X + FILTER_W + 4;
    private const int COUNT_BOX_W = 40;
    private const int COUNT_PAD = 5;
    private const int COUNT_RIGHT_EDGE = COUNT_BOX_X + COUNT_BOX_W - COUNT_PAD;

    private const int LIST_BOX_X = COUNT_BOX_X + COUNT_BOX_W + 6;
    private const int PANEL_WIDTH = 440;
    private const int PANEL_HEIGHT = BOTTOM_Y + BOTTOM_H;
    private const int LIST_BOX_W = PANEL_WIDTH - PADDING - LIST_BOX_X;

    private const int LIST_INNER_PAD = 3;
    private const int LIST_ROW_X = LIST_BOX_X + LIST_INNER_PAD;
    private const int SCROLLBAR_X = LIST_BOX_X + LIST_BOX_W - SCROLLBAR_W - 1;
    private const int LIST_ROW_W = SCROLLBAR_X - LIST_ROW_X - 2;
    private const int LIST_ROW_H = 13;

    //rows tile the list box's inner height (box height minus its 1px top/bottom borders); the leftover is split evenly
    //above/below so the row block is centered and never overflows the container.
    private const int VISIBLE_ROWS = (BODY_H - 2) / LIST_ROW_H;
    private const int LIST_TOP = BODY_Y + (BODY_H - VISIBLE_ROWS * LIST_ROW_H) / 2;
    private const int ICON_SIZE = 11;
    private const int TOTAL_LABEL_W = 120;

    //── colors (frame/tab/row palette shared via DialogPalette; CountIdleColor is list-specific) ──
    private static readonly Color FrameFill = DialogPalette.FrameFill;
    private static readonly Color FrameBorder = DialogPalette.FrameBorder;
    private static readonly Color DividerColor = DialogPalette.Divider;
    private static readonly Color TitleColor = DialogPalette.Title;
    private static readonly Color RowHoverFill = DialogPalette.RowHoverFill;
    private static readonly Color SelectedTextColor = DialogPalette.SelectedText;
    private static readonly Color CountIdleColor = new(150, 152, 164);
    private static readonly Color TabIdleFill = DialogPalette.TabIdleFill;
    private static readonly Color TabSelectedFill = DialogPalette.TabSelectedFill;
    private static readonly Color TabIdleBorder = DialogPalette.TabIdleBorder;
    private static readonly Color TabSelectedBorder = DialogPalette.TabSelectedBorder;

    private readonly FilterButton[] Filters = new FilterButton[TAB_COUNT];

    //per-filter counts are formatted + measured once per snapshot (in UpdateCounts), not per frame; DrawCounts is
    //then pure layout + colour. Both regular and bold widths are cached because the selected count is drawn bold and
    //bold glyphs are wider — the two must right-align to the same edge.
    private readonly string[] CountText = new string[TAB_COUNT];
    private readonly int[] CountWidth = new int[TAB_COUNT];
    private readonly int[] CountWidthBold = new int[TAB_COUNT];
    private readonly NameRow[] Rows = new NameRow[VISIBLE_ROWS];
    private readonly Texture2D?[] StatusIcons = new Texture2D?[STATUS_ICON_COUNT];

    private readonly UILabel TotalNumLabel;

    //the visible name rows are a fixed window over the filtered list; ScrollModel owns the clamp/offset math and a
    //ScrollBarBinder (kept alive by the model/bar delegate lists) keeps the bar thumb in lockstep — Offset is the
    //first visible index.
    private readonly ScrollModel ListScroll = new();

    //the filtered view is rebuilt into this buffer on every filter/refresh, so no per-frame allocation.
    private readonly List<WorldListEntry> FilterBuffer = [];

    private IReadOnlyList<WorldListEntry> AllEntries = [];
    private IReadOnlyList<WorldListEntry> FilteredEntries = [];
    private readonly HashSet<string> FamilyNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> FriendNames = new(StringComparer.OrdinalIgnoreCase);
    private int ActiveFilter;
    private ushort TotalOnline;

    private static string PlayerName => WorldState.PlayerName;

    public event CloseHandler? OnClose;
    public event WhisperRequestedHandler? OnWhisperRequested;

    public WorldListControl(string serverName)
    {
        Name = "WorldList";
        Visible = false;
        UsesControlStack = true;
        Width = PANEL_WIDTH;
        Height = PANEL_HEIGHT;
        X = (ChaosGame.VIRTUAL_WIDTH - PANEL_WIDTH) / 2;
        Y = (ChaosGame.VIRTUAL_HEIGHT - PANEL_HEIGHT) / 2;
        BackgroundColor = FrameFill;
        BorderColor = FrameBorder;

        //social-status icons from _nemots.spf (frame 0 of each group) — cache-owned by UiRenderer, so not disposed here
        LoadStatusIcons();
        Array.Fill(CountText, "0");

        var titleLabel = new UILabel
        {
            X = 0,
            Y = (TITLE_H - TextRenderer.CHAR_HEIGHT) / 2,
            Width = PANEL_WIDTH,
            Height = TextRenderer.CHAR_HEIGHT,
            HorizontalAlignment = HorizontalAlignment.Center,
            ForegroundColor = TitleColor,
            Text = "Aislings",
            IsHitTestVisible = false
        };
        AddChild(titleLabel);

        //── total readout ──
        var totalY = TITLE_H + (TOTAL_H - TextRenderer.CHAR_HEIGHT) / 2;

        var totalLabel = new UILabel
        {
            X = PADDING,
            Y = totalY,
            Width = TOTAL_LABEL_W,
            Height = TextRenderer.CHAR_HEIGHT,
            ForegroundColor = LegendColors.White,
            Text = "World",
            IsHitTestVisible = false
        };
        AddChild(totalLabel);

        //right-aligned to the count column's right edge so the world total lines up vertically with the per-filter counts
        TotalNumLabel = new UILabel
        {
            X = PADDING,
            Y = totalY,
            Width = COUNT_RIGHT_EDGE - PADDING,
            Height = TextRenderer.CHAR_HEIGHT,
            HorizontalAlignment = HorizontalAlignment.Right,
            ForegroundColor = LegendColors.White,
            Text = "0",
            IsHitTestVisible = false
        };
        AddChild(TotalNumLabel);

        //── filter column ── (counts are drawn in Draw, right-aligned inside the count box)
        var filterLabels = ResolveFilterLabels(serverName);

        for (var i = 0; i < TAB_COUNT; i++)
        {
            var index = i;
            var rowY = BODY_Y + i * FILTER_STRIDE + (FILTER_STRIDE - FILTER_H) / 2;

            var filter = new FilterButton(filterLabels[i], FILTER_W, FILTER_H)
            {
                X = BUTTON_X,
                Y = rowY
            };

            filter.Clicked += () => SelectFilter(index);
            Filters[i] = filter;
            AddChild(filter);
        }

        Filters[0].IsSelected = true;

        //── name list ──
        for (var i = 0; i < VISIBLE_ROWS; i++)
        {
            var row = new NameRow(LIST_ROW_W)
            {
                X = LIST_ROW_X,
                Y = LIST_TOP + i * LIST_ROW_H,
                Visible = false
            };

            row.OnWhisper += name => OnWhisperRequested?.Invoke(name);
            Rows[i] = row;
            AddChild(row);
        }

        var scrollBar = new ScrollBarControl
        {
            Name = "WorldListScroll",
            X = SCROLLBAR_X,
            Y = BODY_Y + 1,
            Height = BODY_H - 2
        };

        //the binder couples model <-> bar (thumb metrics + value) and stays alive via their delegate lists; Changed
        //repaints the visible window (covers the scrollbar and wheel paths). Filter/refresh paths call UpdateRows
        //directly since a metrics-only change that leaves the offset at 0 raises no Changed.
        _ = new ScrollBarBinder(ListScroll, scrollBar);
        ListScroll.Changed += _ => UpdateRows();
        AddChild(scrollBar);

        //── bottom bar ──
        const int closeW = 52;

        var closeButton = new TextButton("Close", closeW, 18)
        {
            X = PANEL_WIDTH - PADDING - closeW,
            Y = BOTTOM_Y + (BOTTOM_H - 18) / 2
        };
        closeButton.Clicked += Close;
        AddChild(closeButton);

        WorldState.WorldList.Changed += OnWorldListChanged;
    }

    //Class-filter labels. Reuses the same server string the window title bar keys off
    //(ConnectionManager.ServerName): Hybrasyl servers use the modern Archon/Animist names for the Priest/Monk paths;
    //legacy Dark Ages servers keep Priest/Monk. Display-only — the underlying BaseClass filter in Matches() is
    //unchanged regardless of label. When a structured server-type flag replaces the name string, swap the predicate.
    private static string[] ResolveFilterLabels(string serverName)
    {
        var hybrasyl = serverName.ContainsI("Hybrasyl");

        return
        [
            "Server", "Master", "Warrior", "Rogue", "Wizard", hybrasyl ? "Archon" : "Priest", hybrasyl ? "Animist" : "Monk",
            "Peasant", "Guild"
        ];
    }

    private void LoadStatusIcons()
    {
        var cache = UiRenderer.Instance!;

        for (var i = 0; i < STATUS_ICON_COUNT; i++)
            StatusIcons[i] = cache.GetSpfTexture("_nemots.spf", i);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);

        if (!Visible)
            return;

        //horizontal dividers framing the title / total / body / bottom bands (drawn in the gaps, over the fill)
        DrawDivider(spriteBatch, TITLE_H);
        DrawDivider(spriteBatch, BODY_Y);
        DrawDivider(spriteBatch, BOTTOM_Y);

        //no-fill boxes give the count column and the name list their own frames — the gap between them reads as the
        //vertical separator between counts and names.
        DrawBorderClipped(spriteBatch, new Rectangle(ScreenX + COUNT_BOX_X, ScreenY + BODY_Y, COUNT_BOX_W, BODY_H), DividerColor);
        DrawBorderClipped(spriteBatch, new Rectangle(ScreenX + LIST_BOX_X, ScreenY + BODY_Y, LIST_BOX_W, BODY_H), DividerColor);

        DrawCounts(spriteBatch);
    }

    private void DrawDivider(SpriteBatch spriteBatch, int localY)
        => DrawRectClipped(spriteBatch, new Rectangle(ScreenX + PADDING, ScreenY + localY, Width - 2 * PADDING, 1), DividerColor);

    //Counts are drawn (not UILabels) so the selected filter's count can be emphasized: real bold (the font stack now
    //ships a bold face) plus the brighter selected colour; the others are a dimmer regular. Right-aligned to the count
    //column edge using the per-style width cached in UpdateCounts, so this is pure per-frame layout.
    private void DrawCounts(SpriteBatch spriteBatch)
    {
        var rightEdge = ScreenX + COUNT_RIGHT_EDGE;
        var baseY = ScreenY + BODY_Y + (FILTER_STRIDE - TextRenderer.CHAR_HEIGHT) / 2;

        for (var i = 0; i < TAB_COUNT; i++)
        {
            var selected = i == ActiveFilter;
            var x = rightEdge - (selected ? CountWidthBold[i] : CountWidth[i]);
            var y = baseY + i * FILTER_STRIDE;

            if (selected)
                TextRenderer.DrawText(spriteBatch, new Vector2(x, y), CountText[i], SelectedTextColor, FontStyle.Bold);
            else
                TextRenderer.DrawText(spriteBatch, new Vector2(x, y), CountText[i], CountIdleColor);
        }
    }

    /// <summary>
    ///     Populates and shows the panel from the latest world-list snapshot.
    /// </summary>
    public void Show(IReadOnlyList<WorldListEntry> entries, ushort totalOnline)
    {
        AllEntries = entries;
        TotalOnline = totalOnline;
        SelectFilterInternal(0);

        ApplyFilter();
        UpdateCounts();
        AutoScrollToSelf();
        UpdateRows();

        if (!Visible)
        {
            InputDispatcher.Instance?.PushControl(this);
            Visible = true;
        }
    }

    public void Hide() => Close();

    public void Close()
    {
        if (!Visible)
            return;

        Visible = false;
        InputDispatcher.Instance?.RemoveControl(this);
        OnClose?.Invoke();
    }

    private void OnWorldListChanged() => Show(WorldState.WorldList.Entries, WorldState.WorldList.TotalOnline);

    private void SelectFilter(int index)
    {
        if (index == ActiveFilter)
            return;

        SelectFilterInternal(index);
        ApplyFilter();
        ListScroll.ScrollToStart();
        UpdateRows();
    }

    private void SelectFilterInternal(int index)
    {
        Filters[ActiveFilter].IsSelected = false;
        ActiveFilter = index;
        Filters[ActiveFilter].IsSelected = true;
    }

    private void ApplyFilter()
    {
        if (ActiveFilter == 0)
            FilteredEntries = AllEntries;
        else
        {
            FilterBuffer.Clear();

            foreach (var e in AllEntries)
                if (Matches(ActiveFilter, e))
                    FilterBuffer.Add(e);

            FilteredEntries = FilterBuffer;
        }

        ListScroll.SetMetrics(FilteredEntries.Count, VISIBLE_ROWS);
    }

    private static bool Matches(int filter, WorldListEntry e)
        => filter switch
        {
            1 => e.IsMaster,
            2 => e.BaseClass == BaseClass.Warrior,
            3 => e.BaseClass == BaseClass.Rogue,
            4 => e.BaseClass == BaseClass.Wizard,
            5 => e.BaseClass == BaseClass.Priest,
            6 => e.BaseClass == BaseClass.Monk,
            7 => e.BaseClass == BaseClass.Peasant,
            8 => e.IsGuilded,
            _ => false
        };

    private void UpdateRows()
    {
        for (var i = 0; i < Rows.Length; i++)
        {
            var position = ListScroll.Offset + i;
            var row = Rows[i];

            if (position < FilteredEntries.Count)
            {
                var entry = FilteredEntries[position];
                row.SetEntry(entry, StatusIconFor(entry), NameColorFor(entry));
                row.Visible = true;
            } else
            {
                row.Clear();
                row.Visible = false;
            }
        }
    }

    //Single O(N) pass tallies every slot at once (master/guild are cross-cutting; the rest key off BaseClass), then
    //formats + measures each count. Strings + widths are cached here for the per-frame DrawCounts. Mirrors the slot
    //numbering in Matches.
    private void UpdateCounts()
    {
        TotalNumLabel.Text = TotalOnline.ToString();

        var counts = new int[TAB_COUNT];
        counts[0] = AllEntries.Count;

        foreach (var entry in AllEntries)
        {
            if (entry.IsMaster)
                counts[1]++;

            if (entry.IsGuilded)
                counts[8]++;

            switch (entry.BaseClass)
            {
                case BaseClass.Warrior:
                    counts[2]++;

                    break;
                case BaseClass.Rogue:
                    counts[3]++;

                    break;
                case BaseClass.Wizard:
                    counts[4]++;

                    break;
                case BaseClass.Priest:
                    counts[5]++;

                    break;
                case BaseClass.Monk:
                    counts[6]++;

                    break;
                case BaseClass.Peasant:
                    counts[7]++;

                    break;
            }
        }

        for (var i = 0; i < TAB_COUNT; i++)
        {
            CountText[i] = counts[i].ToString();
            CountWidth[i] = TextRenderer.MeasureWidth(CountText[i]);
            CountWidthBold[i] = TextRenderer.MeasureWidth(CountText[i], FontStyle.Bold);
        }
    }

    private void AutoScrollToSelf()
    {
        if (PlayerName.Length == 0)
            return;

        for (var i = 0; i < FilteredEntries.Count; i++)
            if (FilteredEntries[i].Name.EqualsI(PlayerName))
            {
                ListScroll.ScrollTo(i - VISIBLE_ROWS / 2);

                return;
            }
    }

    private Color NameColorFor(WorldListEntry entry)
        => FamilyNames.Contains(entry.Name)
            ? LegendColors.HotPink
            : FriendNames.Contains(entry.Name)
                ? LegendColors.Lime
                : MapWorldListColor(entry.Color);

    private Texture2D? StatusIconFor(WorldListEntry entry)
    {
        var idx = (int)entry.SocialStatus;

        return (idx >= 0) && (idx < StatusIcons.Length) ? StatusIcons[idx] : null;
    }

    private static Color MapWorldListColor(WorldListColor color)
    {
        if (color == WorldListColor.Invisble)
            return Color.Transparent;

        if (color == WorldListColor.White)
            return LegendColors.White;

        return LegendColors.Get((int)color);
    }

    public void SetFamilyNames(FamilyList? family)
    {
        FamilyNames.Clear();

        if (family is null)
            return;

        foreach (var name in new[]
                 {
                     family.Mother,
                     family.Father,
                     family.Son1,
                     family.Son2,
                     family.Brother1,
                     family.Brother2,
                     family.Brother3,
                     family.Brother4,
                     family.Brother5,
                     family.Brother6
                 })
            if (!string.IsNullOrWhiteSpace(name))
                FamilyNames.Add(name);
    }

    public void SetFriendNames(IReadOnlyList<string> friends)
    {
        FriendNames.Clear();

        foreach (var name in friends)
            if (!string.IsNullOrWhiteSpace(name))
                FriendNames.Add(name);
    }

    public void SetViewportBounds(Rectangle viewport)
    {
        X = viewport.X + (viewport.Width - Width) / 2;
        Y = viewport.Y + (viewport.Height - Height) / 2;
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        ListScroll.WheelBy(e.Delta);
        e.Handled = true;
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    public override void Dispose()
    {
        WorldState.WorldList.Changed -= OnWorldListChanged;

        base.Dispose();
    }

    /// <summary>A single name row: social-status icon + name (left) + right-aligned title, with hover fill.</summary>
    private sealed class NameRow : UIPanel
    {
        private const int NAME_W = 130;

        private readonly UIImage Icon;
        private readonly UILabel NameLabel;
        private readonly UILabel TitleLabel;
        private bool Hovered;

        public string? EntryName { get; private set; }

        public event WhisperRequestedHandler? OnWhisper;

        public NameRow(int width)
        {
            Width = width;
            Height = LIST_ROW_H;

            //display-only children so the row stays the deepest hit-test target (reliable OnMouseLeave for hover fill)
            Icon = new UIImage
            {
                X = 0,
                Y = (LIST_ROW_H - ICON_SIZE) / 2,
                Width = ICON_SIZE,
                Height = ICON_SIZE,
                IsHitTestVisible = false
            };

            NameLabel = new UILabel
            {
                X = ICON_SIZE + 3,
                Y = 0,
                Width = NAME_W,
                Height = LIST_ROW_H,
                VerticalAlignment = VerticalAlignment.Center,
                ForegroundColor = LegendColors.White,
                IsHitTestVisible = false
            };

            var titleX = ICON_SIZE + 3 + NAME_W;

            TitleLabel = new UILabel
            {
                X = titleX,
                Y = 0,
                Width = width - titleX,
                Height = LIST_ROW_H,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                ForegroundColor = LegendColors.White,
                IsHitTestVisible = false
            };

            AddChild(Icon);
            AddChild(NameLabel);
            AddChild(TitleLabel);
        }

        public void SetEntry(WorldListEntry entry, Texture2D? statusIcon, Color nameColor)
        {
            Icon.Texture = statusIcon;
            Icon.Visible = statusIcon is not null;
            NameLabel.ForegroundColor = nameColor;
            NameLabel.Text = entry.Name;
            TitleLabel.Text = entry.Title ?? string.Empty;
            EntryName = entry.Name;
        }

        public void Clear()
        {
            Icon.Texture = null;
            Icon.Visible = false;
            NameLabel.Text = string.Empty;
            TitleLabel.Text = string.Empty;
            EntryName = null;
            Hovered = false;
            RefreshFill();
        }

        private void RefreshFill() => BackgroundColor = Hovered ? RowHoverFill : null;

        public override void OnMouseEnter()
        {
            Hovered = true;
            RefreshFill();
        }

        public override void OnMouseLeave()
        {
            Hovered = false;
            RefreshFill();
        }

        public override void OnDoubleClick(DoubleClickEvent e)
        {
            if ((e.Button == MouseButton.Left) && EntryName is not null)
            {
                OnWhisper?.Invoke(EntryName);
                e.Handled = true;
            }
        }
    }

    /// <summary>A class-filter button: centered label with an idle/selected frame.</summary>
    private sealed class FilterButton : UIPanel
    {
        private readonly UILabel Label;

        public event ClickedHandler? Clicked;

        public bool IsSelected
        {
            get;
            set
            {
                field = value;
                BackgroundColor = value ? TabSelectedFill : TabIdleFill;
                BorderColor = value ? TabSelectedBorder : TabIdleBorder;
                Label.ForegroundColor = value ? SelectedTextColor : LegendColors.White;
            }
        }

        public FilterButton(string text, int width, int height)
        {
            Width = width;
            Height = height;
            BackgroundColor = TabIdleFill;
            BorderColor = TabIdleBorder;

            Label = new UILabel
            {
                X = 0,
                Y = 0,
                Width = width,
                Height = height,
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ShrinkToFit = false,
                ForegroundColor = LegendColors.White,
                IsHitTestVisible = false
            };

            AddChild(Label);
        }

        public override void OnClick(ClickEvent e)
        {
            if (e.Button != MouseButton.Left)
                return;

            Clicked?.Invoke();
            e.Handled = true;
        }
    }
}
