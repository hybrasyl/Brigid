#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Hud.Panel;
using Brigid.Controls.World.ViewPort;
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
///     The legacy members of <see cref="IWorldHud" /> (the inventory, the ability panels, the chat, the stats and the
///     effect bar) are the classic HUD's own panels, which stay hidden with it (D41). So the code that reaches
///     <c>WorldHud.Inventory</c> and the others keeps working on this HUD, nothing of them shows, and nothing is wired
///     twice. Each card replaces its member, and BRIG-156 removes the rest. The legacy buttons are absent.
/// </remarks>
public sealed class MainHudControl : UIPanel, IWorldHud
{
    private readonly IWorldHud Classic;
    private readonly PersistentMessageControl PersistentMessage;

    public MainHudControl(IWorldHud classic)
    {
        Classic = classic;
        Name = "main_hud";
        Width = ChaosGame.VIRTUAL_WIDTH;
        Height = ChaosGame.VIRTUAL_HEIGHT;
        UseTheme = true;

        //the world area passes the mouse to the world; each bar takes it
        IsPassThrough = true;

        AddChild(new HudBarPanel(ThemeRoles.HudTopBar, MainHudLayout.TopBar) { Name = "top_bar" });
        AddChild(new HudBarPanel(ThemeRoles.HudBottomBar, MainHudLayout.BottomArea) { Name = "bottom_bar" });
        AddChild(new HudBarPanel(ThemeRoles.HudSideEdge, MainHudLayout.LeftEdge) { Name = "left_edge" });
        AddChild(new HudBarPanel(ThemeRoles.HudSideEdge, MainHudLayout.RightEdge) { Name = "right_edge" });
        AddChild(new DividerLine(MainHudLayout.Divider) { Name = "divider" });

        //the places of the parts that later cards add
        AddChild(Place("chat_box", MainHudLayout.ChatBox));
        AddChild(Place("toast", MainHudLayout.Toast));
        AddChild(Place("effect_bar", MainHudLayout.EffectBar));
        AddChild(Place("health_bar", MainHudLayout.HealthBar));
        AddChild(Place("mana_bar", MainHudLayout.ManaBar));
        AddChild(Place("hotbar", MainHudLayout.Hotbar));
        AddChild(Place("player_plate", MainHudLayout.PlayerPlate));
        AddChild(Place("window_buttons", MainHudLayout.WindowButtons));
        AddChild(Place("experience_bar", MainHudLayout.ExperienceBar));

        PersistentMessage = new PersistentMessageControl(ViewportBounds) { Name = "persistent_message" };
        AddChild(PersistentMessage);
    }

    public Rectangle ViewportBounds => MainHudLayout.World;

    public string PlayerName { get; private set; } = string.Empty;

    //an empty place that the debug overlay names; it does not take the mouse, so the world under the chat box still does
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
    public void SetPlayerName(string name) => PlayerName = name;

    public void ShowPersistentMessage(string text) => PersistentMessage.SetMessage(text);

    //the top bar (phase 2)
    public void SetCoords(int x, int y) { }

    public void SetZoneName(string zone) { }

    public void SetServerName(string name) { }

    public void SetTransportSecure(bool secure) { }

    public void SetWeight(int current, int max) { }

    //the group button (phase 3)
    public void SetGroupOpen(bool groupOpen) { }

    //the effect bar (BRIG-147)
    public void SetEffect(byte icon, EffectColor color) { }

    //this HUD has no description line; the slot names that the classic HUD shows there go with the hidden panels
    public void SetDescription(string? text) { }

    public UIButton? ServerBoxButton => null;
    #endregion

    #region The classic HUD's panels (D41)
    public HudTab ActiveTab => Classic.ActiveTab;
    public ChatPanel ChatDisplay => Classic.ChatDisplay;
    public SystemMessagePanel MessageHistory => Classic.MessageHistory;
    public ChatInputControl ChatInput => Classic.ChatInput;
    public EffectBarControl EffectBar => Classic.EffectBar;
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

    public void HandleTabActivation(HudTab tab, bool shift) => Classic.HandleTabActivation(tab, shift);

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
