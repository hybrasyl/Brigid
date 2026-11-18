#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Hud.Panel;
using Brigid.Controls.World.ViewPort;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     The new HUD (BRIG-127): a top bar, the game world and a bottom area, built on the themed primitives with no dat
///     art. While it is built, Shift+/ selects it (D42), and the cards BRIG-145 to BRIG-150 fill the places that it keeps
///     for their parts. The positions are in <see cref="MainHudLayout" />.
/// </summary>
/// <remarks>
///     The legacy members of <see cref="IWorldHud" /> (the inventory, the ability panels, the chat display, the message
///     history, the stats and the effect bar) are the classic HUD's own panels, which stay hidden with it (D41). So the
///     code that reaches <c>WorldHud.Inventory</c> and the others keeps working on this HUD, nothing of them shows, and
///     nothing is wired twice. Each card replaces its member, and BRIG-156 removes the rest. The chat input is this HUD's
///     own, in its chat box (BRIG-145). The legacy buttons are absent.
/// </remarks>
public sealed class MainHudControl : UIPanel, IWorldHud
{
    private readonly PlayerAttributes Attributes;
    private readonly IWorldHud Classic;
    private readonly PersistentMessageControl PersistentMessage;
    private readonly HudTopBar TopBar;
    private readonly HudTooltip Tooltip;

    /// <summary>
    ///     The layer of the HUD among the children of the world root: under the popups, also the pause menu and the Aisling
    ///     list (-2), which open in the world area, where the panels of this HUD are. A popup floats above the panels.
    /// </summary>
    public const int LAYER = -3;

    /// <param name="classic">The classic HUD, whose panels this HUD returns for the legacy members.</param>
    /// <param name="clock">The source of the local time of the clock; null for the system clock.</param>
    /// <param name="attributes">The player's attributes; null for <see cref="WorldState.Attributes" />.</param>
    /// <param name="chat">The chat log of the chat box; null for <see cref="WorldState.Chat" />.</param>
    public MainHudControl(IWorldHud classic, TimeProvider? clock = null, PlayerAttributes? attributes = null, Chat? chat = null)
    {
        Classic = classic;
        Attributes = attributes ?? WorldState.Attributes;
        Name = "main_hud";
        Width = ChaosGame.VIRTUAL_WIDTH;
        Height = ChaosGame.VIRTUAL_HEIGHT;
        UseTheme = true;

        //the world area passes the mouse to the world; each bar takes it
        IsPassThrough = true;

        Tooltip = new HudTooltip();
        TopBar = new HudTopBar(clock ?? TimeProvider.System, Tooltip);
        TopBar.ServerClicked += () => ServerBoxClicked?.Invoke();
        TopBar.MailClicked += () => MailClicked?.Invoke();
        AddChild(TopBar);
        AddChild(new HudBarPanel(ThemeRoles.HudBottomBar, MainHudLayout.BottomArea) { Name = "bottom_bar" });
        AddChild(new HudBarPanel(ThemeRoles.HudSideEdge, MainHudLayout.LeftEdge) { Name = "left_edge" });
        AddChild(new HudBarPanel(ThemeRoles.HudSideEdge, MainHudLayout.RightEdge) { Name = "right_edge" });
        AddChild(new DividerLine(MainHudLayout.Divider) { Name = "divider" });

        ChatBox = new ChatBoxControl(chat ?? WorldState.Chat);
        AddChild(ChatBox);

        //the places of the parts that later cards add (BRIG-162, BRIG-147, BRIG-146)
        AddChild(Place("toast", MainHudLayout.Toast));
        AddChild(Place("effect_bar", MainHudLayout.EffectBar));
        AddChild(Place("hotbar", MainHudLayout.Hotbar));

        //the meters (BRIG-148)
        HealthBar = new HudMeter("health_bar", MainHudLayout.HealthBar, ThemeRoles.HudHealthTrack, ThemeRoles.HudHealthFill, DialogPalette.HealthFill)
        {
            Label = "HP"
        };
        ManaBar = new HudMeter("mana_bar", MainHudLayout.ManaBar, ThemeRoles.HudManaTrack, ThemeRoles.HudManaFill, DialogPalette.ManaFill)
        {
            Label = "MP"
        };
        ExperienceBar = new HudMeter(
            "experience_bar",
            MainHudLayout.ExperienceBar,
            ThemeRoles.HudExperienceTrack,
            ThemeRoles.HudExperienceFill,
            DialogPalette.ExperienceFill)
        {
            Segments = EXPERIENCE_SEGMENTS,
            IsHitTestVisible = true
        };
        ExperienceBar.Entered += meter =>
        {
            if (Attributes.HasData)
                Tooltip.ShowAbove(meter, ExperienceLines);
        };
        ExperienceBar.Left += Tooltip.Hide;
        AddChild(HealthBar);
        AddChild(ManaBar);
        AddChild(ExperienceBar);
        Plate = new HudPlayerPlate();
        Plate.Clicked += () => PlateClicked?.Invoke();
        AddChild(Plate);
        Windows = new HudWindowHost(Tooltip);
        AddChild(Windows.Row);

        PersistentMessage = new PersistentMessageControl(ViewportBounds) { Name = "persistent_message" };
        AddChild(PersistentMessage);
        foreach (var panel in Windows.Panels)
            AddChild(panel);

        AddChild(Tooltip);

        //a hidden HUD shows no tooltip, also when it shows again with the mouse still on the box
        VisibilityChanged += _ => Tooltip.Hide();

        //the attributes can arrive before the world screen makes its HUDs
        Attributes.Changed += OnAttributesChanged;
        OnAttributesChanged();
    }

    /// <summary>The game time that a click on the clock shows, or null while no source gives one (D39).</summary>
    public Func<string?> GameTimeSource
    {
        get => TopBar.GameTimeSource;
        set => TopBar.GameTimeSource = value;
    }

    /// <summary>A click on the server box of the top bar: the connection details.</summary>
    public event Action? ServerBoxClicked;

    /// <summary>A click on the mail box of the top bar: the mail.</summary>
    public event Action? MailClicked;

    /// <summary>A click on the player plate: the status book, until the Details window (D34).</summary>
    public event Action? PlateClicked;

    /// <summary>The player plate of the bottom area (BRIG-148).</summary>
    internal HudPlayerPlate Plate { get; }

    /// <summary>The class name of the player, from the self profile, on the player plate.</summary>
    public void SetPlayerClass(string className) => Plate.SetClass(className);

    /// <summary>Shows the head of the aisling of <paramref name="appearance" /> on the player plate.</summary>
    public void SetPlayerAppearance(AislingRenderer renderer, in AislingAppearance appearance)
        => Plate.SetIcon(renderer.RenderHead(in appearance, MainHudLayout.PlateIcon.Width));

    /// <summary>Shows no head on the player plate, for a form that is not an aisling.</summary>
    public void ClearPlayerAppearance() => Plate.SetIcon(null);

    /// <summary>The windows of the HUD: its button row, its panels and the windows that the world screen binds.</summary>
    internal HudWindowHost Windows { get; }

    /// <summary>The chat box over the bottom left of the world (BRIG-145).</summary>
    public ChatBoxControl ChatBox { get; }

    /// <summary>The health bar of the bottom area (BRIG-148).</summary>
    internal HudMeter HealthBar { get; }

    /// <summary>The mana bar of the bottom area (BRIG-148).</summary>
    internal HudMeter ManaBar { get; }

    /// <summary>The experience bar at the bottom of the screen (BRIG-148, D27).</summary>
    internal HudMeter ExperienceBar { get; }

    //D27: 10 or 20 segments; the mockup has 20
    private const int EXPERIENCE_SEGMENTS = 20;

    /// <summary>
    ///     True when the server converts the experience past the level cap into Crystallized Reflections (Hybrasyl), so the
    ///     experience bar at the cap shows the experience toward the next one (<see cref="HudExperience" />).
    /// </summary>
    public bool ConvertsExperiencePastCap
    {
        get;
        set
        {
            field = value;
            OnAttributesChanged();
        }
    }


    /// <summary>Closes the open panels. Returns true when a panel was open, so that Escape closes the panels first.</summary>
    public bool ClosePanels() => Windows.CloseAll();

    /// <summary>
    ///     True when an open panel or the chat box takes the mouse at the screen point, so that a click or a drop there does
    ///     not reach the world.
    /// </summary>
    public bool IsOverPanel(int screenX, int screenY)
        => Windows.IsOverPanel(screenX, screenY) || ChatBox.TakesMouseAt(screenX, screenY);

    public override void Update(GameTime gameTime)
    {
        Windows.RefreshSelection();

        base.Update(gameTime);
    }

    public override void Dispose()
    {
        Attributes.Changed -= OnAttributesChanged;

        base.Dispose();
    }

    //weight, gold, mail, health and mana come with the attributes, as on the classic HUD
    private void OnAttributesChanged()
    {
        if (Attributes.Current is { } attrs)
            ShowAttributes(attrs);
    }

    /// <summary>
    ///     Shows the weight, the gold and the mail state of <paramref name="attrs" /> on the top bar, and its health and mana
    ///     in their meters.
    /// </summary>
    internal void ShowAttributes(PlayerAttributes attrs)
    {
        TopBar.SetWeight(attrs.CurrentWeight, attrs.MaxWeight);
        TopBar.SetGold(attrs.Gold);
        TopBar.SetMail(attrs.HasMail, attrs.HasParcel);
        ShowMeter(HealthBar, attrs.CurrentHp, attrs.MaxHp);
        ShowMeter(ManaBar, attrs.CurrentMp, attrs.MaxMp);
        Plate.SetLevel(attrs.Level);

        var experience = HudExperience.Fill(attrs, ConvertsExperiencePastCap);
        ExperienceBar.SetFill(experience.Fraction);
        ExperienceBar.FillRole = experience.UsesCapFill ? ThemeRoles.HudExperienceCapFill : ThemeRoles.HudExperienceFill;
        ExperienceBar.FlatFill = experience.UsesCapFill ? DialogPalette.CapFill : DialogPalette.ExperienceFill;
    }

    internal IReadOnlyList<TooltipLine> ExperienceLines()
    {
        var experience = HudExperience.Describe(Attributes, ConvertsExperiencePastCap);

        return [TooltipLine.Themed(experience.Title, ThemeRoles.TextTitle), ..experience.Lines.Select(line => TooltipLine.Themed(line, ThemeRoles.Text))];
    }

    private static void ShowMeter(HudMeter meter, uint current, uint max)
    {
        meter.SetFill(current, max);
        meter.Value = HudFormat.Meter(current, max);
    }

    public Rectangle ViewportBounds => MainHudLayout.World;

    public string PlayerName { get; private set; } = string.Empty;

    //an empty place that the debug overlay names; it does not take the mouse, so the world under it still does
    private static UIPanel Place(string name, Rectangle bounds)
    {
        var place = new UIPanel
        {
            Name = name,
            IsHitTestVisible = false
        };

        place.SetBounds(bounds);

        return place;
    }

    #region The shell
    public ChatInputControl ChatInput => ChatBox.Input;

    public void SetPlayerName(string name)
    {
        PlayerName = name;
        Plate.SetName(name);
    }

    public void ShowPersistentMessage(string text) => PersistentMessage.SetMessage(text);

    public void SetCoords(int x, int y) => TopBar.SetCoords(x, y);

    public void SetZoneName(string zone) => TopBar.SetZoneName(zone);

    public void SetServerName(string name) => TopBar.SetServerName(name);

    public void SetTransportSecure(bool secure) => TopBar.SetTransportSecure(secure);

    public void SetWeight(int current, int max) => TopBar.SetWeight(current, max);

    //the group button reads the group open state through its binding at each update (D18)
    public void SetGroupOpen(bool groupOpen) { }

    //the effect bar (BRIG-147)
    public void SetEffect(byte icon, EffectColor color) { }

    //this HUD has no description line; the slot names that the classic HUD shows there go with the hidden panels
    public void SetDescription(string? text) { }

    //the server box raises ServerBoxClicked; it is not a prefab button
    public UIButton? ServerBoxButton => null;
    #endregion

    #region The classic HUD's panels (D41)
    public HudTab ActiveTab => Classic.ActiveTab;
    public ChatPanel ChatDisplay => Classic.ChatDisplay;
    public SystemMessagePanel MessageHistory => Classic.MessageHistory;
    public ExtendedStatsPanel ExtendedStatsPanel => Classic.ExtendedStatsPanel;
    public InventoryPanel Inventory => Classic.Inventory;
    public Rectangle InventoryBounds => Classic.InventoryBounds;
    public bool IsOrangeBarDragging => Classic.IsOrangeBarDragging;
    public SkillBookPanel SkillBook => Classic.SkillBook;
    public SkillBookPanel SkillBookAlt => Classic.SkillBookAlt;
    public SpellBookPanel SpellBook => Classic.SpellBook;
    public SpellBookPanel SpellBookAlt => Classic.SpellBookAlt;
    public StatsPanel StatsPanel => Classic.StatsPanel;
    public ToolsPanel Tools => Classic.Tools;

    public void ShowTab(HudTab tab) => Classic.ShowTab(tab);

    //F hides and shows the chat box, and Shift+F shows its System tab (BRIG-145); the other tab keys act on the hidden
    //classic panels until their cards
    public void HandleTabActivation(HudTab tab, bool shift)
    {
        if (tab == HudTab.Chat)
        {
            if (shift)
                ChatBox.ShowSystem();
            else
                ChatBox.SetCollapsed(!ChatBox.IsCollapsed);

            return;
        }

        Classic.HandleTabActivation(tab, shift);
    }

    public void ToggleExpand() => Classic.ToggleExpand();

    public bool CollapseExpanded() => Classic.CollapseExpanded();

    //the classic HUD raises this to its own subscribers, also from the HandleTabActivation that this HUD forwards to it, so
    //the self profile opens through the classic HUD's wiring; this HUD has nothing of its own to raise
    public event ClickedHandler? InventoryReactivated
    {
        add { }
        remove { }
    }
    #endregion

    #region The legacy buttons, absent on this HUD
    public UIButton? BulletinButton => null;
    public UIButton? ChangeLayoutButton => null;
    public UIButton? CharScreenshotButton => null;
    public UIButton? EmoteButton => null;
    public StatusIcon? EmoteStatusIcon => null;
    public UIButton? ExpandButton => null;
    public UIButton? GroupButton => null;
    public UIButton? GroupIndicator => null;
    public UIButton? HelpButton => null;
    public UIButton?[] InventoryTabButtons => [];
    public UIButton? LegendButton => null;
    public MailButton? MailButton => null;
    public UIButton? OptionButton => null;
    public UIButton? ScreenshotButton => null;
    public UIButton? SettingsButton => null;
    public UIButton? TownMapButton => null;
    public UIButton? UsersButton => null;
    #endregion

    //the line between the hotbar row and the button row, in the theme's divider colour
    private sealed class DividerLine : UIPanel
    {
        public DividerLine(Rectangle bounds)
        {
            SetBounds(bounds);
            BackgroundColor = DialogPalette.Divider;
            IsHitTestVisible = false;
        }

        protected internal override Color? DrawnBackgroundColor => ThemeColor(ThemeRoles.Divider);
    }
}
