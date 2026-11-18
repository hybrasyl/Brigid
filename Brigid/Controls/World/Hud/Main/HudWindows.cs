#region
using Brigid.Systems.Keybinds;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     A window of the new HUD and its button (BRIG-127 D3, D29). A window with a <see cref="Panel" /> size opens as a
///     panel of the HUD; the others run the action that the world screen binds to them.
/// </summary>
/// <param name="Id">The name of the button and the end of its icon role.</param>
/// <param name="Name">The name that the tooltip shows.</param>
/// <param name="Key">The keybind of the window, which the tooltip shows as it resolves now.</param>
/// <param name="FixedKey">A key that is not a keybind (F12 for the screenshot), or null.</param>
/// <param name="Panel">The panel size of a window of the HUD, or null for a window that the world screen binds.</param>
internal sealed record HudWindow(string Id, string Name, CommandId? Key = null, string? FixedKey = null, HudPanelSize? Panel = null)
{
    public string IconRole => $"icon.window.{Id}";
}

/// <summary>
///     The one list of the window buttons, from the left (D29). A button is added or removed here, and the row lays itself
///     out again. A window that a later card makes is an empty panel until that card is done (D38, D40).
/// </summary>
internal static class HudWindows
{
    public static readonly HudWindow Abilities = new("abilities", "Abilities", Panel: HudPanelSize.Compact);
    public static readonly HudWindow Inventory = new("inventory", "Inventory", Panel: HudPanelSize.Compact);
    public static readonly HudWindow Equipment = new("equipment", "Equipment", Panel: HudPanelSize.Compact);
    public static readonly HudWindow Mastery = new("mastery", "Mastery", Panel: HudPanelSize.Large);
    public static readonly HudWindow Quests = new("quests", "Quests", Panel: HudPanelSize.Compact);
    public static readonly HudWindow Menu = new("menu", "Menu", CommandId.World_TogglePauseMenu);
    public static readonly HudWindow Mail = new("mail", "Mail", CommandId.World_ToggleBoard);
    public static readonly HudWindow Community = new("community", "Community", CommandId.World_ToggleWorldList);
    public static readonly HudWindow Map = new("map", "Map", CommandId.World_TownMap);
    public static readonly HudWindow Group = new("group", "Group", CommandId.World_GroupPanel);
    public static readonly HudWindow Screenshot = new("screenshot", "Screenshot", FixedKey: "F12");

    /// <summary>The row, from the left.</summary>
    public static readonly HudWindow[] All = [Abilities, Inventory, Equipment, Mastery, Quests, Menu, Mail, Community, Map, Group, Screenshot];
}
