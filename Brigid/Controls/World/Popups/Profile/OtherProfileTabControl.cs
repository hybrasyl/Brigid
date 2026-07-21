#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Data;
using Brigid.Data.Repositories;
using Brigid.Models;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Profile;

/// <summary>
///     Tab-based profile viewer for other players, using the _nui prefab. Only enables Equipment and Legend tabs (plus
///     Close). Equipment tab uses _nui_eqa (no stats), legend tab reuses <see cref="SelfProfileLegendTab" />.
/// </summary>
public sealed class OtherProfileTabControl : PrefabPanel
{
    private static readonly (string ControlName, StatusBookTab Tab)[] EnabledTabs =
    [
        ("TAB_INTRO", StatusBookTab.Equipment),
        ("TAB_LEGEND", StatusBookTab.Legend)
    ];

    private readonly Rectangle ContentRect;
    private readonly UIButton?[] TabButtons = new UIButton?[EnabledTabs.Length];
    private readonly Dictionary<StatusBookTab, PrefabPanel?> TabPages = [];

    private StatusBookTab ActiveTab = StatusBookTab.Equipment;
    private bool GroupButtonWired;
    private uint ShownId;

    public UIButton? CloseButton { get; }

    public OtherProfileTabControl()
        : base("_nui", false)
    {
        Name = "OtherProfile";
        Visible = false;
        UsesControlStack = true;
        X = 0;
        Y = 0;

        ContentRect = GetRect("CONTENT");

        //close button
        CloseButton = CreateButton("TAB_CLOSE");

        if (CloseButton is not null)
            CloseButton.Clicked += Hide;

        //only create equipment + legend tab buttons
        var cache = UiRenderer.Instance!;

        for (var i = 0; i < EnabledTabs.Length; i++)
        {
            (var controlName, var tab) = EnabledTabs[i];

            if (CreateButton(controlName) is not { } tabBtn)
                continue;

            TabButtons[i] = tabBtn;
            tabBtn.CenterTexture = true;

            //prefab image is big/selected — swap to selectedtexture
            tabBtn.SelectedTexture = tabBtn.NormalTexture;

            //load small/normal texture from _nui_tb1.spf
            var frameIndex = (int)tab;

            if (PrefabSet.Contains(controlName))
            {
                var prefab = PrefabSet[controlName];

                if (prefab.Control.Images is { Count: > 0 })
                    frameIndex = prefab.Control.Images[0].FrameIndex;
            }

            tabBtn.NormalTexture = cache.GetSpfTexture("_nui_tb1.spf", frameIndex);

            var capturedTab = tab;
            tabBtn.Clicked += () => SwitchTab(capturedTab);

            tabBtn.IsSelected = tab == ActiveTab;
            tabBtn.ZIndex = 1;
        }

        CloseButton?.ZIndex = 1;

        TabPages[StatusBookTab.Equipment] = null;
        TabPages[StatusBookTab.Legend] = null;

        SwitchTab(StatusBookTab.Equipment);
    }

    private PrefabPanel? CreateTabPage(StatusBookTab tab)
    {
        var prefabName = tab switch
        {
            StatusBookTab.Equipment => "_nui_eqa",
            StatusBookTab.Legend    => "_nui_dr",
            _                       => null
        };

        if (prefabName is null)
            return null;

        if (DataContext.UserControls.Get(prefabName) is null)
            return null;

        PrefabPanel page = tab switch
        {
            StatusBookTab.Equipment => new OtherProfileEquipmentTab(prefabName),
            StatusBookTab.Legend    => new SelfProfileLegendTab(prefabName),
            _                       => new SelfProfileBlankTab(prefabName)
        };

        page.X = ContentRect.X;
        page.Y = ContentRect.Y;

        return page;
    }

    private static int FindTabIndex(StatusBookTab tab)
    {
        for (var i = 0; i < EnabledTabs.Length; i++)
            if (EnabledTabs[i].Tab == tab)
                return i;

        return -1;
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

    public new void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
    }
    public event GroupInviteRequestedHandler? OnGroupInviteRequested;

    /// <summary>
    ///     Populates and shows the other player's profile.
    /// </summary>
    public void Show(ProfilePacket args, List<LegendMarkEntry> legendMarks, AislingRenderer aislingRenderer)
    {
        ShownId = args.Id;

        //equipment tab
        var equipPage = GetOrCreatePage<OtherProfileEquipmentTab>(StatusBookTab.Equipment);

        if (equipPage is not null)
        {
            if (!GroupButtonWired)
            {
                equipPage.OnGroupInviteRequested += name => OnGroupInviteRequested?.Invoke(name);
                GroupButtonWired = true;
            }

            var socialStatus = (Chaos.DarkAges.Definitions.SocialStatus)(byte)args.SocialStatus;

            equipPage.SetPlayerInfo(
                args.Name,
                args.ClassName,
                args.GuildName,
                args.GuildRank,
                args.Title);

            equipPage.SetEquipment(args.Equipment);
            equipPage.SetGroupOpen(args.GroupOpen);
            equipPage.SetNation(args.NationFlag);
            equipPage.SetEmoticonState((byte)socialStatus, UiComponentRepository.GetSocialStatusName(socialStatus));

            equipPage.SetProfileText(args.ProfileText);
            equipPage.SetPortrait(args.Portrait);

            //paperdoll from the entity's current appearance on the map
            var entity = WorldState.GetEntity(args.Id);

            if (entity?.Appearance is { } appearance)
                equipPage.SetPaperdoll(aislingRenderer, in appearance);
        }

        //legend tab
        var legendPage = GetOrCreatePage<SelfProfileLegendTab>(StatusBookTab.Legend);

        legendPage?.SetMarks(legendMarks);

        SwitchTab(StatusBookTab.Equipment);
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
    }

    /// <summary>
    ///     Re-renders the paperdoll from the inspected player's live appearance when it changes while their profile is
    ///     open (e.g. they equip/remove armor). No-op unless this profile is currently showing that same aisling —
    ///     mirrors the self-profile refresh so an inspected player's paperdoll never goes stale/partial.
    /// </summary>
    public void RefreshPaperdoll(uint id, AislingRenderer aislingRenderer, in AislingAppearance appearance)
    {
        if (!Visible || id != ShownId)
            return;

        var equipPage = GetOrCreatePage<OtherProfileEquipmentTab>(StatusBookTab.Equipment);

        equipPage?.SetPaperdoll(aislingRenderer, in appearance);
    }

    public void SwitchTab(StatusBookTab tab)
    {
        //only allow equipment or legend
        if (tab is not (StatusBookTab.Equipment or StatusBookTab.Legend))
            return;

        //hide current tab page
        if (TabPages.TryGetValue(ActiveTab, out var currentPage) && currentPage is not null)
            currentPage.Visible = false;

        //deselect old tab, select new
        var oldIndex = FindTabIndex(ActiveTab);
        var newIndex = FindTabIndex(tab);

        if ((oldIndex >= 0) && TabButtons[oldIndex] is not null)
            TabButtons[oldIndex]!.IsSelected = false;

        ActiveTab = tab;

        if ((newIndex >= 0) && TabButtons[newIndex] is not null)
            TabButtons[newIndex]!.IsSelected = true;

        //lazy-load and show
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
            e.Handled = true;
        }
    }
}