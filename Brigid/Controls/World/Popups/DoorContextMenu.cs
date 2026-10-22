#region
using Brigid.Controls.Generic;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     The menu that an Alt+right-click near doors opens (BRIG-125): "Doors" as the title, then up to
///     <see cref="MAX_ENTRIES" /> doors. The caller scans for the doors, sorts them by distance, and gives each entry its
///     text ("Open Door" or "Close Door") and its action (a ClickTile on the door). A <see cref="PopupMenu" />, so it takes
///     the theme's <c>frame.popup</c> part.
/// </summary>
public sealed class DoorContextMenu : PopupMenu
{
    public const int MAX_ENTRIES = 3;

    private const string TITLE_TEXT = "Doors";

    public DoorContextMenu()
        : base(MAX_ENTRIES) => Name = "doormenu";

    /// <summary>
    ///     Opens the menu at (<paramref name="screenX" />, <paramref name="screenY" />) with up to <see cref="MAX_ENTRIES" />
    ///     entries; the menu drops the others. With no entry, the menu does not open.
    /// </summary>
    public void Show(int screenX, int screenY, IReadOnlyList<(string Label, Action Callback)> entries)
        => Show(screenX, screenY, TITLE_TEXT, entries);
}
