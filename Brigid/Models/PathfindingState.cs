#region
using Chaos.Geometry.Abstractions;
#endregion

namespace Brigid.Models;

/// <summary>
///     Manages A* pathfinding state: the current path, optional entity target, and walk queue.
/// </summary>
public sealed class PathfindingState
{
    public Stack<IPoint>? Path;
    public float RetargetTimer;
    public uint? TargetEntityId;

    public bool HasPath => Path is { Count: > 0 };
    public bool HasTarget => TargetEntityId.HasValue;

    public void Clear()
    {
        Path = null;
        TargetEntityId = null;
        RetargetTimer = 0;
    }

    public void SetEntityTarget(uint entityId)
    {
        TargetEntityId = entityId;
        RetargetTimer = 0;
    }

    public void SetPath(Stack<IPoint> path) => Path = path.Count > 0 ? path : null;
}