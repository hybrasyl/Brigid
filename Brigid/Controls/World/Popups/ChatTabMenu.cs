#region
using Brigid.Controls.Generic;
using Brigid.ViewModel;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>The menu of a custom chat tab of the new HUD (BRIG-145): the tab's name, then Edit and Remove.</summary>
public sealed class ChatTabMenu : PopupMenu
{
    //edit and remove
    private const int ENTRY_COUNT = 2;

    public ChatTabMenu()
        : base(ENTRY_COUNT) => Name = "chat_tab_menu";

    /// <summary>Opens the menu for <paramref name="tab" /> at (<paramref name="screenX" />, <paramref name="screenY" />).</summary>
    public void Show(int screenX, int screenY, ChatTab tab, Action onEdit, Action onRemove)
        => Show(screenX, screenY, tab.Name, [("Edit", onEdit), ("Remove", onRemove)]);
}
