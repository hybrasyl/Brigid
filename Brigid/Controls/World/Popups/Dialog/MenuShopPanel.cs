#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Data;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Chaos.Extensions.Common;
using Chaos.Networking.Entities.Server;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Popups.Dialog;

/// <summary>
///     Merchant/shop/trainer browser panel using the lnpcd3 prefab. Handles the 6 merchant menu types: ShowItems (buy),
///     ShowPlayerItems (sell), ShowSkills, ShowSpells, ShowPlayerSkills, ShowPlayerSpells. Displays an icon+name list in
///     the Content area, item details on the right side, category tabs, and page navigation. Owned by NpcSessionControl
///     which handles Escape, close, and response dispatch.
/// </summary>
public sealed class MenuShopPanel : PrefabPanel
{
    private const int ICON_SIZE = 32;
    private const int MAX_COMBINED_CHARS = 32;
    private const int ROW_HEIGHT = 40;
    private const int ICON_TEXT_GAP = 8;
    private const int MAX_VISIBLE_TABS = 4;
    private const int TAB_WIDTH = 60;
    private const int TAB_HEIGHT = 16;
    private const int TAB_START_X = 165;
    private const int TAB_START_Y = 34;

    private readonly List<string> Categories = [];
    private readonly Rectangle ContentRect;
    private readonly UILabel? DescClassLabel;
    private readonly UILabel? DescLevelLabel;
    private readonly UILabel? DescTextLabel;
    private readonly UILabel? DescWeightLabel;
    private readonly List<MerchantEntry> Entries = [];
    private readonly List<int> FilteredIndices = [];
    private readonly int ItemsPerPage;

    private readonly MerchantListingPanel[] Listings;
    private readonly UILabel? MoneyLabel;
    private readonly UILabel? PageLabel;
    private readonly MerchantTab[] Tabs;

    private int CurrentPage;
    private int HoveredRow = -1;
    private int SelectedCategoryIndex;
    private int SelectedIndex = -1;
    private int TabWindowStart;
    private int TotalPages;

    //per-NPC shop state memory: restore last-used tab + page when the same NPC re-opens a shop menu
    private readonly Dictionary<string, (string Category, int Page)> NpcShopMemory = new(StringComparer.OrdinalIgnoreCase);
    private string? CurrentNpcName;

    public MenuType CurrentMenuType { get; private set; }

    public UIButton? CloseButton { get; }
    public UIButton? PageNextButton { get; }
    public UIButton? PagePrevButton { get; }
    public UIButton? TabNextButton { get; }
    public UIButton? TabPrevButton { get; }

    public MenuShopPanel()
        : base("lnpcd3", false)
    {
        Name = "MerchantBrowser";
        Visible = false;

        //right-aligned, bottom-anchored above dialog bar (same as other dialog sub-panels)
        X = ChaosGame.VIRTUAL_WIDTH - Width;
        Y = 372 - Height;

        CloseButton = CreateButton("Btn1");
        PagePrevButton = CreateButton("PagePrev");
        PageNextButton = CreateButton("PageNext");
        TabPrevButton = CreateButton("TabPrev");
        TabNextButton = CreateButton("TabNext");

        if (CloseButton is not null)
            CloseButton.Clicked += () =>
            {
                if (SelectedIndex >= 0)
                    OnItemSelected?.Invoke(SelectedIndex);
                else
                    OnClose?.Invoke();
            };

        if (PagePrevButton is not null)
            PagePrevButton.Clicked += () =>
            {
                if (CurrentPage > 0)
                {
                    CurrentPage--;
                    UpdatePageDisplay();
                    SaveNpcMemory();
                }
            };

        if (PageNextButton is not null)
            PageNextButton.Clicked += () =>
            {
                if (CurrentPage < (TotalPages - 1))
                {
                    CurrentPage++;
                    UpdatePageDisplay();
                    SaveNpcMemory();
                }
            };

        if (TabPrevButton is not null)
        {
            TabPrevButton.DisabledTexture = UiRenderer.Instance!.GetSpfTexture("nd_mcp.spf", 2);

            TabPrevButton.Clicked += () =>
            {
                if (TabWindowStart > 0)
                {
                    TabWindowStart--;
                    UpdateTabDisplay();
                }
            };
        }

        if (TabNextButton is not null)
        {
            TabNextButton.DisabledTexture = UiRenderer.Instance!.GetSpfTexture("nd_mcn.spf", 2);

            TabNextButton.Clicked += () =>
            {
                if ((TabWindowStart + MAX_VISIBLE_TABS) < Categories.Count)
                {
                    TabWindowStart++;
                    UpdateTabDisplay();
                }
            };
        }

        //create category tabs
        var uiCache = UiRenderer.Instance!;
        var tabNormal = uiCache.GetSpfTexture("nd_mtab.spf");
        var tabSelected = uiCache.GetSpfTexture("nd_mtab.spf", 1);

        Tabs = new MerchantTab[MAX_VISIBLE_TABS];

        for (var i = 0; i < MAX_VISIBLE_TABS; i++)
        {
            var tab = new MerchantTab(tabNormal, tabSelected)
            {
                X = TAB_START_X + i * TAB_WIDTH,
                Y = TAB_START_Y,
                Width = TAB_WIDTH,
                Height = TAB_HEIGHT,
                Visible = false
            };

            var tabIndex = i;
            tab.Clicked += () => HandleTabClick(tabIndex);
            Tabs[i] = tab;
            AddChild(tab);
        }

        ContentRect = GetRect("Content");
        ItemsPerPage = ContentRect.Height > 0 ? ContentRect.Height / ROW_HEIGHT : 4;

        //create listing panels as children for each visible row slot
        Listings = new MerchantListingPanel[ItemsPerPage];

        for (var i = 0; i < ItemsPerPage; i++)
        {
            var listing = new MerchantListingPanel(ContentRect.Width)
            {
                RowIndex = i,
                X = ContentRect.X,
                Y = ContentRect.Y + i * ROW_HEIGHT,
                Width = ContentRect.Width,
                Height = ROW_HEIGHT,
                Visible = false
            };

            var rowIndex = i;
            listing.Clicked += () => HandleListingClick(rowIndex);
            Listings[i] = listing;
            AddChild(listing);
        }

        DescClassLabel = CreateLabel("DescClass");
        DescClassLabel?.ForegroundColor = LegendColors.White;
        
        DescLevelLabel = CreateLabel("DescLevel");
        DescLevelLabel?.ForegroundColor = LegendColors.White;
        
        DescWeightLabel = CreateLabel("DescWeight");
        DescWeightLabel?.ForegroundColor = LegendColors.White;
        
        DescTextLabel = CreateLabel("DescText");
        DescTextLabel?.ForegroundColor = LegendColors.White;

        DescTextLabel?.WordWrap = true;
        MoneyLabel = CreateLabel("Money", HorizontalAlignment.Right);
        MoneyLabel?.ForegroundColor = LegendColors.White;
        
        PageLabel = CreateLabel("Page", HorizontalAlignment.Center);
        PageLabel?.PaddingLeft = 0;
        PageLabel?.PaddingRight = 0;
        PageLabel?.HorizontalAlignment = HorizontalAlignment.Center;
        PageLabel?.ShrinkToFit = false;
        PageLabel?.ForegroundColor = LegendColors.White;
    }

    private void BuildCategories()
    {
        Categories.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in Entries)
        {
            var category = entry.Category.Length > 0 ? entry.Category : "other";

            if (seen.Add(category))
                Categories.Add(category);
        }

        //sort alphabetically, but keep "other" at the end
        Categories.Sort((a, b) =>
        {
            var aIsOther = a.EqualsI("Other");
            var bIsOther = b.EqualsI("Other");

            if (aIsOther && !bIsOther)
                return 1;

            if (!aIsOther && bIsOther)
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

        var selectedCategory = Categories[SelectedCategoryIndex];
        var isOther = selectedCategory.EqualsI("Other");

        for (var i = 0; i < Entries.Count; i++)
        {
            var entryCategory = Entries[i].Category;

            if (isOther && (entryCategory.Length == 0))
                FilteredIndices.Add(i);
            else if (entryCategory.EqualsI(selectedCategory))
                FilteredIndices.Add(i);
        }
    }

    private void ClearDetails()
    {
        DescClassLabel?.Text = string.Empty;
        DescLevelLabel?.Text = string.Empty;
        DescWeightLabel?.Text = string.Empty;
        DescTextLabel?.Text = string.Empty;
    }

    private void ClearEntries()
    {
        Entries.Clear();
        FilteredIndices.Clear();
        Categories.Clear();
        SelectedIndex = -1;
        SelectedCategoryIndex = 0;
        TabWindowStart = 0;

        foreach (var listing in Listings)
        {
            listing.ClearEntry();
            listing.IsSelected = false;
            listing.Visible = false;
        }

        foreach (var tab in Tabs)
            tab.Visible = false;
    }

    /// <summary>
    ///     Returns the name of the entry at the given absolute index, or null if out of range.
    /// </summary>
    public string? GetEntryName(int index)
    {
        if ((index < 0) || (index >= Entries.Count))
            return null;

        return Entries[index].Name;
    }

    /// <summary>
    ///     Returns the slot byte for the entry at the given absolute index, or null if out of range.
    /// </summary>
    public byte? GetEntrySlot(int index)
    {
        if ((index < 0) || (index >= Entries.Count))
            return null;

        return Entries[index].Slot;
    }

    private void HandleListingClick(int rowIndex)
    {
        var filteredPosition = CurrentPage * ItemsPerPage + rowIndex;

        if (filteredPosition >= FilteredIndices.Count)
            return;

        var absoluteIndex = FilteredIndices[filteredPosition];

        if (absoluteIndex == SelectedIndex)
            OnItemSelected?.Invoke(SelectedIndex);
        else
        {
            SelectedIndex = absoluteIndex;
            ShowDetails(absoluteIndex);
            UpdateListingStates();

            CloseButton?.Enabled = true;
        }
    }

    private void HandleTabClick(int slotIndex)
    {
        var visibleCount = Math.Min(MAX_VISIBLE_TABS, Categories.Count - TabWindowStart);
        var offset = MAX_VISIBLE_TABS - visibleCount;

        if (slotIndex < offset)
            return;

        var categoryIndex = TabWindowStart + (slotIndex - offset);

        if ((categoryIndex >= Categories.Count) || (categoryIndex == SelectedCategoryIndex))
            return;

        SelectedCategoryIndex = categoryIndex;
        SelectedIndex = -1;
        CurrentPage = 0;
        HoveredRow = -1;

        BuildFilteredIndices();
        TotalPages = FilteredIndices.Count > 0 ? (FilteredIndices.Count + ItemsPerPage - 1) / ItemsPerPage : 1;

        UpdateTabDisplay();
        UpdatePageDisplay();
        ClearDetails();

        CloseButton?.Enabled = false;

        SaveNpcMemory();
    }

    private void SaveNpcMemory()
    {
        if (string.IsNullOrEmpty(CurrentNpcName) || Categories.Count == 0)
            return;

        if ((SelectedCategoryIndex < 0) || (SelectedCategoryIndex >= Categories.Count))
            return;

        NpcShopMemory[CurrentNpcName] = (Categories[SelectedCategoryIndex], CurrentPage);
    }

    public override void Hide()
    {
        if (HoveredRow >= 0)
        {
            HoveredRow = -1;
            OnItemHoverExit?.Invoke();
        }

        Visible = false;
        ClearEntries();
        ClearDetails();
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        var localX = e.ScreenX - ScreenX;
        var localY = e.ScreenY - ScreenY;

        if ((localX >= ContentRect.X)
            && (localX < (ContentRect.X + ContentRect.Width))
            && (localY >= ContentRect.Y)
            && (localY < (ContentRect.Y + ContentRect.Height)))
        {
            var row = (localY - ContentRect.Y) / ROW_HEIGHT;
            var filteredPosition = CurrentPage * ItemsPerPage + row;

            if ((row >= 0) && (row < ItemsPerPage) && (filteredPosition < FilteredIndices.Count))
            {
                if (row != HoveredRow)
                {
                    HoveredRow = row;
                    var absoluteIndex = FilteredIndices[filteredPosition];
                    ShowDetails(absoluteIndex);
                    OnItemHoverEnter?.Invoke(Entries[absoluteIndex].Name);
                }
            } else if (HoveredRow >= 0)
            {
                HoveredRow = -1;
                OnItemHoverExit?.Invoke();
            }
        } else if (HoveredRow >= 0)
        {
            HoveredRow = -1;
            OnItemHoverExit?.Invoke();
        }
    }

    /// <summary>
    ///     Called by a child listing when its OnMouseLeave fires. Because OnMouseMove is dispatched BEFORE
    ///     OnMouseLeave in the input dispatcher, a row-to-row transition has already updated HoveredRow via
    ///     the MenuShopPanel.OnMouseMove bubble from the new listing by the time this runs — so we only
    ///     reset state when HoveredRow still matches the exiting row (meaning no sibling listing took over,
    ///     i.e. the mouse left the panel entirely, possibly via a fast jump).
    /// </summary>
    private void NotifyListingLeave(int rowIndex)
    {
        if (HoveredRow == rowIndex)
        {
            HoveredRow = -1;
            OnItemHoverExit?.Invoke();
        }
    }

    public event CloseHandler? OnClose;
    public event ItemHoverEnterHandler? OnItemHoverEnter;
    public event ItemHoverExitHandler? OnItemHoverExit;
    public event ItemSelectedHandler? OnItemSelected;

    private void PopulateItems(NpcMenuPacket pkt)
    {
        if (pkt.Menu is not ItemListMenu menu)
            return;

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

    private void PopulatePlayerItems(NpcMenuPacket pkt)
    {
        if (pkt.Menu is not PlayerItemListMenu menu)
            return;

        //collect names for metadata batch lookup
        var names = new List<string>();

        foreach (var slot in menu.Slots)
        {
            ref readonly var slotData = ref WorldState.Inventory.GetSlot(slot);

            if (slotData is { IsOccupied: true, Name: not null })
                names.Add(slotData.Name);
        }

        var metadata = DataContext.MetaFiles.GetItemMetadata(names.ToArray());

        foreach (var slot in menu.Slots)
        {
            ref readonly var slotData = ref WorldState.Inventory.GetSlot(slot);

            if (!slotData.IsOccupied)
                continue;

            var name = slotData.Name ?? string.Empty;
            var icon = UiRenderer.Instance!.GetItemIcon(slotData.Sprite, slotData.Color);
            metadata.TryGetValue(name, out var meta);

            Entries.Add(
                new MerchantEntry(
                    name,
                    icon,
                    0,
                    slot,
                    meta?.Category ?? string.Empty,
                    meta?.Description ?? string.Empty,
                    meta?.Level,
                    meta?.Class,
                    meta?.Weight));
        }
    }

    private void PopulatePlayerSkills()
    {
        for (byte slot = 1; slot <= SkillBook.MAX_SLOTS; slot++)
        {
            ref readonly var slotData = ref WorldState.SkillBook.GetSlot(slot);

            if (!slotData.IsOccupied)
                continue;

            var icon = UiRenderer.Instance!.GetSkillIcon(slotData.Sprite).Texture;

            Entries.Add(
                new MerchantEntry(
                    slotData.Name ?? string.Empty,
                    icon,
                    0,
                    slot));
        }
    }

    private void PopulatePlayerSpells()
    {
        for (byte slot = 1; slot <= SpellBook.MAX_SLOTS; slot++)
        {
            ref readonly var slotData = ref WorldState.SpellBook.GetSlot(slot);

            if (!slotData.IsOccupied)
                continue;

            var icon = UiRenderer.Instance!.GetSpellIcon(slotData.Sprite).Texture;

            Entries.Add(
                new MerchantEntry(
                    slotData.Name ?? string.Empty,
                    icon,
                    0,
                    slot));
        }
    }

    private void PopulateSkills(NpcMenuPacket pkt)
    {
        if (pkt.Menu is not SkillListMenu menu)
            return;

        byte slot = 1;

        foreach (var skill in menu.Skills)
        {
            var icon = UiRenderer.Instance!.GetSkillIcon(skill.Icon).Texture;

            Entries.Add(
                new MerchantEntry(
                    skill.Name,
                    icon,
                    0,
                    slot++));
        }
    }

    private void PopulateSpells(NpcMenuPacket pkt)
    {
        if (pkt.Menu is not SpellListMenu menu)
            return;

        byte slot = 1;

        foreach (var spell in menu.Spells)
        {
            var icon = UiRenderer.Instance!.GetSpellIcon(spell.Icon).Texture;

            Entries.Add(
                new MerchantEntry(
                    spell.Name,
                    icon,
                    0,
                    slot++));
        }
    }

    private void ShowDetails(int absoluteIndex)
    {
        if ((absoluteIndex < 0) || (absoluteIndex >= Entries.Count))
        {
            ClearDetails();

            return;
        }

        var entry = Entries[absoluteIndex];
        var hasDetails = entry.Class is not null || entry.Level is not null || entry.Weight is not null;

        DescClassLabel?.Text = entry.Class is { } cls
            ? ((BaseClass)cls == BaseClass.Peasant ? "all class" : ((BaseClass)cls).ToString())
            : string.Empty;

        DescLevelLabel?.Text = entry.Level is { } lvl
            ? (lvl == 0 ? "no limit" : lvl.ToString())
            : string.Empty;

        DescWeightLabel?.Text = entry.Weight?.ToString() ?? string.Empty;

        DescTextLabel?.Text = hasDetails ? entry.Description : string.Empty;
    }

    /// <summary>
    ///     Shows the merchant panel for a DisplayMenuArgs with one of the 6 merchant menu types.
    /// </summary>
    public void ShowMerchant(NpcMenuPacket pkt)
    {
        CurrentMenuType = (MenuType)(byte)pkt.MenuType;
        CurrentNpcName = pkt.Name;

        ClearEntries();
        CurrentPage = 0;
        SelectedIndex = -1;

        switch ((MenuType)(byte)pkt.MenuType)
        {
            case MenuType.ShowItems:
                PopulateItems(pkt);

                break;

            case MenuType.ShowPlayerItems:
                PopulatePlayerItems(pkt);

                break;

            case MenuType.ShowSkills:
                PopulateSkills(pkt);

                break;

            case MenuType.ShowSpells:
                PopulateSpells(pkt);

                break;

            case MenuType.ShowPlayerSkills:
                PopulatePlayerSkills();

                break;

            case MenuType.ShowPlayerSpells:
                PopulatePlayerSpells();

                break;
        }

        BuildCategories();

        //restore the last-used tab + page for this NPC, if the saved category still exists
        SelectedCategoryIndex = 0;
        var savedPage = 0;

        if (!string.IsNullOrEmpty(CurrentNpcName) && NpcShopMemory.TryGetValue(CurrentNpcName, out var saved))
            for (var i = 0; i < Categories.Count; i++)
                if (Categories[i].EqualsI(saved.Category))
                {
                    SelectedCategoryIndex = i;
                    savedPage = saved.Page;

                    break;
                }

        //scroll the tab window so the restored tab is visible
        TabWindowStart = 0;

        if (SelectedCategoryIndex >= MAX_VISIBLE_TABS)
            TabWindowStart = Math.Min(SelectedCategoryIndex - MAX_VISIBLE_TABS + 1, Math.Max(0, Categories.Count - MAX_VISIBLE_TABS));

        BuildFilteredIndices();
        TotalPages = FilteredIndices.Count > 0 ? (FilteredIndices.Count + ItemsPerPage - 1) / ItemsPerPage : 1;

        //clamp saved page against the new filtered total; fall back to page 1 if out of range
        CurrentPage = savedPage < TotalPages ? savedPage : 0;

        UpdateTabDisplay();
        UpdatePageDisplay();
        ClearDetails();

        MoneyLabel?.Text = WorldState.Inventory.Gold.ToString("N0");

        CloseButton?.Enabled = false;

        Show();
    }

    private void UpdateListingStates()
    {
        var pageStart = CurrentPage * ItemsPerPage;

        for (var i = 0; i < Listings.Length; i++)
        {
            var filteredPosition = pageStart + i;
            var listing = Listings[i];

            if (filteredPosition < FilteredIndices.Count)
            {
                var absoluteIndex = FilteredIndices[filteredPosition];
                var entry = Entries[absoluteIndex];
                listing.SetEntry(entry.Icon, entry.Name, entry.Cost);
                listing.IsSelected = absoluteIndex == SelectedIndex;
                listing.Visible = true;
            } else
            {
                listing.ClearEntry();
                listing.IsSelected = false;
                listing.Visible = false;
            }
        }
    }

    private void UpdatePageDisplay()
    {
        PageLabel?.Text = $"{CurrentPage + 1} / {TotalPages}";

        PagePrevButton?.Enabled = CurrentPage > 0;

        PageNextButton?.Enabled = CurrentPage < (TotalPages - 1);

        UpdateListingStates();
    }

    private void UpdateTabDisplay()
    {
        var visibleCount = Math.Min(MAX_VISIBLE_TABS, Categories.Count - TabWindowStart);
        var offset = MAX_VISIBLE_TABS - visibleCount;

        for (var i = 0; i < MAX_VISIBLE_TABS; i++)
        {
            var tab = Tabs[i];

            if (i < offset)
            {
                tab.Visible = false;

                continue;
            }

            var categoryIndex = TabWindowStart + (i - offset);

            if (categoryIndex < Categories.Count)
            {
                tab.X = TAB_START_X + i * TAB_WIDTH;
                tab.SetCategory(Categories[categoryIndex]);
                tab.IsSelected = categoryIndex == SelectedCategoryIndex;
                tab.Visible = true;
            } else
                tab.Visible = false;
        }

        TabPrevButton?.Enabled = TabWindowStart > 0;

        TabNextButton?.Enabled = (TabWindowStart + MAX_VISIBLE_TABS) < Categories.Count;
    }

    private sealed record MerchantEntry(
        string Name,
        Texture2D? Icon,
        int Cost,
        byte Slot,
        string Category = "",
        string Description = "",
        int? Level = null,
        byte? Class = null,
        int? Weight = null);

    /// <summary>
    ///     A single row in the merchant listing. Renders an icon and name text with a selection highlight.
    /// </summary>
    private sealed class MerchantListingPanel : UIPanel
    {
        private static readonly Color SELECTED_TEXT_COLOR = new(206, 0, 16);

        private readonly UILabel CostLabel;
        private readonly UIImage IconImage;
        private readonly UILabel NameLabel;

        public bool IsSelected
        {
            // ReSharper disable once UnusedMember.Local
            get;
            set
            {
                if (field == value)
                    return;

                field = value;

                var color = value ? SELECTED_TEXT_COLOR : Color.White;
                NameLabel.ForegroundColor = color;
                CostLabel.ForegroundColor = color;
            }
        }

        public int RowIndex { get; init; }

        public MerchantListingPanel(int contentWidth)
        {
            Width = contentWidth;
            Height = ROW_HEIGHT;

            //display-only children: the listing must remain the deepest hit-test target so its
            //OnMouseLeave fires reliably when the cursor exits the panel — otherwise the parent
            //MenuShopPanel never learns that hover ended and the tooltip "escapes" the panel.
            IconImage = new UIImage
            {
                X = 4,
                Y = (ROW_HEIGHT - ICON_SIZE) / 2,
                Width = ICON_SIZE,
                Height = ICON_SIZE,
                IsHitTestVisible = false
            };

            NameLabel = new UILabel
            {
                X = 4 + ICON_SIZE + ICON_TEXT_GAP,
                Y = (ROW_HEIGHT - TextRenderer.CHAR_HEIGHT) / 2,
                Width = 200,
                Height = TextRenderer.CHAR_HEIGHT,
                ForegroundColor = Color.White,
                IsHitTestVisible = false
            };

            CostLabel = new UILabel
            {
                X = 0,
                Y = (ROW_HEIGHT - TextRenderer.CHAR_HEIGHT) / 2,
                Width = contentWidth - 4,
                Height = TextRenderer.CHAR_HEIGHT,
                HorizontalAlignment = HorizontalAlignment.Right,
                ForegroundColor = Color.White,
                IsHitTestVisible = false
            };

            AddChild(IconImage);
            AddChild(NameLabel);
            AddChild(CostLabel);
        }

        public void ClearEntry()
        {
            IconImage.Texture = null;
            IconImage.Visible = false;
            NameLabel.Text = string.Empty;
            CostLabel.Text = string.Empty;
        }

        public event ClickedHandler? Clicked;

        public void SetEntry(Texture2D? icon, string name, int cost)
        {
            IconImage.Texture = icon;
            IconImage.Visible = icon is not null;

            var costText = cost > 0 ? cost.ToString("N0") : string.Empty;
            var maxName = MAX_COMBINED_CHARS - costText.Length;

            //truncate at first newline
            var newlineIndex = name.IndexOf('\n');

            if (newlineIndex >= 0)
                name = newlineIndex <= (maxName - 3) ? name[..newlineIndex] + "..." : name[..newlineIndex];

            //truncate to fit within combined max
            if (name.Length > maxName)
                name = name[..(maxName - 3)] + "...";

            NameLabel.Text = name;
            CostLabel.Text = costText;
        }

        public override void OnClick(ClickEvent e)
        {
            Clicked?.Invoke();
            e.Handled = true;
        }

        public override void OnMouseLeave() => (Parent as MenuShopPanel)?.NotifyListingLeave(RowIndex);
    }

    /// <summary>
    ///     A single category tab. Draws nd_mtab.spf normal/selected background with centered category text.
    /// </summary>
    private sealed class MerchantTab : UIPanel
    {
        private readonly UIImage BackgroundImage;
        private readonly UILabel NameLabel;
        private readonly Texture2D? NormalBg;
        private readonly Texture2D? SelectedBg;

        public bool IsSelected
        {
            // ReSharper disable once UnusedMember.Local
            get;
            set
            {
                if (field == value)
                    return;

                field = value;
                BackgroundImage.Texture = value ? SelectedBg : NormalBg;
            }
        }

        public MerchantTab(Texture2D? normalTexture, Texture2D? selectedTexture)
        {
            Width = TAB_WIDTH;
            Height = TAB_HEIGHT;
            NormalBg = normalTexture;
            SelectedBg = selectedTexture;

            //background UIImage must use the texture's natural dimensions, not TAB_WIDTH/TAB_HEIGHT:
            //UIImage's Width/Height gate its ClipRect, so smaller values would self-clip the texture.
            //Named "BackgroundImage" (not "Background") to avoid shadowing UIPanel.Background (Texture2D?).
            BackgroundImage = new UIImage
            {
                Name = "Background",
                X = 0,
                Y = 0,
                Texture = NormalBg,
                Width = NormalBg?.Width ?? 0,
                Height = NormalBg?.Height ?? 0,
                IsHitTestVisible = false
            };
            AddChild(BackgroundImage);

            NameLabel = new UILabel
            {
                X = 0,
                Y = (TAB_HEIGHT - TextRenderer.CHAR_HEIGHT) / 2 + 2,
                Width = TAB_WIDTH,
                Height = TextRenderer.CHAR_HEIGHT,
                HorizontalAlignment = HorizontalAlignment.Center,
                ForegroundColor = Color.White
            };

            AddChild(NameLabel);
        }

        public event ClickedHandler? Clicked;

        public void SetCategory(string category) => NameLabel.Text = category;

        public override void OnClick(ClickEvent e)
        {
            Clicked?.Invoke();
            e.Handled = true;
        }
    }
}