#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.World.Hud.Panel;
using Brigid.Controls.World.ViewPort;
using Brigid.Data;
using Brigid.Systems;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud;

/// <summary>
///     Main game HUD frame loaded from _nbk_s.txt (small/normal layout). Defines viewport bounds, chat area, inventory
///     area, HP/MP orbs, info text fields, and all action buttons. Manages the shared "center bottom" tab area where
///     Inventory/Skills/Spells/Chat/Stats panels swap.
/// </summary>
public sealed class WorldHudControl : PrefabPanel, IWorldHud
{
    private readonly PlayerAttributes AttributesState;
    private readonly UILabel CoordsLabel;
    private readonly UILabel? DescriptionLabel;

    //hp/mp numeric displays
    private readonly UILabel HpNumLabel;

    //hp/mp orbs
    private readonly UIProgressBar HpOrb;
    private readonly UILabel MpNumLabel;
    private readonly UIProgressBar MpOrb;

    //orange bar
    private readonly OrangeBarControl OrangeBar;
    private readonly PersistentMessageControl PersistentMessage;

    //info text
    private readonly UILabel PlayerNameLabel;

    private readonly UILabel? ServerNameLabel;

    //tab panel system — shared center-bottom area
    private readonly UIPanel?[] TabPanels = new UIPanel?[Enum.GetValues<HudTab>()
                                                             .Length];

    //shared tooltip label for hud utility buttons
    private readonly UILabel TooltipLabel;

    private readonly UILabel WeightLabel;

    private int Hp = int.MinValue;
    private int Mp = int.MinValue;
    private int WeightCurrent = int.MinValue;
    private int WeightMax = int.MinValue;
    private int CoordX = int.MinValue;
    private int CoordY = int.MinValue;
    private readonly UILabel ZoneNameLabel;

    //ping indicator — _nping.spf is a single dot in four colors: [0]=red, [1]=orange, [2]=green, [3]=blue
    private readonly UIImage? PingIcon;
    private readonly Texture2D?[] PingFrames = new Texture2D?[4];
    public HudTab ActiveTab { get; private set; } = HudTab.Inventory;
    public ChatPanel ChatDisplay { get; private set; } = null!;
    public event ClickedHandler? InventoryReactivated;
    public SystemMessagePanel MessageHistory { get; private set; } = null!;
    public ExtendedStatsPanel ExtendedStatsPanel { get; private set; } = null!;
    public InventoryPanel Inventory { get; private set; } = null!;
    public SkillBookPanel SkillBook { get; private set; } = null!;
    public SkillBookPanel SkillBookAlt { get; private set; } = null!;
    public SpellBookPanel SpellBook { get; private set; } = null!;
    public SpellBookPanel SpellBookAlt { get; private set; } = null!;
    public StatsPanel StatsPanel { get; private set; } = null!;
    public ToolsPanel Tools { get; private set; } = null!;
    public UIButton? BulletinButton { get; }
    public UIButton? ChangeLayoutButton { get; }
    public UIButton? CharScreenshotButton { get; }

    //chat
    public ChatInputControl ChatInput { get; }

    public EffectBarControl EffectBar { get; }
    public UIButton? EmoteButton { get; }
    public UIButton? ExpandButton { get; }
    public UIButton? GroupButton { get; }
    public UIButton? GroupIndicator { get; }

    //buttons — lower right
    public UIButton? HelpButton { get; }

    //inventory
    public Rectangle InventoryBounds { get; }

    //inventory tab buttons
    public UIButton?[] InventoryTabButtons { get; } = new UIButton?[6];
    public UIButton? LegendButton { get; }

    //buttons — notification indicators
    public MailButton? MailButton { get; }

    //buttons — right side
    public UIButton? OptionButton { get; }
    public UIButton? ScreenshotButton { get; }
    public UIButton? SettingsButton { get; }
    public UIButton? TownMapButton { get; }

    public UIButton? UsersButton { get; }

    //viewport — the area where the game world renders
    public Rectangle ViewportBounds { get; }

    public WorldHudControl(string prefabName = "_nbk_s")
        : base(prefabName, false)
    {
        Name = "GameHud";
        IsPassThrough = true;

        //viewport rect — where the game world renders
        ViewportBounds = GetRect("MAP");

        if (ViewportBounds == Rectangle.Empty)
            ViewportBounds = GetRect("EMPTY");

        //chat input control (say) — clicks must not steal focus; only hotkey/macro activation focuses it
        ChatInput = new ChatInputControl(PrefabSet)
        {
            IsHitTestVisible = false
        };
        AddChild(ChatInput);

        ChatInput.FocusChanged += focused =>
        {
            //gate hit-testing on focus: idle clicks fall through,
            //but drag-select works normally once focused via hotkey.
            ChatInput.IsHitTestVisible = focused;

            if (focused)
                DescriptionLabel?.Text = string.Empty;
        };

        //inventory area
        InventoryBounds = GetRect("InventoryRect");

        //hp/mp orbs
        HpOrb = CreateProgressBar("ORB_HP")!;
        MpOrb = CreateProgressBar("ORB_MP")!;

        //hp/mp numeric text — zindex=1 so labels always render above overlapping orbs after re-sorts
        HpNumLabel = CreateLabel("NUM_HP", HorizontalAlignment.Right)!;
        HpNumLabel.ZIndex = 1;
        MpNumLabel = CreateLabel("NUM_MP", HorizontalAlignment.Right)!;
        MpNumLabel.ZIndex = 1;

        //info text areas
        PlayerNameLabel = CreateLabel("SZ_ID", HorizontalAlignment.Center)!;
        ZoneNameLabel = CreateLabel("SZ_ZONE", HorizontalAlignment.Center)!;
        ZoneNameLabel.ForegroundColor = LegendColors.White;
        ZoneNameLabel.ShrinkToFit = false;

        WeightLabel = CreateLabel("SZ_WEIGHT", HorizontalAlignment.Center)!;
        WeightLabel.PaddingLeft = 0;
        WeightLabel.PaddingRight = 0;
        CoordsLabel = CreateLabel("SZ_XY", HorizontalAlignment.Center)!;
        CoordsLabel.ShrinkToFit = false;
        ServerNameLabel = CreateLabel("SZ_SERVER", HorizontalAlignment.Center);
        DescriptionLabel = CreateLabel("SZ_DESCRIPTION");

        //ping indicator — prefab control supplies position; frames are loaded straight from _nping.spf
        PingIcon = CreateImage("PING");

        if (PingIcon is not null)
        {
            var uiCache = UiRenderer.Instance!;

            for (var i = 0; i < PingFrames.Length; i++)
                PingFrames[i] = uiCache.GetSpfTexture("_nping.spf", i);

            LatencyMonitor.LatencyChanged += OnLatencyChanged;
            OnLatencyChanged();

            PingIcon.Hovered += _ => ShowPingTooltip();
            PingIcon.Unhovered += _ => HideTooltip();
            PingIcon.VisibilityChanged += _ => HideTooltip();
        }

        //effect bar — right side, above the option button
        EffectBar = new EffectBarControl
        {
            X = 618,
            Y = 2
        };
        AddChild(EffectBar);

        //buttons — right side
        OptionButton = CreateButton("BTN_OPTION");
        BulletinButton = CreateButton("BTN_BULLETIN");
        UsersButton = CreateButton("BTN_USERS");
        ExpandButton = CreateButton("BTN_EXPAND");
        ChangeLayoutButton = CreateButton("BTN_CHANGELAYOUT");

        //btn_changelayout is a stateful indicator — small hud shows the normal frame, and the
        //pressed frame is only shown by the large hud. strip the press-state visual here.
        ChangeLayoutButton?.PressedTexture = null;

        //buttons — lower right
        HelpButton = CreateButton("BTN_HELP");
        LegendButton = CreateButton("BTN_LEGEND");
        TownMapButton = CreateButton("BTN_TOWNMAP");
        GroupButton = CreateButton("BTN_GROUP");

        //notification indicators — mail button pulses a yellow outline while HasUnreadMail is set
        MailButton = CreateButton<MailButton>("CMail");
        GroupIndicator = CreateButton("CGroup");
        ScreenshotButton = CreateButton("CShot");

        //emote/status button
        EmoteButton = CreateButton("BTN_EMOT");

        //conditional buttons (may not exist in all client versions)
        SettingsButton = CreateButton("BTN_SETTING");
        CharScreenshotButton = CreateButton("BTN_SCREENSHOT");

        //shared tooltip label for hud utility buttons — anchored above the hovered button
        TooltipLabel = new UILabel
        {
            Name = "HudTooltip",
            Visible = false,
            IsHitTestVisible = false,
            PaddingLeft = 1,
            PaddingTop = 1,
            BackgroundColor = new Color(0, 0, 0, 128),
            BorderColor = LegendColors.White,
            ForegroundColor = LegendColors.White,
            ZIndex = 100
        };
        AddChild(TooltipLabel);

        //wire tooltips for the 6 utility buttons on the small hud
        WireTooltip(LegendButton, "Legend");
        WireTooltip(TownMapButton, "Map");
        WireTooltip(GroupButton, "Group");
        WireTooltip(SettingsButton, "Settings");
        WireTooltip(CharScreenshotButton, "ScreenShot");
        WireTooltip(HelpButton, "Hotkeys");

        //inventory tab buttons (btn_inv0 through btn_inv5)
        for (var i = 0; i < 6; i++)
            InventoryTabButtons[i] = CreateButton($"BTN_INV{i}");

        //_nbk_s.txt has BTN_INV4 with rect H=23 (others are H=24). UpdateClipRect uses Height for clipping,
        //so the 24-tall texture's bottom row gets clipped, leaving a 1px gap before BTN_INV5. Normalize.
        foreach (var btn in InventoryTabButtons)
            if (btn?.NormalTexture is { } tex)
            {
                if (btn.Height < tex.Height)
                    btn.Height = tex.Height;

                if (btn.Width < tex.Width)
                    btn.Width = tex.Width;
            }

        //persistent message — floating text, top-right of viewport
        PersistentMessage = new PersistentMessageControl(ViewportBounds);
        AddChild(PersistentMessage);

        //resolve inventory background textures from prefab for tab panels
        var cache = UiRenderer.Instance!;
        var invBgTexture = GetPrefabTexture(cache, "InventoryBackground");
        var invBgExpandedTexture = GetPrefabTexture(cache, "InventoryBackgroundExpanded");
        var livingBgTexture = GetPrefabTexture(cache, "LivingInventoryBackground");

        //orange bar — created before tab panels (messagehistorypanel needs its history list),
        //but added as child after so it draws on top
        OrangeBar = new OrangeBarControl(PrefabSet);

        //tab panels — shared center-bottom area
        CreateTabPanels(invBgTexture, invBgExpandedTexture, livingBgTexture);

        //orange bar renders above collapsed tab panels (z=0) but below expanded ones (z=10)
        OrangeBar.ZIndex = 1;
        AddChild(OrangeBar);

        //subscribe to attributes changes for hp/mp/weight/stats
        AttributesState = WorldState.Attributes;
        AttributesState.Changed += OnAttributesChanged;

        if (AttributesState.Current is not null)
            OnAttributesChanged();
    }

    public override void Dispose()
    {
        AttributesState.Changed -= OnAttributesChanged;
        LatencyMonitor.LatencyChanged -= OnLatencyChanged;

        base.Dispose();
    }

    private void OnLatencyChanged()
    {
        if (PingIcon is null)
            return;

        var frameIndex = LatencyMonitor.LatencyMs switch
        {
            null   => 3, // blue — no data / error
            < 100  => 2, // green — good
            < 1000 => 1, // orange — mid
            _      => 0  // red — 1000ms+
        };

        PingIcon.Texture = PingFrames[frameIndex];
    }

    private void OnAttributesChanged()
    {
        if (AttributesState.Current is not { } attrs)
            return;

        UpdateHp((int)attrs.CurrentHp, (int)attrs.MaxHp);
        UpdateMp((int)attrs.CurrentMp, (int)attrs.MaxMp);
        SetWeight(attrs.CurrentWeight, attrs.MaxWeight);
        StatsPanel.UpdateAttributes(attrs);
        ExtendedStatsPanel.UpdateAttributes(attrs);

        MailButton?.SetAnimating(attrs.HasUnreadMail);
    }

    #region Helpers
    private static int GetTabButtonIndex(HudTab tab)
        => tab switch
        {
            HudTab.Inventory                     => 0,
            HudTab.Skills or HudTab.SkillsAlt    => 1,
            HudTab.Spells or HudTab.SpellsAlt    => 2,
            HudTab.Chat or HudTab.MessageHistory => 3,
            HudTab.Stats or HudTab.ExtendedStats => 4,
            HudTab.Tools                         => 5,
            _                                    => -1
        };

    private Texture2D? GetPrefabTexture(UiRenderer cache, string controlName)
        => PrefabSet.Contains(controlName) && (PrefabSet[controlName].Images.Count > 0)
            ? cache.GetPrefabTexture(PrefabSet.Name, controlName, 0)
            : null;

    private void WireTooltip(UIButton? btn, string label)
    {
        if (btn is null)
            return;

        btn.TooltipText = label;
        btn.Hovered += ShowTooltip;
        btn.Unhovered += _ => HideTooltip();
        btn.VisibilityChanged += _ => HideTooltip();
    }

    private void ShowTooltip(UIButton btn)
    {
        if (btn.TooltipText is not { Length: > 0 } text)
            return;

        TooltipLabel.Text = text;
        TooltipLabel.Width = TextRenderer.MeasureWidth(text) + 4;
        TooltipLabel.Height = TextRenderer.CHAR_HEIGHT + 4;

        var x = btn.X;
        var y = btn.Y + 5 - TooltipLabel.Height;

        var rightLimit = ChaosGame.VIRTUAL_WIDTH - TooltipLabel.Width;

        if (x > rightLimit)
            x = rightLimit;

        if (x < 0)
            x = 0;

        TooltipLabel.X = x;
        TooltipLabel.Y = y;
        TooltipLabel.Visible = true;
    }

    private void HideTooltip() => TooltipLabel.Visible = false;

    private void ShowPingTooltip()
    {
        if (PingIcon is null)
            return;

        var ms = LatencyMonitor.LatencyMs;
        var text = ms.HasValue ? $"{ms.Value} ms (TCP)" : "— (no data)";

        TooltipLabel.Text = text;
        TooltipLabel.Width = TextRenderer.MeasureWidth(text) + 4;
        TooltipLabel.Height = TextRenderer.CHAR_HEIGHT + 4;

        var x = PingIcon.X;
        var y = PingIcon.Y + 5 - TooltipLabel.Height;

        var rightLimit = ChaosGame.VIRTUAL_WIDTH - TooltipLabel.Width;

        if (x > rightLimit)
            x = rightLimit;

        if (x < 0)
            x = 0;

        TooltipLabel.X = x;
        TooltipLabel.Y = y;
        TooltipLabel.Visible = true;
    }
    #endregion

    #region Tab Panel Management
    /// <summary>
    ///     Switches the active tab in the shared center-bottom area. Hides the current panel and shows the new one.
    /// </summary>
    public void ShowTab(HudTab tab)
    {
        //collapse inventory when switching away so it doesn't persist expanded state
        if (Inventory.IsExpanded && (tab != HudTab.Inventory))
            Inventory.SetExpanded(false);

        //hide all unique panels to prevent stale visibility
        foreach (var panel in TabPanels)
            panel?.Visible = false;

        //force hover exit on inventory when switching away — tooltip won't clear on its own
        //since the panel stops updating when hidden
        if ((ActiveTab == HudTab.Inventory) && (tab != HudTab.Inventory))
            Inventory.ForceHoverExit();

        //deselect old tab button
        var oldButtonIndex = GetTabButtonIndex(ActiveTab);

        if ((oldButtonIndex >= 0) && (oldButtonIndex < InventoryTabButtons.Length))
            if (InventoryTabButtons[oldButtonIndex] is { } oldBtn)
                oldBtn.IsSelected = false;

        ActiveTab = tab;

        var next = TabPanels[(int)tab];

        next?.Visible = true;

        //select new tab button
        var newButtonIndex = GetTabButtonIndex(tab);

        if ((newButtonIndex >= 0) && (newButtonIndex < InventoryTabButtons.Length))
            if (InventoryTabButtons[newButtonIndex] is { } newBtn)
                newBtn.IsSelected = true;
    }

    private void CreateTabPanels(Texture2D? invBgTexture, Texture2D? invBgExpandedTexture, Texture2D? livingBgTexture)
    {
        //tab panels are placed at inventorybounds (absolute screen position from inventoryrect control).
        var tabRect = InventoryBounds;

        //inventory (a)
        Inventory = new InventoryPanel(PrefabSet, invBgTexture, invBgExpandedTexture);
        RegisterTab(HudTab.Inventory, Inventory, tabRect);

        //skills (s) / skills alt (shift+s)
        SkillBook = new SkillBookPanel(PrefabSet, background: invBgTexture);

        SkillBookAlt = new SkillBookPanel(PrefabSet, SkillBookPage.Page2, invBgTexture);
        RegisterTab(HudTab.Skills, SkillBook, tabRect);
        RegisterTab(HudTab.SkillsAlt, SkillBookAlt, tabRect);

        //spells (d) / spells alt (shift+d)
        SpellBook = new SpellBookPanel(PrefabSet, background: invBgTexture);

        SpellBookAlt = new SpellBookPanel(PrefabSet, SkillBookPage.Page2, invBgTexture);
        RegisterTab(HudTab.Spells, SpellBook, tabRect);
        RegisterTab(HudTab.SpellsAlt, SpellBookAlt, tabRect);

        //chat (f)
        var chatDisplayBounds = GetRect("ChattingRect");
        ChatDisplay = new ChatPanel(chatDisplayBounds, tabRect);

        //the input renders at the size the display computed, so the two can never disagree
        ChatInput.Display = ChatDisplay;
        RegisterTab(HudTab.Chat, ChatDisplay, tabRect);

        //stats (g) / extended stats (shift+g) — both load from _nstatus prefab
        var statusPrefabSet = DataContext.UserControls.Get("_nstatus")!;
        StatsPanel = new StatsPanel(statusPrefabSet);
        ExtendedStatsPanel = new ExtendedStatsPanel(statusPrefabSet);
        RegisterTab(HudTab.Stats, StatsPanel, tabRect);
        RegisterTab(HudTab.ExtendedStats, ExtendedStatsPanel, tabRect);

        //tools (h) — composite with skill page-3 left half + spell page-3 right half
        Tools = new ToolsPanel(PrefabSet, livingBgTexture);
        RegisterTab(HudTab.Tools, Tools, tabRect);

        //message history (shift+f) — displays orange bar messages in a tab panel
        var msgHistoryBounds = GetRect("ChattingRect");

        MessageHistory = new SystemMessagePanel(msgHistoryBounds, tabRect, WorldState.Chat.GetOrangeBarHistory());
        RegisterTab(HudTab.MessageHistory, MessageHistory, tabRect);

        //wire tab button clicks: btn_inv0=a, btn_inv1=s, btn_inv2=d, btn_inv3=f, btn_inv4=g, btn_inv5=h
        HudTab[] tabMapping =
        [
            HudTab.Inventory,
            HudTab.Skills,
            HudTab.Spells,
            HudTab.Chat,
            HudTab.Stats,
            HudTab.Tools
        ];

        for (var i = 0; i < InventoryTabButtons.Length; i++)
            if (InventoryTabButtons[i] is not null && (i < tabMapping.Length))
            {
                var tab = tabMapping[i];

                //mirror keyboard semantics: shift+click and click-while-active behaviors
                InventoryTabButtons[i]!.ClickedWithModifiers += modifiers => HandleTabActivation(tab, (modifiers & KeyModifiers.Shift) != 0);
            }

        //default to inventory visible
        ShowTab(HudTab.Inventory);
    }

    private void RegisterTab(HudTab tab, UIPanel panel, Rectangle tabRect)
    {
        panel.X = tabRect.X;
        panel.Y = tabRect.Y;

        //ensure tab panels have bounds for hit-testing (background size or tab rect as fallback)
        if (panel.Width == 0)
            panel.Width = panel.Background?.Width ?? tabRect.Width;

        if (panel.Height == 0)
            panel.Height = panel.Background?.Height ?? tabRect.Height;

        panel.Visible = false;

        if (panel is PanelBase panelBase)
            panelBase.Tab = tab;

        TabPanels[(int)tab] = panel;
        AddChild(panel);
    }
    #endregion

    #region Public Methods
    public void UpdateHp(int current, int max)
    {
        if (current != Hp)
        {
            Hp = current;
            HpNumLabel.Text = current.ToString();
        }

        HpOrb.UpdateValue(current, max);
    }

    public void UpdateMp(int current, int max)
    {
        if (current != Mp)
        {
            Mp = current;
            MpNumLabel.Text = current.ToString();
        }

        MpOrb.UpdateValue(current, max);
    }

    public string PlayerName { get; private set; } = string.Empty;

    public void SetGroupOpen(bool groupOpen)
    {
        if (GroupIndicator is null)
            return;

        var cache = UiRenderer.Instance!;

        if (groupOpen)
        {
            GroupIndicator.NormalTexture = cache.GetPrefabTexture(PrefabSet.Name, "CGroup", 0);
            GroupIndicator.PressedTexture = cache.GetPrefabTexture(PrefabSet.Name, "CGroup", 1);
        } else
        {
            GroupIndicator.NormalTexture = cache.GetSpfTexture("_ni_gr0.spf");
            GroupIndicator.PressedTexture = cache.GetSpfTexture("_ni_gr0.spf", 1);
        }
    }

    public void SetPlayerName(string name)
    {
        PlayerName = name;
        PlayerNameLabel.Text = name;
    }

    public void SetZoneName(string zone) => ZoneNameLabel.Text = zone;

    public void SetWeight(int current, int max)
    {
        if ((current == WeightCurrent) && (max == WeightMax))
            return;

        WeightCurrent = current;
        WeightMax = max;
        WeightLabel.Text = $"{current} / {max}";
    }

    public void SetCoords(int x, int y)
    {
        if ((x == CoordX) && (y == CoordY))
            return;

        CoordX = x;
        CoordY = y;
        CoordsLabel.Text = $"{x}, {y}";
    }

    public void SetServerName(string name) => ServerNameLabel?.Text = name;

    /// <summary>
    ///     Shows a description text in the SZ_DESCRIPTION area (item/skill/spell name on hover). Color 0x14 = green/teal,
    ///     matching original client.
    /// </summary>
    public void SetDescription(string? text)
    {
        //don't show hover descriptions while chat input is focused — they overlap
        if (ChatInput.IsFocused && !string.IsNullOrEmpty(text))
            return;

        DescriptionLabel?.Text = text ?? string.Empty;
    }

    public bool IsOrangeBarDragging => OrangeBar.IsDragging;

    /// <summary>
    ///     Small HUD: only inventory supports expand (3 rows → 5 rows).
    /// </summary>
    public void ToggleExpand() => Inventory.SetExpanded(!Inventory.IsExpanded);

    /// <inheritdoc />
    public bool CollapseExpanded()
    {
        if (!Inventory.IsExpanded)
            return false;

        Inventory.SetExpanded(false);

        return true;
    }

    public void HandleTabActivation(HudTab tab, bool shift)
    {
        switch (tab)
        {
            case HudTab.Inventory:
                if (shift)
                {
                    if (ActiveTab != HudTab.Inventory)
                        ShowTab(HudTab.Inventory);

                    ToggleExpand();
                } else if (ActiveTab == HudTab.Inventory)
                    InventoryReactivated?.Invoke();
                else
                    ShowTab(HudTab.Inventory);

                break;

            case HudTab.Skills:
            case HudTab.SkillsAlt:
            {
                var alt = shift || (!ClientSettings.UseShiftKeyForAltPanels && (ActiveTab == HudTab.Skills));
                ShowTab(alt ? HudTab.SkillsAlt : HudTab.Skills);

                break;
            }

            case HudTab.Spells:
            case HudTab.SpellsAlt:
            {
                var alt = shift || (!ClientSettings.UseShiftKeyForAltPanels && (ActiveTab == HudTab.Spells));
                ShowTab(alt ? HudTab.SpellsAlt : HudTab.Spells);

                break;
            }

            case HudTab.Chat:
            case HudTab.MessageHistory:
                if (shift)
                {
                    ShowTab(HudTab.MessageHistory);
                    MessageHistory.ScrollToBottom();
                } else
                {
                    ShowTab(HudTab.Chat);
                    ChatDisplay.ScrollToBottom();
                }

                break;

            case HudTab.Stats:
            case HudTab.ExtendedStats:
                ShowTab(shift ? HudTab.ExtendedStats : HudTab.Stats);

                break;

            case HudTab.Tools:
                ShowTab(HudTab.Tools);

                break;
        }
    }

    public void ShowPersistentMessage(string text) => PersistentMessage.SetMessage(text);
    #endregion
}