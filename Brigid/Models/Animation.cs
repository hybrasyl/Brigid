#region
using Brigid.Data.Definitions;
#endregion

namespace Brigid.Models;

/// <summary>
///     Tracks a single in-flight spell/effect animation. Can be attached to an entity or positioned at a tile.
/// </summary>
public sealed class Animation
{
    public EffectBlendMode BlendMode { get; init; }
    public int CurrentFrame { get; set; }
    public int EffectId { get; init; }
    public float ElapsedMs { get; set; }
    public int FrameCount { get; init; }
    public float FrameIntervalMs { get; init; }
    public uint? TargetEntityId { get; init; }
    public int? TileX { get; init; }
    public int? TileY { get; init; }
    public bool IsComplete => CurrentFrame >= FrameCount;
}