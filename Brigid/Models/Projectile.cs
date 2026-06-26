namespace Brigid.Models;

public sealed class Projectile
{
    public uint TargetEntityId { get; init; }
    public int MeffectId { get; init; }

    public float CurrentX { get; set; }
    public float CurrentY { get; set; }

    public float LastKnownTargetX { get; set; }
    public float LastKnownTargetY { get; set; }

    public int Step { get; init; }
    public float StepDelayMs { get; init; }
    public float InitialDistance { get; init; }
    public int? ArcRatioV { get; init; }
    public int? ArcRatioH { get; init; }

    public float ArcOffsetX { get; set; }
    public float ArcOffsetY { get; set; }

    public float ElapsedMs { get; set; }
    public float DistanceTraveled { get; set; }

    public int Direction { get; set; }
    public int FramesPerDirection { get; init; }
    public int CurrentFrameCycle { get; set; }

    public bool IsComplete { get; set; }
}
