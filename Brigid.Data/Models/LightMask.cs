namespace Brigid.Data.Models;

public sealed class LightMask
{
    public required int Height { get; init; }
    public required byte[] Pixels { get; init; }
    public required int Width { get; init; }
}