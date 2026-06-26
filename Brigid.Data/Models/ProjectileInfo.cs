namespace Brigid.Data.Models;

public sealed class ProjectileInfo
{
    public required int Id { get; init; }
    public required int Type { get; init; }
    public required int FramesPerDirection { get; init; }
    public required int Step { get; init; }
    public required int StepDelay { get; init; }
    public int? ArcRatioV { get; init; }
    public int? ArcRatioH { get; init; }
}