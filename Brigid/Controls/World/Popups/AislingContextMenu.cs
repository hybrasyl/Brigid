#region
using Brigid.Controls.Generic;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     The menu that a Ctrl+click on another aisling opens (BRIG-125): the aisling's name as the title, then info, group
///     request and whisper. A <see cref="PopupMenu" />, so it takes the theme's <c>frame.popup</c> part.
/// </summary>
public sealed class AislingContextMenu : PopupMenu
{
    //info, group request and whisper
    private const int ENTRY_COUNT = 3;

    public AislingContextMenu()
        : base(ENTRY_COUNT) => Name = "aislingmenu";

    /// <summary>Opens the menu for the aisling <paramref name="name" /> at (<paramref name="screenX" />, <paramref name="screenY" />).</summary>
    public void Show(
        int screenX,
        int screenY,
        string name,
        Action onInfo,
        Action onGroupRequest,
        Action onWhisper)
        => Show(screenX, screenY, name, [("info", onInfo), ("group request", onGroupRequest), ("whisper", onWhisper)]);
}
