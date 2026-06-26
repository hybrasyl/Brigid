#region
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Models;

/// <summary>
///     Per-frame, render-oriented state computed once by <see cref="Brigid.Screens.WorldScreen" />.Update and consumed
///     by Draw and overlay systems. This centralizes transient derived state (hover, sort order, tile under cursor) so
///     rendering never recomputes it mid-frame.
/// </summary>
public sealed class DrawState
{
    public IReadOnlyList<WorldEntity> SortedEntities { get; internal set; } = [];
    public uint? HoveredEntityId { get; internal set; }
    public uint? HoveredGroupBoxId { get; internal set; }
    public Point? HoveredTile { get; internal set; }
    public bool ShowTintHighlight { get; internal set; }
    public bool UseDragCursor { get; internal set; }

    internal void Reset()
    {
        SortedEntities = [];
        HoveredEntityId = null;
        HoveredGroupBoxId = null;
        HoveredTile = null;
        ShowTintHighlight = false;
        UseDragCursor = false;
    }
}