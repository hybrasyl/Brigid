#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.World.Hud.Panel;
using Brigid.Controls.World.Hud.Panel.Slots;
using Brigid.Controls.World.ViewPort;
using Brigid.Data;
using Brigid.Systems;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud;

/// <summary>
///     Large/expanded game HUD frame loaded from _nbk_l.txt. Same functionality as <see cref="WorldHudControl" /> but with
///     a different layout (larger viewport, repositioned controls). Once working, common code should be extracted into a
///     shared base class.
/// </summary>
public sealed class LargeWorldHudControl : PrefabPanel, IWorldHud
{
    private const int LARGE_NORMAL_SLOTS = 1 * 12;
    private const int LARGE_EXPANDED_SKILL_SPELL_SLOTS = 3 * 12;
    private const int COMPACT_VISIBLE_SLOTS_PER_HALF = 6;
    private const int EXPANDED_VISIBLE_SLOTS_PER_HALF = 18;

    private readonly PlayerAttributes AttributesState;
    private readonly UILabel CoordsLabel; 
    private readonly UILabel? DescriptionLabel;
    private readonly Rectangle ExpandedChatBounds;
    private readonly UIImage? ExtendedTabFrame;
    private readonly UILabel HpNumLabel;
    private readonly UIProgressBar HpOrb;
    private readonly UILabel MpNumLabel;
    private readonly UIProgressBar MpOrb;
    private readonly OrangeBarControl OrangeBar;
    private readonly PersistentMessageControl PersistentMessage;
    private readonly UILabel PlayerNameLabel;
    private readonly UILabel? ServerNameLabel;
    private readonly UILabel TooltipLabel;

    private readonly UIPanel?[] TabPanels = new UIPanel?[Enum.GetValues<HudTab>()
                                                             .Length];

    private readonly UILabel WeightLabel;
    private readonly UILabel ZoneNameLabel;

    //ping indicator — _nping.spf is a single dot in four colors: [0]=red, [1]=orange, [2]=green, [3]=blue
    private readonly UIImage? PingIcon;
    private readonly Texture2D?[] PingFrames = new Texture2D?[4];
    private int Hp = int.MinValue;
    private int Mp = int.MinValue;
    private int WeightCurrent = int.MinValue;
    private int WeightMax = int.MinValue;
    private int CoordX = int.MinValue;
    private int CoordY = int.MinValue;
    private bool Expanded;
    private Rectangle NormalChatBounds;

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
    public ChatInputControl ChatInput { get; }
    public EffectBarControl EffectBar { get; }
    public UIButton? EmoteButton { get; }
    public UIButton? ExpandButton { get; }
    public UIButton? GroupButton { get; }
    public UIButton? GroupIndicator { get; }
    public UIButton? HelpButton { get; }
    public Rectangle InventoryBounds { get; }
    public UIButton?[] InventoryTabButtons { get; } = new UIButton?[6];
    public UIButton? LegendButton { get; }
    public MailButton? MailButton { get; }
    public UIButton? OptionButton { get; }
    public UIButton? ScreenshotButton { get; }
    public UIButton? SettingsButton { get; }
    public UIButton? TownMapButton { get; }
    public UIButton? UsersButton { get; }
    public Rectangle ViewportBounds { get; }

    public LargeWorldHudControl()
        : base("_nbk_l", false)
    {
        Name = "GameHudLarge";
        Visible = false;
        IsPassThrough = true;

        //viewport rect
        ViewportBounds = GetRect("MAP");

        if (ViewportBounds == Rectangle.Empty)
            ViewportBounds = GetRect("EMPTY");

        //chat input control (say) — overlays the world; clicks must pass through to the game
        ChatInput = new ChatInputControl(PrefabSet)
        {
            IsHitTestVisible = false
        };
        AddChild(ChatInput);

        ChatInput.FocusChanged += focused =>
        {
            //gate hit-testing on focus: clicks pass through to the world when idle,
            //but drag-select works normally once the box is focused via hotkey.
            ChatInput.IsHitTestVisible = focused;

            if (focused)
                DescriptionLabel?.Text = string.Empty;
        };

        //inventory area
        InventoryBounds = GetRect("InventoryRect");

        //hp/mp orbs
        HpOrb = CreateProgressBar("ORB_HP")!;
        MpOrb = CreateProgressBar("ORB_MP")!;

        //hp/mp numeric text — centered for horizontal orbs, zindex=1 to render above orbs after re-sorts
        HpNumLabel = CreateLabel("NUM_HP", HorizontalAlignment.Center)!;
        HpNumLabel.ZIndex = 1;
        HpNumLabel.ForegroundColor = Color.White;
        HpNumLabel.ShadowStyle = ShadowStyle.BothSides;
        MpNumLabel = CreateLabel("NUM_MP", HorizontalAlignment.Center)!;
        MpNumLabel.ZIndex = 1;
        MpNumLabel.ForegroundColor = Color.White;
        MpNumLabel.ShadowStyle = ShadowStyle.BothSides;

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
            var pingCache = UiRenderer.Instance!;

            for (var i = 0; i < PingFrames.Length; i++)
                PingFrames[i] = pingCache.GetSpfTexture("_nping.spf", i);

            LatencyMonitor.LatencyChanged += OnLatencyChanged;
            OnLatencyChanged();

            PingIcon.Hovered += _ => ShowPingTooltip();
            PingIcon.Unhovered += _ => HideTooltip();
            PingIcon.VisibilityChanged += _ => HideTooltip();
        }

        //effect bar
        EffectBar = new EffectBarControl
        {
            X = 618,
            Y = 2
        };
        AddChild(EffectBar);

        //buttons
        OptionButton = CreateButton("BTN_OPTION");
        BulletinButton = CreateButton("BTN_BULLETIN");
        UsersButton = CreateButton("BTN_USERS");
        ExpandButton = CreateButton("BTN_EXPAND");
        ChangeLayoutButton = CreateButton("BTN_CHANGELAYOUT");

        //btn_changelayout is a stateful indicator — large hud shows the pressed frame as its
        //persistent look. promote it to normal and drop the press-state swap.
        if (ChangeLayoutButton is not null)
        {
            ChangeLayoutButton.NormalTexture = ChangeLayoutButton.PressedTexture;
            ChangeLayoutButton.PressedTexture = null;
        }
        HelpButton = CreateButton("BTN_HELP");
        LegendButton = CreateButton("BTN_LEGEND");
        TownMapButton = CreateButton("BTN_TOWNMAP");
        GroupButton = CreateButton("BTN_GROUP");
        //mail button pulses a yellow outline while HasUnreadMail is set
        MailButton = CreateButton<MailButton>("CMail");
        GroupIndicator = CreateButton("CGroup");

        ScreenshotButton = CreateButton("CShot");
        EmoteButton = CreateButton("BTN_EMOT");
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

        //wire tooltips for the 9 utility buttons on the large hud
        WireTooltip(LegendButton, "Legend");
        WireTooltip(TownMapButton, "Map");
        WireTooltip(GroupButton, "Group");
        WireTooltip(MailButton, "Mail");
        WireTooltip(GroupIndicator, "Group");
        WireTooltip(ScreenshotButton, "Portraits");
        WireTooltip(SettingsButton, "Settings");
        WireTooltip(CharScreenshotButton, "ScreenShot");
        WireTooltip(HelpButton, "Hotkeys");

        for (var i = 0; i < 6; i++)
            InventoryTabButtons[i] = CreateButton($"BTN_INV{i}");

        ExtendedTabFrame = CreateImage("BTN_INV_EXTENDED_FRAME");

        if (ExtendedTabFrame is not null)
        {
            ExtendedTabFrame.X = InventoryBounds.X;
            ExtendedTabFrame.Y = InventoryBounds.Y - ExtendedTabFrame.Height;
            ExtendedTabFrame.ZIndex = -1;
            ExtendedTabFrame.Visible = false;
        }

        //persistent message — floating text, top-right of viewport
        PersistentMessage = new PersistentMessageControl(ViewportBounds);
        AddChild(PersistentMessage);

        //resolve inventory background textures from prefab for tab panels
        var cache = UiRenderer.Instance!;
        var invBgTexture = GetPrefabTexture(cache, "InventoryBackground");
        var invBgExpandedTexture = GetPrefabTexture(cache, "InventoryBackgroundExpanded");
        var livingBgTexture = GetPrefabTexture(cache, "LivingInventoryBackground");
        var skillSpellExpandedTexture = GetPrefabTexture(cache, "SkillSpellBackgroundExpanded");
        var chatExpandedTexture = GetPrefabTexture(cache, "ChatBackgroundExpanded");
        ExpandedChatBounds = GetRect("ChattingRectExpanded");

        //orange bar
        OrangeBar = new OrangeBarControl(PrefabSet);

        //tab panels
        CreateTabPanels(
            invBgTexture,
            invBgExpandedTexture,
            livingBgTexture,
            skillSpellExpandedTexture,
            chatExpandedTexture);

        //orange bar renders above collapsed tab panels (z=0) but below expanded ones (z=10)
        OrangeBar.ZIndex = 1;
        AddChild(OrangeBar);

        //attributes subscription + initial sync if data already exists
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

        UpdateHp((int)attrs.CurrentHp, (int)attrs.MaximumHp);
        UpdateMp((int)attrs.CurrentMp, (int)attrs.MaximumMp);
        SetWeight(attrs.CurrentWeight, attrs.MaxWeight);
        StatsPanel.UpdateAttributes(attrs);
        ExtendedStatsPanel.UpdateAttributes(attrs);

        MailButton?.SetAnimating(attrs.HasUnreadMail);
    }

    #region Tab Panel Management
    public void ShowTab(HudTab tab)
    {
        //collapse the outgoing panel before hiding it (so it returns to normal state)
        if (TabPanels[(int)ActiveTab] is ExpandablePanel { IsExpanded: true } oldExpandable)
        {
            var offset = oldExpandable.ExpandYOffset;
            oldExpandable.SetExpanded(false);
            ShiftCompanionElements(offset);

            ExtendedTabFrame?.Visible = false;
        }

        foreach (var panel in TabPanels)
            panel?.Visible = false;

        if ((ActiveTab == HudTab.Inventory) && (tab != HudTab.Inventory))
            Inventory.ForceHoverExit();

        var oldButtonIndex = GetTabButtonIndex(ActiveTab);

        if ((oldButtonIndex >= 0) && (oldButtonIndex < InventoryTabButtons.Length))
            if (InventoryTabButtons[oldButtonIndex] is { } oldBtn)
                oldBtn.IsSelected = false;

        ActiveTab = tab;

        TabPanels[(int)tab]?.Visible = true;

        //apply global expand state to the newly shown panel
        if (Expanded)
            ApplyExpandToActiveTab();

        var newButtonIndex = GetTabButtonIndex(tab);

        if ((newButtonIndex >= 0) && (newButtonIndex < InventoryTabButtons.Length))
            if (InventoryTabButtons[newButtonIndex] is { } newBtn)
                newBtn.IsSelected = true;
    }

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

    private PanelSlot? TooltipSlot;

    /// <summary>
    ///     Shows the shared tooltip label anchored to the cursor for the given panel slot. Used for skill/spell slot hovers so
    ///     the user can see the full ability name + level details (e.g. "Ioc (Lev:50/100)") without clipping in the
    ///     description area. The tooltip follows the cursor via <see cref="Update"/> until <see cref="HideSlotTooltip"/>.
    /// </summary>
    public void ShowSlotTooltip(PanelSlot slot)
    {
        TooltipSlot = slot;
        RefreshSlotTooltip();
    }

    public void HideSlotTooltip()
    {
        TooltipSlot = null;
        TooltipLabel.Visible = false;
    }

    private void RefreshSlotTooltip()
    {
        if (TooltipSlot is not { SlotName: { Length: > 0 } text })
        {
            TooltipLabel.Visible = false;

            return;
        }

        TooltipLabel.Text = text;
        TooltipLabel.Width = TextRenderer.MeasureWidth(text) + 4;
        TooltipLabel.Height = TextRenderer.CHAR_HEIGHT + 4;

        //anchor bottom-left at cursor, shifted down 3px, clamping right edge to screen
        var cursorX = InputBuffer.MouseX;
        var cursorY = InputBuffer.MouseY;

        var absoluteRight = cursorX + TooltipLabel.Width;

        if (absoluteRight > ChaosGame.VIRTUAL_WIDTH)
            cursorX -= absoluteRight - ChaosGame.VIRTUAL_WIDTH;

        if (cursorX < 0)
            cursorX = 0;

        TooltipLabel.X = cursorX - ScreenX;
        TooltipLabel.Y = cursorY - ScreenY - TooltipLabel.Height + 3;
        TooltipLabel.Visible = true;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (TooltipSlot is not null)
            RefreshSlotTooltip();
    }

    private void CreateTabPanels(
        Texture2D? invBgTexture,
        Texture2D? invBgExpandedTexture,
        Texture2D? livingBgTexture,
        Texture2D? skillSpellExpandedTexture,
        Texture2D? chatExpandedTexture)
    {
        var tabRect = InventoryBounds;

        //large hud: 1 row normal (12 slots), 5 rows expanded (60 slots) for inventory
        Inventory = new InventoryPanel(
            PrefabSet,
            invBgTexture,
            invBgExpandedTexture,
            LARGE_NORMAL_SLOTS);
        RegisterTab(HudTab.Inventory, Inventory, tabRect);

        //large hud: 1 row normal, 3 rows expanded for skills/spells
        SkillBook = new SkillBookPanel(PrefabSet, background: invBgTexture, normalVisibleSlots: LARGE_NORMAL_SLOTS);
        SkillBook.ConfigureExpand(skillSpellExpandedTexture, LARGE_EXPANDED_SKILL_SPELL_SLOTS);

        SkillBookAlt = new SkillBookPanel(
            PrefabSet,
            SkillBookPage.Page2,
            invBgTexture,
            LARGE_NORMAL_SLOTS);
        SkillBookAlt.ConfigureExpand(skillSpellExpandedTexture, LARGE_EXPANDED_SKILL_SPELL_SLOTS);

        RegisterTab(HudTab.Skills, SkillBook, tabRect);
        RegisterTab(HudTab.SkillsAlt, SkillBookAlt, tabRect);

        SpellBook = new SpellBookPanel(PrefabSet, background: invBgTexture, normalVisibleSlots: LARGE_NORMAL_SLOTS);
        SpellBook.ConfigureExpand(skillSpellExpandedTexture, LARGE_EXPANDED_SKILL_SPELL_SLOTS);

        SpellBookAlt = new SpellBookPanel(
            PrefabSet,
            SkillBookPage.Page2,
            invBgTexture,
            LARGE_NORMAL_SLOTS);
        SpellBookAlt.ConfigureExpand(skillSpellExpandedTexture, LARGE_EXPANDED_SKILL_SPELL_SLOTS);

        RegisterTab(HudTab.Spells, SpellBook, tabRect);
        RegisterTab(HudTab.SpellsAlt, SpellBookAlt, tabRect);

        //chat — stores both normal and expanded bounds for expand toggle
        NormalChatBounds = GetRect("ChattingRect");
        ChatDisplay = new ChatPanel(NormalChatBounds, tabRect);
        ChatDisplay.ConfigureExpand(chatExpandedTexture, ExpandedChatBounds, tabRect);
        RegisterTab(HudTab.Chat, ChatDisplay, tabRect);

        //large hud uses compact stats (_nstatur), expanding to full stats (_nstatus)
        var compactStatusPrefabSet = DataContext.UserControls.Get("_nstatur")!;
        var fullStatusPrefabSet = DataContext.UserControls.Get("_nstatus")!;
        StatsPanel = new StatsPanel(compactStatusPrefabSet);
        StatsPanel.ConfigureExpand(fullStatusPrefabSet);
        ExtendedStatsPanel = new ExtendedStatsPanel(compactStatusPrefabSet);
        ExtendedStatsPanel.ConfigureExpand(fullStatusPrefabSet);
        RegisterTab(HudTab.Stats, StatsPanel, tabRect);
        RegisterTab(HudTab.ExtendedStats, ExtendedStatsPanel, tabRect);

        //tools (h tab): 1 row normal (6 cells per half), expanded to 3 rows (18 cells per half)
        //uses the small-hud's _nbk_s LivingInventoryBackground as the expanded variant
        var normalHudPrefabSet = DataContext.UserControls.Get("_nbk_s")!;
        var uiCache = UiRenderer.Instance!;
        Texture2D? toolsExpandedTexture = null;

        if (normalHudPrefabSet.Contains("LivingInventoryBackground") && (normalHudPrefabSet["LivingInventoryBackground"].Images.Count > 0))
            toolsExpandedTexture = uiCache.GetPrefabTexture(normalHudPrefabSet.Name, "LivingInventoryBackground", 0);

        Tools = new ToolsPanel(PrefabSet, livingBgTexture, COMPACT_VISIBLE_SLOTS_PER_HALF);
        Tools.ConfigureExpandPerHalf(toolsExpandedTexture, EXPANDED_VISIBLE_SLOTS_PER_HALF);
        RegisterTab(HudTab.Tools, Tools, tabRect);

        var msgHistoryBounds = GetRect("ChattingRect");
        MessageHistory = new SystemMessagePanel(msgHistoryBounds, tabRect, WorldState.Chat.GetOrangeBarHistory());
        MessageHistory.ConfigureExpand(chatExpandedTexture, ExpandedChatBounds, tabRect);
        RegisterTab(HudTab.MessageHistory, MessageHistory, tabRect);

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

        ShowTab(HudTab.Inventory);
    }

    private void RegisterTab(HudTab tab, UIPanel panel, Rectangle tabRect)
    {
        panel.X = tabRect.X;
        panel.Y = tabRect.Y;

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
            GroupIndicator.NormalTexture = cache.GetSpfTexture("_ni_gr0b.spf");
            GroupIndicator.PressedTexture = cache.GetSpfTexture("_ni_gr0b.spf", 1);
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
        WeightLabel.Text = $"{current}/{max}";
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

    public void SetDescription(string? text)
    {
        if (ChatInput.IsFocused && !string.IsNullOrEmpty(text))
            return;

        DescriptionLabel?.Text = text ?? string.Empty;
    }

    public bool IsOrangeBarDragging => OrangeBar.IsDragging;

    /// <summary>
    ///     Large HUD: expand toggles globally. Affects inventory (1→5 rows), skills/spells/tools (1→3 rows), and chat
    ///     (small→large text area). State persists across tab switches.
    /// </summary>
    public void ToggleExpand()
    {
        Expanded = !Expanded;

        ApplyExpandToActiveTab();
    }

    /// <inheritdoc />
    public bool CollapseExpanded()
    {
        if (!Expanded)
            return false;

        Expanded = false;
        ApplyExpandToActiveTab();

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

    private void ApplyExpandToActiveTab()
    {
        if (TabPanels[(int)ActiveTab] is not ExpandablePanel expandable)
            return;

        var wasExpanded = expandable.IsExpanded;
        expandable.SetExpanded(Expanded);

        if (expandable.IsExpanded != wasExpanded)
        {
            ShiftCompanionElements(expandable.IsExpanded ? -expandable.ExpandYOffset : expandable.ExpandYOffset);

            ExtendedTabFrame?.Visible = expandable.IsExpanded;
        }
    }

    private void ShiftCompanionElements(int yShift)
    {
        foreach (var btn in InventoryTabButtons)
            btn?.Y += yShift;

        ExtendedTabFrame?.Y += yShift;

        ChatInput.Y += yShift;
    }

    public void ShowPersistentMessage(string text) => PersistentMessage.SetMessage(text);
    #endregion
}