#region
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>The two sizes of a window of the new HUD (BRIG-127 D30).</summary>
internal enum HudPanelSize
{
    /// <summary>Half of the world area: <see cref="MainHudLayout.CompactLeft" /> or <see cref="MainHudLayout.CompactRight" />.</summary>
    Compact,

    /// <summary>The world area: <see cref="MainHudLayout.LargePanel" />.</summary>
    Large
}

/// <summary>
///     Which window panels of the new HUD are open, and where (BRIG-127 D30). One open compact panel is on the right; a
///     second opens on the left; a third takes the place of the left one. A large panel closes the compact panels, and a
///     compact panel closes the large panel. When the right panel closes, the left panel moves to the right.
/// </summary>
internal sealed class HudPanelHost
{
    public string? Left { get; private set; }
    public string? Right { get; private set; }
    public string? Large { get; private set; }

    public bool IsOpen(string id) => (Left == id) || (Right == id) || (Large == id);

    /// <summary>Opens the panel of <paramref name="id" />, or closes it when it is open.</summary>
    public void Toggle(string id, HudPanelSize size)
    {
        if (IsOpen(id))
            Close(id);
        else
            Open(id, size);
    }

    /// <summary>Opens the panel of <paramref name="id" />; an open panel stays where it is.</summary>
    public void Open(string id, HudPanelSize size)
    {
        if (IsOpen(id))
            return;

        if (size == HudPanelSize.Large)
        {
            (Left, Right, Large) = (null, null, id);

            return;
        }

        Large = null;

        if (Right is null)
            Right = id;
        else
            Left = id;
    }

    public void Close(string id)
    {
        if (Large == id)
            Large = null;
        else if (Left == id)
            Left = null;
        else if (Right == id)
            (Right, Left) = (Left, null);
    }

    /// <summary>Closes every panel. Returns true when a panel was open (Escape closes the panels first).</summary>
    public bool CloseAll()
    {
        var any = (Left ?? Right ?? Large) is not null;
        (Left, Right, Large) = (null, null, null);

        return any;
    }

    /// <summary>The place of the panel of <paramref name="id" />, or null when it is not open.</summary>
    public Rectangle? PlaceOf(string id)
        => Large == id ? MainHudLayout.LargePanel
            : Right == id ? MainHudLayout.CompactRight
            : Left == id ? MainHudLayout.CompactLeft : null;
}
