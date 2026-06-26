#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Data;
using Brigid.Data.Models;
using Brigid.Models;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Profile;

/// <summary>
///     Status book container using _nui prefab (main page). Contains tab navigation and hosts sub-pages: Equipment,
///     Skills, Legend, Events, Family. Each tab page is a separate prefab that swaps in when the tab is selected.
/// </summary>
public sealed class SelfProfileTabControl : PrefabPanel
{
    private const int TAB_COUNT = 6;

    //tab control names in the _nui prefab, in order matching statusbooktab enum
    private static readonly string[] TabControlNames =
    [
        "TAB_INTRO",
        "TAB_LEGEND",
        "TAB_SKILL",
        "TAB_EVENT",
        "TAB_ALBUM",
        "TAB_FAMILY"
    ];

    private readonly Rectangle ContentRect;

    private readonly UIButton?[] TabButtons = new UIButton?[TAB_COUNT];
    private readonly Dictionary<StatusBookTab, PrefabPanel?> TabPages = [];

    public StatusBookTab ActiveTab { get; private set; } = StatusBookTab.Equipment;

    public UIButton? CloseButton { get; }

    public SelfProfileTabControl()
        : base("_nui", false)
    {
        Name = "StatusBook";
        Visible = false;
        UsesControlStack = true;
        IsPassThrough = true;
        WorldState.Equipment.SlotChanged += OnEquipmentSlotChanged;
        WorldState.Equipment.SlotCleared += OnEquipmentSlotCleared;
        X = 0;
        Y = 0;

        //content rect defines where tab page content is positioned
        ContentRect = GetRect("CONTENT");

        //close button (tab_close)
        CloseButton = CreateButton("TAB_CLOSE");

        if (CloseButton is not null)
            CloseButton.Clicked += () =>
            {
                Hide();
                OnClose?.Invoke();
            };

        //configure tab buttons — createbutton creates them as uibuttons with the big/selected
        //texture as normaltexture (the only image in the prefab). we swap that to selectedtexture
        //and assign the small/normal texture from _nui_tb1.spf.
        var cache = UiRenderer.Instance!;

        for (var i = 0; i < TAB_COUNT; i++)
        {
            var tab = (StatusBookTab)i;

            if (CreateButton(TabControlNames[i]) is not { } tabBtn)
                continue;

            TabButtons[i] = tabBtn;
            tabBtn.CenterTexture = true;

            //the prefab's image is the big/selected state — move it to selectedtexture
            tabBtn.SelectedTexture = tabBtn.NormalTexture;

            //load the small/normal texture from _nui_tb1.spf using the same frame index
            var frameIndex = i;

            if (PrefabSet.Contains(TabControlNames[i]))
            {
                var prefab = PrefabSet[TabControlNames[i]];

                if (prefab.Control.Images is { Count: > 0 })
                    frameIndex = prefab.Control.Images[0].FrameIndex;
            }

            tabBtn.NormalTexture = cache.GetSpfTexture("_nui_tb1.spf", frameIndex);

            //wire click → switch tab
            var capturedTab = tab;
            tabBtn.Clicked += () => SwitchTab(capturedTab);

            //set initial selection state
            tabBtn.IsSelected = tab == ActiveTab;

            //draw tabs on top of tab page content
            tabBtn.ZIndex = 1;
        }

        CloseButton?.ZIndex = 1;

        //lazily load tab pages
        TabPages[StatusBookTab.Equipment] = null;
        TabPages[StatusBookTab.Skills] = null;
        TabPages[StatusBookTab.Legend] = null;
        TabPages[StatusBookTab.Events] = null;
        TabPages[StatusBookTab.Album] = null;
        TabPages[StatusBookTab.Family] = null;

        //load the default tab so content is visible immediately
        SwitchTab(StatusBookTab.Equipment);
    }

    private PrefabPanel? CreateTabPage(StatusBookTab tab)
    {
        var prefabName = tab switch
        {
            StatusBookTab.Equipment => "_nui_eq",
            StatusBookTab.Skills    => "_nui_sk",
            StatusBookTab.Legend    => "_nui_dr",
            StatusBookTab.Events    => "_nui_ev",
            StatusBookTab.Album     => "_nui_al",
            StatusBookTab.Family    => "_nui_fm",
            _                       => null
        };

        if (prefabName is null)
            return null;

        //tab pages may not exist in all client versions
        if (DataContext.UserControls.Get(prefabName) is null)
            return null;

        PrefabPanel page = tab switch
        {
            StatusBookTab.Equipment => new SelfProfileEquipmentTab(prefabName),
            StatusBookTab.Skills    => new SelfProfileAbilityMetadataTab(prefabName),
            StatusBookTab.Legend    => new SelfProfileLegendTab(prefabName),
            StatusBookTab.Events    => new SelfProfileEventMetadataTab(prefabName),
            StatusBookTab.Album     => new SelfProfileBlankTab(prefabName),
            StatusBookTab.Family    => new SelfProfileFamilyTab(prefabName),
            _                       => new SelfProfileBlankTab(prefabName)
        };

        page.X = ContentRect.X;
        page.Y = ContentRect.Y;

        if (page is SelfProfileEquipmentTab equipTab)
        {
            equipTab.OnUnequip += slot => OnUnequip?.Invoke(slot);
            equipTab.OnGroupToggled += () => OnGroupToggled?.Invoke();
            equipTab.OnProfileTextClicked += () => OnProfileTextClicked?.Invoke();
        }

        if (page is SelfProfileAbilityMetadataTab skillsTab)
            skillsTab.OnEntryClicked += entry => OnAbilityDetailRequested?.Invoke(entry);

        if (page is SelfProfileEventMetadataTab eventsTab)
            eventsTab.OnEntryClicked += (entry, state) => OnEventDetailRequested?.Invoke(entry, state);

        return page;
    }

    public override void Dispose()
    {
        WorldState.Equipment.SlotChanged -= OnEquipmentSlotChanged;
        WorldState.Equipment.SlotCleared -= OnEquipmentSlotCleared;

        base.Dispose();
    }

    private T? GetOrCreatePage<T>(StatusBookTab tab) where T: PrefabPanel
    {
        if (TabPages.TryGetValue(tab, out var page) && page is T existing)
            return existing;

        if (page is null)
        {
            page = CreateTabPage(tab);
            TabPages[tab] = page;

            if (page is not null)
                AddChild(page);
        }

        return page as T;
    }

    public event AbilityMetadataClickedHandler? OnAbilityDetailRequested;
    public event CloseHandler? OnClose;
    public event EventMetadataClickedHandler? OnEventDetailRequested;
    public event GroupToggledHandler? OnGroupToggled;
    public event ProfileTextClickedHandler? OnProfileTextClicked;
    public event UnequipHandler? OnUnequip;

    #region Legend API
    /// <summary>
    ///     Sets the legend marks on the Legend tab page.
    /// </summary>
    public void SetLegendMarks(List<LegendMarkEntry> marks)
    {
        if (GetOrCreatePage<SelfProfileLegendTab>(StatusBookTab.Legend) is { } page)
            page.SetMarks(marks);
    }
    #endregion

    public void SwitchTab(StatusBookTab tab)
    {
        //hide current tab page
        if (TabPages.TryGetValue(ActiveTab, out var currentPage) && currentPage is not null)
            currentPage.Visible = false;

        //deselect old tab button, select new
        var oldIndex = (int)ActiveTab;
        var newIndex = (int)tab;

        if ((oldIndex < TAB_COUNT) && TabButtons[oldIndex] is not null)
            TabButtons[oldIndex]!.IsSelected = false;

        ActiveTab = tab;

        if ((newIndex < TAB_COUNT) && TabButtons[newIndex] is not null)
            TabButtons[newIndex]!.IsSelected = true;

        //lazy-load and show the new tab page
        if (!TabPages.TryGetValue(tab, out var page) || page is null)
        {
            page = CreateTabPage(tab);
            TabPages[tab] = page;

            if (page is not null)
                AddChild(page);
        }

        page?.Visible = true;
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            Hide();
            OnClose?.Invoke();
            e.Handled = true;
        }
    }

    #region Events API
    /// <summary>
    ///     Sets the event/quest entries on the Events tab page.
    /// </summary>
    public void SetEvents(
        IReadOnlyList<EventMetadataEntry> events,
        HashSet<string> completedEventIds,
        BaseClass baseClass,
        bool enableMasterQuests)
    {
        if (GetOrCreatePage<SelfProfileEventMetadataTab>(StatusBookTab.Events) is { } page)
            page.SetEvents(
                events,
                completedEventIds,
                baseClass,
                enableMasterQuests);
    }

    /// <summary>
    ///     Clears all event entries on the Events tab page.
    /// </summary>
    public void ClearEvents()
    {
        if (GetOrCreatePage<SelfProfileEventMetadataTab>(StatusBookTab.Events) is { } page)
            page.ClearAll();
    }
    #endregion

    #region Family API
    /// <summary>
    ///     Updates the Family tab with player and spouse information.
    /// </summary>
    public void SetFamilyInfo(string spouseName)
    {
        if (GetOrCreatePage<SelfProfileFamilyTab>(StatusBookTab.Family) is { } page)
            page.SetFamilyInfo(spouseName);
    }

    public FamilyList? GetFamilyMembers()
    {
        if (GetOrCreatePage<SelfProfileFamilyTab>(StatusBookTab.Family) is { } page)
            return page.GetFamilyMembers();

        return null;
    }

    public void SetFamilyMembers(FamilyList family)
    {
        if (GetOrCreatePage<SelfProfileFamilyTab>(StatusBookTab.Family) is not { } page)
            return;

        page.SetTextField(0, family.Mother);
        page.SetTextField(1, family.Father);
        page.SetTextField(2, family.Son1);
        page.SetTextField(3, family.Son2);
        page.SetTextField(4, family.Brother1);
        page.SetTextField(5, family.Brother2);
        page.SetTextField(6, family.Brother3);
        page.SetTextField(7, family.Brother4);
        page.SetTextField(8, family.Brother5);
        page.SetTextField(9, family.Brother6);
    }
    #endregion

    #region Equipment API
    private void OnEquipmentSlotChanged(EquipmentSlot slot)
    {
        var data = WorldState.Equipment.GetSlot(slot);

        if (data is null)
            return;

        var equipPage = GetOrCreateEquipmentPage();

        equipPage?.SetSlot(slot, data.Value.Sprite, data.Value.Color, data.Value.Name);
    }

    private void OnEquipmentSlotCleared(EquipmentSlot slot)
    {
        var equipPage = GetOrCreateEquipmentPage();

        equipPage?.ClearSlot(slot);
    }

    /// <summary>
    ///     Bulk-refreshes all equipment slots from the current equipment state. Typically called when the status book is
    ///     first opened.
    /// </summary>
    public void RefreshEquipment()
    {
        var equipPage = GetOrCreateEquipmentPage();

        if (equipPage is null)
            return;

        equipPage.ClearAllSlots();

        foreach ((var slot, var info) in WorldState.Equipment.GetAll())
            equipPage.SetSlot(slot, info.Sprite, info.Color, info.Name);
    }

    /// <summary>
    ///     Updates the stat labels on the equipment tab page.
    /// </summary>
    public void UpdateEquipmentStats(
        int str,
        int intel,
        int wis,
        int con,
        int dex,
        int ac)
    {
        var equipPage = GetOrCreateEquipmentPage();

        equipPage?.UpdateStats(
            str,
            intel,
            wis,
            con,
            dex,
            ac);
    }

    /// <summary>
    ///     Updates the player identity labels on the equipment tab page (name, class, clan, title).
    /// </summary>
    public void SetPlayerInfo(
        string name,
        string className = "",
        string clanName = "",
        string clanTitle = "",
        string title = "")
    {
        var equipPage = GetOrCreateEquipmentPage();

        equipPage?.SetPlayerInfo(
            name,
            className,
            clanName,
            clanTitle,
            title);
    }

    /// <summary>
    ///     Renders the paperdoll from the player's current appearance (same full AislingRenderer as world, frozen at idle).
    /// </summary>
    public void SetPaperdoll(AislingRenderer renderer, in AislingAppearance appearance)
    {
        var equipPage = GetOrCreateEquipmentPage();

        equipPage?.SetPaperdoll(renderer, in appearance);
    }

    /// <summary>
    ///     Sets the emoticon/social status display on the equipment page.
    /// </summary>
    public void SetEmoticonState(byte state, string statusText)
    {
        var equipPage = GetOrCreateEquipmentPage();

        equipPage?.SetEmoticonState(state, statusText);
    }

    /// <summary>
    ///     Toggles the group button between enabled and disabled states.
    /// </summary>
    public void SetGroupOpen(bool groupOpen)
    {
        var equipPage = GetOrCreateEquipmentPage();

        equipPage?.SetGroupOpen(groupOpen);
    }

    /// <summary>
    ///     Sets the nation icon and text on the equipment page.
    /// </summary>
    public void SetNation(byte nationId)
    {
        var equipPage = GetOrCreateEquipmentPage();

        equipPage?.SetNation(nationId);
    }

    public bool ContainsEquipmentSlotPoint(int screenX, int screenY)
    {
        var equipPage = GetOrCreateEquipmentPage();

        return equipPage?.ContainsEquipmentSlotPoint(screenX, screenY) ?? false;
    }

    /// <summary>
    ///     Gets the current profile text from the equipment tab's editable text box.
    /// </summary>
    public string GetProfileText()
    {
        var equipPage = GetOrCreateEquipmentPage();

        return equipPage?.ProfileText ?? string.Empty;
    }

    /// <summary>
    ///     Sets the profile text on the equipment tab's editable text box.
    /// </summary>
    public void SetProfileText(string text)
    {
        var equipPage = GetOrCreateEquipmentPage();

        equipPage?.SetProfileText(text);
    }

    private SelfProfileEquipmentTab? GetOrCreateEquipmentPage()
    {
        if (TabPages.TryGetValue(StatusBookTab.Equipment, out var page) && page is SelfProfileEquipmentTab existing)
            return existing;

        //force lazy-create if not yet created
        if (page is null)
        {
            page = CreateTabPage(StatusBookTab.Equipment);
            TabPages[StatusBookTab.Equipment] = page;

            if (page is not null)
                AddChild(page);
        }

        return page as SelfProfileEquipmentTab;
    }
    #endregion

    #region Skills API
    /// <summary>
    ///     Populates the Skills tab with parsed ability metadata.
    /// </summary>
    public void SetAbilityMetadata(AbilityMetadata metadata)
    {
        if (GetOrCreatePage<SelfProfileAbilityMetadataTab>(StatusBookTab.Skills) is { } page)
            page.SetAbilityMetadata(metadata);
    }

    /// <summary>
    ///     Clears all skill/spell entries on the Skills tab.
    /// </summary>
    public void ClearSkills()
    {
        if (GetOrCreatePage<SelfProfileAbilityMetadataTab>(StatusBookTab.Skills) is { } page)
            page.ClearAll();
    }
    #endregion
}