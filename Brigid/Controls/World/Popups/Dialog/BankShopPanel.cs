#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Data;
using Brigid.Definitions;
using Brigid.Networking;
using Brigid.Rendering;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Chaos.Extensions.Common;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Dialog;

/// <summary>
///     From-scratch, asset-free merchant/bank browser for the <see cref="MenuType.ShowItems" /> menu. Draws its whole
///     frame with primitives (no legacy SPF/EPF prefab), suppresses the NPC portrait, and lays out a centered window:
///     centered title, a vertical strip of text category tabs on the left (paginated by a tab &lt;/&gt; pager), item
///     rows (icon + name + right-aligned count) on the right (paginated by a separate item &lt;/&gt; pager), and a
///     bottom bar with the gold readout and an OK button. Owned by <see cref="NpcSessionControl" />, which reuses the
///     retail pacing latch and response dispatch. Hover details are drawn by <see cref="BankItemTooltip" /> (Phase 3).
/// </summary>
public sealed class BankShopPanel : UIPanel
{
    //── layout ──
    private const int PADDING = 6;
    private const int TITLE_H = 20;
    private const int ROW_H = 34;
    private const int VISIBLE_ROWS = 8;
    private const int BOTTOM_H = 30;
    private const int TAB_W = 78;
    private const int TAB_H = 30;
    private const int TAB_MAX_CHARS = (TAB_W - 6) / TextRenderer.CHAR_WIDTH;
    private const int ICON_SIZE = 32;
    private const int PANEL_WIDTH = 410;
    private const int PANEL_HEIGHT = TITLE_H + VISIBLE_ROWS * ROW_H + BOTTOM_H;
    private const int ITEM_X = PADDING + TAB_W + PADDING;
    private const int ITEM_W = PANEL_WIDTH - ITEM_X - PADDING;
    private const int ROWS_Y = TITLE_H;
    private const int BOTTOM_Y = TITLE_H + VISIBLE_ROWS * ROW_H;
    private const int COUNT_W = 56;
    private const int MAX_NAME_CHARS = 24;
    private const int MAX_VISIBLE_TABS = VISIBLE_ROWS;

    //── colors ──
    private static readonly Color TooltipFill = new(6, 8, 14, 245);
    private static readonly Color TooltipBorder = new(150, 126, 78);
    private static readonly Color TooltipHeading = new(226, 202, 120);
    private const int TOOLTIP_WIDTH = 190;
    private const int TOOLTIP_PAD = 6;

    private static readonly Color FrameFill = new(6, 8, 14, 240);
    private static readonly Color FrameBorder = new(150, 126, 78);
    private static readonly Color DividerColor = new(64, 56, 36);
    private static readonly Color TitleColor = new(226, 202, 120);
    private static readonly Color RowHoverFill = new(38, 40, 48);
    private static readonly Color RowSelectedFill = new(74, 58, 22);
    private static readonly Color SelectedTextColor = new(255, 208, 96);
    private static readonly Color TabIdleFill = new(24, 26, 34);
    private static readonly Color TabSelectedFill = new(74, 58, 22);
    private static readonly Color TabIdleBorder = new(80, 70, 44);
    private static readonly Color TabSelectedBorder = new(210, 176, 96);

    private readonly List<string> Categories = [];
    private readonly List<MerchantEntry> Entries = [];
    private readonly List<int> FilteredIndices = [];

    private readonly ItemRow[] Rows;
    private readonly CategoryTab[] Tabs;
    private readonly UILabel TitleLabel;
    private readonly UILabel GoldLabel;
    private readonly UILabel PageLabel;
    private readonly TextButton TabPrevButton;
    private readonly TextButton TabNextButton;
    private readonly TextButton ItemPrevButton;
    private readonly TextButton ItemNextButton;
    private readonly TextButton OkButton;

    //first visible filtered-item index; the row list is a VISIBLE_ROWS window starting here (mouse-wheel + < > paging)
    private int ItemScrollOffset;
    private int SelectedCategoryIndex;
    private int SelectedIndex = -1;
    private int TabWindowStart;

    private int MaxItemScroll => Math.Max(0, FilteredIndices.Count - VISIBLE_ROWS);

    //hover tooltip state — HoveredSlot is the visible row slot, HoveredIndex the absolute entry it maps to (-1 = none)
    private int HoveredSlot = -1;
    private int HoveredIndex = -1;

    //category-tab hover: HoveredCategory is the full name to show (set only when the tab label was truncated)
    private int HoveredTabSlot = -1;
    private string? HoveredCategory;

    //per-NPC memory: restore last-used tab + scroll offset when the same NPC re-opens the shop
    private readonly Dictionary<string, (string Category, int Scroll)> NpcMemory = new(StringComparer.OrdinalIgnoreCase);
    private string? CurrentNpcName;

    public event ItemSelectedHandler? OnItemSelected;
    public event CloseHandler? OnClose;

    public BankShopPanel()
    {
        Name = "BankShop";
        Visible = false;
        Width = PANEL_WIDTH;
        Height = PANEL_HEIGHT;
        X = (ChaosGame.VIRTUAL_WIDTH - PANEL_WIDTH) / 2;
        Y = (ChaosGame.VIRTUAL_HEIGHT - PANEL_HEIGHT) / 2;
        BackgroundColor = FrameFill;
        BorderColor = FrameBorder;

        //centered (unlike the bottom-anchored/right-aligned legacy dialog sub-panels) because the suppressed chrome
        //frees the whole screen — don't "fix" this to the sibling convention.

        TitleLabel = new UILabel
        {
            X = 0,
            Y = (TITLE_H - TextRenderer.CHAR_HEIGHT) / 2,
            Width = PANEL_WIDTH,
            Height = TextRenderer.CHAR_HEIGHT,
            HorizontalAlignment = HorizontalAlignment.Center,
            ForegroundColor = TitleColor,
            IsHitTestVisible = false
        };
        AddChild(TitleLabel);

        //category tab strip (vertical, aligned 1:1 with the item rows)
        Tabs = new CategoryTab[MAX_VISIBLE_TABS];

        for (var i = 0; i < MAX_VISIBLE_TABS; i++)
        {
            var slot = i;

            var tab = new CategoryTab(TAB_W, TAB_H)
            {
                TabIndex = i,
                X = PADDING,
                Y = ROWS_Y + i * ROW_H + (ROW_H - TAB_H) / 2,
                Width = TAB_W,
                Height = TAB_H,
                Visible = false
            };

            tab.Clicked += () => HandleTabClick(slot);
            Tabs[i] = tab;
            AddChild(tab);
        }

        //item rows
        Rows = new ItemRow[VISIBLE_ROWS];

        for (var i = 0; i < VISIBLE_ROWS; i++)
        {
            var slot = i;

            var row = new ItemRow(ITEM_W)
            {
                RowIndex = i,
                X = ITEM_X,
                Y = ROWS_Y + i * ROW_H,
                Width = ITEM_W,
                Height = ROW_H,
                Visible = false
            };

            row.Clicked += () => HandleRowClick(slot);
            row.Confirmed += () => HandleRowConfirm(slot);
            Rows[i] = row;
            AddChild(row);
        }

        //── bottom bar ──
        var btnY = BOTTOM_Y + (BOTTOM_H - 18) / 2;

        TabPrevButton = new TextButton("<", 16, 18)
        {
            X = PADDING,
            Y = btnY
        };
        TabPrevButton.Clicked += ScrollTabsPrev;
        AddChild(TabPrevButton);

        TabNextButton = new TextButton(">", 16, 18)
        {
            X = PADDING + 18,
            Y = btnY
        };
        TabNextButton.Clicked += ScrollTabsNext;
        AddChild(TabNextButton);

        GoldLabel = new UILabel
        {
            X = PADDING + 40,
            Y = BOTTOM_Y + (BOTTOM_H - TextRenderer.CHAR_HEIGHT) / 2,
            Width = 150,
            Height = TextRenderer.CHAR_HEIGHT,
            ForegroundColor = LegendColors.Gold,
            IsHitTestVisible = false
        };
        AddChild(GoldLabel);

        const int okW = 40;
        var itemNextX = PANEL_WIDTH - PADDING - okW - 8 - 16;
        var itemPrevX = itemNextX - 18;

        PageLabel = new UILabel
        {
            X = itemPrevX - 76,
            Y = BOTTOM_Y + (BOTTOM_H - TextRenderer.CHAR_HEIGHT) / 2,
            Width = 72,
            Height = TextRenderer.CHAR_HEIGHT,
            HorizontalAlignment = HorizontalAlignment.Right,
            ForegroundColor = LegendColors.White,
            ShrinkToFit = false,
            IsHitTestVisible = false
        };
        AddChild(PageLabel);

        ItemPrevButton = new TextButton("<", 16, 18)
        {
            X = itemPrevX,
            Y = btnY
        };
        ItemPrevButton.Clicked += PrevItemPage;
        AddChild(ItemPrevButton);

        ItemNextButton = new TextButton(">", 16, 18)
        {
            X = itemNextX,
            Y = btnY
        };
        ItemNextButton.Clicked += NextItemPage;
        AddChild(ItemNextButton);

        OkButton = new TextButton("OK", okW, 18)
        {
            X = PANEL_WIDTH - PADDING - okW,
            Y = btnY
        };
        OkButton.Clicked += HandleOk;
        AddChild(OkButton);
    }

    /// <summary>
    ///     Populates and shows the panel for a <see cref="MenuType.ShowItems" /> menu packet.
    /// </summary>
    public void ShowMerchant(NpcMenuPacket pkt)
    {
        CurrentNpcName = pkt.Name;
        ClearEntries();
        PopulateItems(pkt);
        BuildCategories();

        //restore last-used tab + scroll offset for this NPC, if the saved category still exists
        SelectedCategoryIndex = 0;
        var savedScroll = 0;

        if (!string.IsNullOrEmpty(CurrentNpcName) && NpcMemory.TryGetValue(CurrentNpcName, out var saved))
            for (var i = 0; i < Categories.Count; i++)
                if (Categories[i].EqualsI(saved.Category))
                {
                    SelectedCategoryIndex = i;
                    savedScroll = saved.Scroll;

                    break;
                }

        //scroll the tab window so the restored tab is visible
        TabWindowStart = 0;

        if (SelectedCategoryIndex >= MAX_VISIBLE_TABS)
            TabWindowStart = Math.Min(SelectedCategoryIndex - MAX_VISIBLE_TABS + 1, Math.Max(0, Categories.Count - MAX_VISIBLE_TABS));

        BuildFilteredIndices();
        ItemScrollOffset = Math.Clamp(savedScroll, 0, MaxItemScroll);

        TitleLabel.Text = string.IsNullOrEmpty(pkt.Name) ? "Shop" : pkt.Name;
        GoldLabel.Text = $"Gold: {WorldState.Inventory.Gold:N0}";

        UpdateTabDisplay();
        UpdateItemDisplay();

        Visible = true;
    }

    public void Hide()
    {
        Visible = false;
        ClearEntries();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        base.Draw(spriteBatch);

        if (!Visible)
            return;

        //hover tooltips are drawn unclipped, on top, following the cursor. A tab and a row can't be hovered at once,
        //but item detail takes precedence if both are somehow set.
        if ((HoveredIndex >= 0) && (HoveredIndex < Entries.Count))
            DrawTooltip(spriteBatch, Entries[HoveredIndex]);
        else if (HoveredCategory is { } category)
            DrawLineTooltip(spriteBatch, category);
    }

    private static void DrawLineTooltip(SpriteBatch spriteBatch, string text)
    {
        var width = TextRenderer.MeasureWidth(text) + 2 * TOOLTIP_PAD;
        var height = TextRenderer.CHAR_HEIGHT + 2 * TOOLTIP_PAD;

        var rightX = InputBuffer.MouseX + 15;
        var x = (rightX + width) <= ChaosGame.VIRTUAL_WIDTH ? rightX : InputBuffer.MouseX - width;
        var y = Math.Clamp(InputBuffer.MouseY + 15, 0, ChaosGame.VIRTUAL_HEIGHT - height);

        DrawBorderedRect(spriteBatch, new Rectangle(x, y, width, height), TooltipFill, TooltipBorder);
        TextRenderer.DrawText(spriteBatch, new Vector2(x + TOOLTIP_PAD, y + TOOLTIP_PAD), text, LegendColors.White);
    }

    private static void DrawTooltip(SpriteBatch spriteBatch, MerchantEntry entry)
    {
        var lines = new List<(string Text, Color Color)>();

        //item name heading first, wrapped to the box width (always shown, even for items with no metadata)
        foreach (var wrapped in TextRenderer.WrapText(entry.Name, TOOLTIP_WIDTH - 2 * TOOLTIP_PAD))
            lines.Add((wrapped, TooltipHeading));

        if (entry.Class is { } cls)
        {
            var name = (BaseClass)cls == BaseClass.Peasant ? "all class" : ((BaseClass)cls).ToString();
            lines.Add(($"Class: {name}", LegendColors.White));
        }

        if (entry.Level is { } lvl)
            lines.Add(($"Level: {(lvl == 0 ? "no limit" : lvl.ToString())}", LegendColors.White));

        if (entry.Weight is { } weight)
            lines.Add(($"Weight: {weight}", LegendColors.White));

        var hasDescription = !string.IsNullOrWhiteSpace(entry.Description);

        if (hasDescription)
        {
            lines.Add(("Description:", TooltipHeading));

            foreach (var wrapped in TextRenderer.WrapText(entry.Description, TOOLTIP_WIDTH - 2 * TOOLTIP_PAD))
                lines.Add((wrapped, LegendColors.White));
        }

        //nothing worth showing (item had no metadata) — skip the tooltip entirely
        if (lines.Count == 0)
            return;

        var height = 2 * TOOLTIP_PAD + lines.Count * TextRenderer.CHAR_HEIGHT;

        var rightX = InputBuffer.MouseX + 15;
        var x = (rightX + TOOLTIP_WIDTH) <= ChaosGame.VIRTUAL_WIDTH ? rightX : InputBuffer.MouseX - TOOLTIP_WIDTH;
        var y = Math.Clamp(InputBuffer.MouseY + 15, 0, ChaosGame.VIRTUAL_HEIGHT - height);

        DrawBorderedRect(spriteBatch, new Rectangle(x, y, TOOLTIP_WIDTH, height), TooltipFill, TooltipBorder);

        var textY = y + TOOLTIP_PAD;

        foreach (var (text, color) in lines)
        {
            TextRenderer.DrawText(spriteBatch, new Vector2(x + TOOLTIP_PAD, textY), text, color);
            textY += TextRenderer.CHAR_HEIGHT;
        }
    }

    /// <summary>Returns the name of the entry at the given absolute index, or null if out of range.</summary>
    public string? GetEntryName(int index) => (index >= 0) && (index < Entries.Count) ? Entries[index].Name : null;

    /// <summary>Returns the slot byte for the entry at the given absolute index, or null if out of range.</summary>
    public byte? GetEntrySlot(int index) => (index >= 0) && (index < Entries.Count) ? Entries[index].Slot : null;

    private void ClearEntries()
    {
        Entries.Clear();
        FilteredIndices.Clear();
        Categories.Clear();
        SelectedIndex = -1;
        SelectedCategoryIndex = 0;
        TabWindowStart = 0;
        ItemScrollOffset = 0;
        ClearHover();

        foreach (var row in Rows)
        {
            row.ClearEntry();
            row.Visible = false;
        }

        foreach (var tab in Tabs)
            tab.Visible = false;
    }

    private void PopulateItems(NpcMenuPacket pkt)
    {
        if (pkt.Menu is not ItemListMenu menu)
        {
            NoticeDebugLog.Write(
                $"[BankShop] ShowItems body is {pkt.Menu?.GetType().Name ?? "null"} (expected ItemListMenu); not displayed");

            return;
        }

        var items = menu.Items;
        var names = new string[items.Count];

        for (var i = 0; i < items.Count; i++)
            names[i] = items[i].Name;

        var metadata = DataContext.MetaFiles.GetItemMetadata(names);
        byte slot = 1;

        foreach (var item in items)
        {
            var icon = UiRenderer.Instance!.GetItemIcon(item.Sprite, (DisplayColor)item.Color);
            metadata.TryGetValue(item.Name, out var meta);

            //the wire carries this numeric field as NpcMenuItem.Cost; a bank repurposes it as the stored stack
            //quantity, which is what the right-hand column shows (see the "count" column in the mock).
            Entries.Add(
                new MerchantEntry(
                    item.Name,
                    icon,
                    (int)item.Cost,
                    slot++,
                    meta?.Category ?? string.Empty,
                    meta?.Description ?? string.Empty,
                    meta?.Level,
                    meta?.Class,
                    meta?.Weight));
        }
    }

    private void BuildCategories()
    {
        Categories.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Entries)
        {
            var category = entry.Category.Length > 0 ? entry.Category : "Other";

            if (seen.Add(category))
                Categories.Add(category);
        }

        //sort alphabetically, "Other" last
        Categories.Sort((a, b) =>
        {
            var aOther = a.EqualsI("Other");
            var bOther = b.EqualsI("Other");

            if (aOther && !bOther)
                return 1;

            if (!aOther && bOther)
                return -1;

            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        });
    }

    private void BuildFilteredIndices()
    {
        FilteredIndices.Clear();

        if (Categories.Count == 0)
        {
            for (var i = 0; i < Entries.Count; i++)
                FilteredIndices.Add(i);

            return;
        }

        var selected = Categories[SelectedCategoryIndex];
        var isOther = selected.EqualsI("Other");

        for (var i = 0; i < Entries.Count; i++)
        {
            var cat = Entries[i].Category;

            if (isOther && (cat.Length == 0))
                FilteredIndices.Add(i);
            else if (cat.EqualsI(selected))
                FilteredIndices.Add(i);
        }
    }

    private void UpdateTabDisplay()
    {
        var visibleCount = Math.Min(MAX_VISIBLE_TABS, Categories.Count - TabWindowStart);

        for (var i = 0; i < MAX_VISIBLE_TABS; i++)
        {
            var tab = Tabs[i];
            var categoryIndex = TabWindowStart + i;

            if ((i < visibleCount) && (categoryIndex < Categories.Count))
            {
                var category = Categories[categoryIndex];
                var label = category.Length > TAB_MAX_CHARS ? category[..(TAB_MAX_CHARS - 1)] + "…" : category;
                tab.SetCategory(category, label);
                tab.IsSelected = categoryIndex == SelectedCategoryIndex;
                tab.Visible = true;
            } else
                tab.Visible = false;
        }

        TabPrevButton.SetEnabled(TabWindowStart > 0);
        TabNextButton.SetEnabled((TabWindowStart + MAX_VISIBLE_TABS) < Categories.Count);

        //the tab under the cursor may now show a different category after a wheel/page scroll — refresh its tooltip
        if (HoveredTabSlot >= 0)
            NotifyTabHover(HoveredTabSlot);
    }

    private void UpdateItemDisplay()
    {
        for (var i = 0; i < Rows.Length; i++)
        {
            var filteredPosition = ItemScrollOffset + i;
            var row = Rows[i];

            if (filteredPosition < FilteredIndices.Count)
            {
                var absoluteIndex = FilteredIndices[filteredPosition];
                var entry = Entries[absoluteIndex];
                row.SetEntry(entry.Icon, entry.Name, entry.Count);
                row.IsSelected = absoluteIndex == SelectedIndex;
                row.Visible = true;
            } else
            {
                row.ClearEntry();
                row.Visible = false;
            }
        }

        var total = FilteredIndices.Count;
        var last = Math.Min(ItemScrollOffset + VISIBLE_ROWS, total);
        PageLabel.Text = total > VISIBLE_ROWS ? $"{ItemScrollOffset + 1}-{last}/{total}" : string.Empty;
        ItemPrevButton.SetEnabled(ItemScrollOffset > 0);
        ItemNextButton.SetEnabled(ItemScrollOffset < MaxItemScroll);

        //a wheel scroll keeps the cursor over the same row slot, which now shows a different item — refresh the tooltip
        if (HoveredSlot >= 0)
            HoveredIndex = RowToAbsoluteIndex(HoveredSlot);

        //OK is always actionable — it confirms the selection, or closes the shop when nothing is selected
        //(HandleOk). With the session chrome suppressed there is no other mouse affordance to dismiss the bank.
    }

    private void HandleTabClick(int slot)
    {
        var categoryIndex = TabWindowStart + slot;

        if ((categoryIndex >= Categories.Count) || (categoryIndex == SelectedCategoryIndex))
            return;

        SelectedCategoryIndex = categoryIndex;
        SelectedIndex = -1;
        ItemScrollOffset = 0;
        ClearHover();

        BuildFilteredIndices();
        UpdateTabDisplay();
        UpdateItemDisplay();
        SaveNpcMemory();
    }

    private void HandleRowClick(int slot)
    {
        var index = RowToAbsoluteIndex(slot);

        if (index < 0)
            return;

        SelectedIndex = index;
        UpdateItemDisplay();
    }

    private void HandleRowConfirm(int slot)
    {
        var index = RowToAbsoluteIndex(slot);

        if (index < 0)
            return;

        SelectedIndex = index;
        UpdateItemDisplay();
        OnItemSelected?.Invoke(SelectedIndex);
    }

    private int RowToAbsoluteIndex(int slot)
    {
        var filteredPosition = ItemScrollOffset + slot;

        return filteredPosition < FilteredIndices.Count ? FilteredIndices[filteredPosition] : -1;
    }

    internal void NotifyRowHover(int slot)
    {
        HoveredSlot = slot;
        HoveredIndex = RowToAbsoluteIndex(slot);
    }

    //only clear when the leaving row is the one currently tracked — a row-to-row move enters the new row
    //(setting HoveredSlot) before the old row's leave fires, so a stale leave must not wipe the new hover.
    internal void NotifyRowLeave(int slot)
    {
        if (HoveredSlot != slot)
            return;

        HoveredSlot = -1;
        HoveredIndex = -1;
    }

    internal void NotifyTabHover(int slot)
    {
        var categoryIndex = TabWindowStart + slot;

        if ((categoryIndex < 0) || (categoryIndex >= Categories.Count))
            return;

        var category = Categories[categoryIndex];
        HoveredTabSlot = slot;

        //only surface the tooltip when the tab label is actually clipped — otherwise the full name is already visible
        HoveredCategory = category.Length > TAB_MAX_CHARS ? category : null;
    }

    internal void NotifyTabLeave(int slot)
    {
        if (HoveredTabSlot != slot)
            return;

        HoveredTabSlot = -1;
        HoveredCategory = null;
    }

    private void ClearHover()
    {
        HoveredSlot = -1;
        HoveredIndex = -1;
        HoveredTabSlot = -1;
        HoveredCategory = null;
    }

    private void HandleOk()
    {
        if (SelectedIndex >= 0)
            OnItemSelected?.Invoke(SelectedIndex);
        else
            OnClose?.Invoke();
    }

    private void ScrollTabsPrev() => SetTabWindow(TabWindowStart - 1);

    private void ScrollTabsNext() => SetTabWindow(TabWindowStart + 1);

    private void SetTabWindow(int target)
    {
        target = Math.Clamp(target, 0, Math.Max(0, Categories.Count - MAX_VISIBLE_TABS));

        if (target == TabWindowStart)
            return;

        TabWindowStart = target;
        UpdateTabDisplay();
    }

    //the < > buttons jump a whole page; the wheel moves one row at a time (see OnMouseScroll)
    private void PrevItemPage() => SetItemScroll(ItemScrollOffset - VISIBLE_ROWS);

    private void NextItemPage() => SetItemScroll(ItemScrollOffset + VISIBLE_ROWS);

    private void SetItemScroll(int target)
    {
        target = Math.Clamp(target, 0, MaxItemScroll);

        if (target == ItemScrollOffset)
            return;

        //the selection is intentionally kept across scrolling — the highlight simply moves out of view and OK still
        //acts on it; UpdateItemDisplay re-maps the hovered row so its tooltip tracks the newly revealed item
        ItemScrollOffset = target;
        UpdateItemDisplay();
        SaveNpcMemory();
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        var localX = e.ScreenX - ScreenX;

        //cursor over the left tab column scrolls categories; anywhere else scrolls the item rows. Wheel-up (positive
        //delta) moves toward the top, matching the rest of the client (see MenuListPanel).
        if (localX < ITEM_X)
            SetTabWindow(TabWindowStart - e.Delta);
        else
            SetItemScroll(ItemScrollOffset - e.Delta);

        e.Handled = true;
    }

    private void SaveNpcMemory()
    {
        if (string.IsNullOrEmpty(CurrentNpcName) || Categories.Count == 0)
            return;

        if ((SelectedCategoryIndex < 0) || (SelectedCategoryIndex >= Categories.Count))
            return;

        NpcMemory[CurrentNpcName] = (Categories[SelectedCategoryIndex], ItemScrollOffset);
    }

    private sealed record MerchantEntry(
        string Name,
        Texture2D? Icon,
        int Count,
        byte Slot,
        string Category = "",
        string Description = "",
        int? Level = null,
        byte? Class = null,
        int? Weight = null);

    /// <summary>A single item row: icon + truncated name + right-aligned count, with hover/selection fills.</summary>
    private sealed class ItemRow : UIPanel
    {
        private readonly UIImage IconImage;
        private readonly UILabel NameLabel;
        private readonly UILabel CountLabel;
        private bool Hovered;

        public event ClickedHandler? Clicked;
        public event ClickedHandler? Confirmed;

        public bool IsSelected
        {
            get;
            set
            {
                if (field == value)
                    return;

                field = value;
                var color = value ? SelectedTextColor : LegendColors.White;
                NameLabel.ForegroundColor = color;
                CountLabel.ForegroundColor = color;
                RefreshFill();
            }
        }

        public int RowIndex { get; init; }

        public ItemRow(int width)
        {
            Width = width;
            Height = ROW_H;

            //display-only children so the row stays the deepest hit-test target (reliable OnMouseLeave for the tooltip)
            IconImage = new UIImage
            {
                X = 4,
                Y = (ROW_H - ICON_SIZE) / 2,
                Width = ICON_SIZE,
                Height = ICON_SIZE,
                IsHitTestVisible = false
            };

            NameLabel = new UILabel
            {
                X = 4 + ICON_SIZE + 6,
                Y = (ROW_H - TextRenderer.CHAR_HEIGHT) / 2,
                Width = width - (4 + ICON_SIZE + 6) - COUNT_W - 4,
                Height = TextRenderer.CHAR_HEIGHT,
                ForegroundColor = LegendColors.White,
                IsHitTestVisible = false
            };

            CountLabel = new UILabel
            {
                X = width - COUNT_W - 4,
                Y = (ROW_H - TextRenderer.CHAR_HEIGHT) / 2,
                Width = COUNT_W,
                Height = TextRenderer.CHAR_HEIGHT,
                HorizontalAlignment = HorizontalAlignment.Right,
                ForegroundColor = LegendColors.White,
                IsHitTestVisible = false
            };

            AddChild(IconImage);
            AddChild(NameLabel);
            AddChild(CountLabel);
        }

        public void SetEntry(Texture2D? icon, string name, int count)
        {
            IconImage.Texture = icon;
            IconImage.Visible = icon is not null;

            var newline = name.IndexOf('\n');

            if (newline >= 0)
                name = name[..newline];

            if (name.Length > MAX_NAME_CHARS)
                name = name[..(MAX_NAME_CHARS - 3)] + "...";

            NameLabel.Text = name;
            CountLabel.Text = count > 0 ? count.ToString("N0") : string.Empty;
        }

        public void ClearEntry()
        {
            IconImage.Texture = null;
            IconImage.Visible = false;
            NameLabel.Text = string.Empty;
            CountLabel.Text = string.Empty;
            IsSelected = false;
            Hovered = false;
            RefreshFill();
        }

        private void RefreshFill() => BackgroundColor = IsSelected ? RowSelectedFill : Hovered ? RowHoverFill : null;

        public override void OnMouseEnter()
        {
            Hovered = true;
            RefreshFill();
            (Parent as BankShopPanel)?.NotifyRowHover(RowIndex);
        }

        public override void OnMouseLeave()
        {
            Hovered = false;
            RefreshFill();
            (Parent as BankShopPanel)?.NotifyRowLeave(RowIndex);
        }

        public override void OnClick(ClickEvent e)
        {
            if (e.Button != MouseButton.Left)
                return;

            Clicked?.Invoke();
            e.Handled = true;
        }

        public override void OnDoubleClick(DoubleClickEvent e)
        {
            if (e.Button != MouseButton.Left)
                return;

            Confirmed?.Invoke();
            e.Handled = true;
        }
    }

    /// <summary>A category tab showing the category name as text with an idle/selected frame.</summary>
    private sealed class CategoryTab : UIPanel
    {
        private readonly UILabel NameLabel;

        public string Category { get; private set; } = string.Empty;
        public int TabIndex { get; init; }

        public event ClickedHandler? Clicked;

        public bool IsSelected
        {
            get;
            set
            {
                field = value;
                BackgroundColor = value ? TabSelectedFill : TabIdleFill;
                BorderColor = value ? TabSelectedBorder : TabIdleBorder;
                NameLabel.ForegroundColor = value ? SelectedTextColor : LegendColors.White;
            }
        }

        public CategoryTab(int width, int height)
        {
            BackgroundColor = TabIdleFill;
            BorderColor = TabIdleBorder;

            NameLabel = new UILabel
            {
                X = 2,
                Y = 0,
                Width = width - 4,
                Height = height,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ShrinkToFit = false,
                ForegroundColor = LegendColors.White,
                IsHitTestVisible = false
            };

            AddChild(NameLabel);
        }

        public void SetCategory(string category, string label)
        {
            Category = category;
            NameLabel.Text = label;
        }

        public override void OnMouseEnter() => (Parent as BankShopPanel)?.NotifyTabHover(TabIndex);

        public override void OnMouseLeave() => (Parent as BankShopPanel)?.NotifyTabLeave(TabIndex);

        public override void OnClick(ClickEvent e)
        {
            if (e.Button != MouseButton.Left)
                return;

            Clicked?.Invoke();
            e.Handled = true;
        }
    }

    /// <summary>A primitive text button (bg/border + centered label) with hover/pressed/disabled states.</summary>
    private sealed class TextButton : UIPanel
    {
        private static readonly Color IdleFill = new(30, 32, 42);
        private static readonly Color HoverFill = new(52, 54, 66);
        private static readonly Color PressFill = new(74, 58, 22);
        private static readonly Color EdgeColor = new(150, 126, 78);
        private static readonly Color DisabledText = new(96, 96, 96);

        private readonly UILabel Label;
        private bool Hovered;
        private bool Pressed;

        public event ClickedHandler? Clicked;

        public TextButton(string text, int width, int height)
        {
            Width = width;
            Height = height;
            BorderColor = EdgeColor;

            Label = new UILabel
            {
                X = 0,
                Y = 0,
                Width = width,
                Height = height,
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ForegroundColor = LegendColors.White,
                ShrinkToFit = false,
                IsHitTestVisible = false
            };

            AddChild(Label);
            RefreshVisual();
        }

        //Enabled has no change hook on UIElement, so callers flip state through here to refresh the visual
        public void SetEnabled(bool value)
        {
            Enabled = value;
            RefreshVisual();
        }

        private void RefreshVisual()
        {
            BackgroundColor = !Enabled ? IdleFill : Pressed ? PressFill : Hovered ? HoverFill : IdleFill;
            Label.ForegroundColor = Enabled ? LegendColors.White : DisabledText;
            BorderColor = Enabled ? EdgeColor : DividerColor;
        }

        public override void OnMouseEnter()
        {
            Hovered = true;
            RefreshVisual();
        }

        public override void OnMouseLeave()
        {
            Hovered = false;
            Pressed = false;
            RefreshVisual();
        }

        public override void OnMouseDown(MouseDownEvent e)
        {
            if (e.Button != MouseButton.Left)
                return;

            Pressed = true;
            RefreshVisual();
            e.Handled = true;
        }

        public override void OnMouseUp(MouseUpEvent e)
        {
            if (e.Button == MouseButton.Left)
            {
                Pressed = false;
                RefreshVisual();
            }
        }

        public override void OnClick(ClickEvent e)
        {
            if (e.Button != MouseButton.Left)
                return;

            if (Enabled)
                Clicked?.Invoke();

            e.Handled = true;
        }
    }
}
