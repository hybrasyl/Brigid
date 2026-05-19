#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.World.Hud.Panel;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Controls.World.Hud;

/// <summary>
///     Common interface for both small and large HUD layouts. Allows WorldScreen to reference either layout
///     interchangeably. Extract a shared base class once both implementations are stable.
/// </summary>
public interface IWorldHud
{
    HudTab ActiveTab { get; }

    //buttons — nullable since some may not exist in a given prefab
    UIButton? BulletinButton { get; }
    UIButton? ChangeLayoutButton { get; }
    UIButton? CharScreenshotButton { get; }
    ChatPanel ChatDisplay { get; }
    SystemMessagePanel MessageHistory { get; }
    ChatInputControl ChatInput { get; }
    EffectBarControl EffectBar { get; }
    UIButton? EmoteButton { get; }
    UIButton? ExpandButton { get; }
    ExtendedStatsPanel ExtendedStatsPanel { get; }
    UIButton? GroupButton { get; }
    UIButton? GroupIndicator { get; }
    UIButton? HelpButton { get; }
    InventoryPanel Inventory { get; }
    Rectangle InventoryBounds { get; }
    UIButton?[] InventoryTabButtons { get; }
    bool IsOrangeBarDragging { get; }
    UIButton? LegendButton { get; }
    MailButton? MailButton { get; }
    string PlayerName { get; }
    UIButton? ScreenshotButton { get; }
    UIButton? SettingsButton { get; }
    SkillBookPanel SkillBook { get; }
    SkillBookPanel SkillBookAlt { get; }
    SpellBookPanel SpellBook { get; }
    SpellBookPanel SpellBookAlt { get; }
    StatsPanel StatsPanel { get; }
    ToolsPanel Tools { get; }
    UIButton? TownMapButton { get; }
    UIButton? UsersButton { get; }
    Rectangle ViewportBounds { get; }
    void SetCoords(int x, int y);
    void SetDescription(string? text);

    void SetGroupOpen(bool groupOpen);
    void SetPlayerName(string name);
    void SetServerName(string name);
    void SetWeight(int current, int max);
    void SetZoneName(string zone);
    void ShowPersistentMessage(string text);
    void ShowTab(HudTab tab);
    void ToggleExpand();

    /// <summary>
    ///     Collapses any panel currently in expanded mode back to its normal size. Returns <c>true</c> if anything was
    ///     collapsed, <c>false</c> if nothing was expanded.
    /// </summary>
    bool CollapseExpanded();

    /// <summary>
    ///     Shared activation logic for HUD tab hotkeys (A/S/D/F/G/H) and the matching tab buttons. Mirrors the keyboard
    ///     handler exactly: applies shift modifiers (alt panels, expand inventory, message history, extended stats) and
    ///     the click-while-active behavior gated on <c>UseShiftKeyForAltPanels</c>.
    /// </summary>
    void HandleTabActivation(HudTab tab, bool shift);

    /// <summary>
    ///     Fired when the inventory tab button is clicked while already on the inventory tab.
    /// </summary>
    event ClickedHandler? InventoryReactivated;
}