namespace Brigid.Data.Models;

/// <summary>
///     Ground tile attribute for a background tile ID. Encodes color tint, walk-blocking, water adjacency, and foreground
///     height override flags.
/// </summary>
public sealed class GroundAttribute
{
    public byte A { get; set; }
    public byte B { get; set; }

    public byte G { get; set; }

    /// <summary>
    ///     True when the tile is adjacent to water edges.
    /// </summary>
    public bool IsAdjacentWater { get; set; }

    /// <summary>
    ///     True when the tile blocks movement (e.g. deep water).
    /// </summary>
    public bool IsWalkBlocking { get; set; }

    /// <summary>
    ///     Foreground height override value. Non-zero values cause objects on this tile to appear partially submerged.
    /// </summary>
    public int PaintHeight { get; set; }

    /// <summary>
    ///     Red channel (0-255) of the color overlay tint applied to this tile.
    /// </summary>
    public byte R { get; set; }

    /// <summary>
    ///     True when the tile has a visible color tint overlay.
    /// </summary>
    public bool HasColorTint => (A > 0) && (PaintHeight > 0);

    /// <summary>
    ///     True when the tile is a water tile (either deep/walk-blocking or painted water).
    /// </summary>
    public bool IsWater => IsWalkBlocking || (PaintHeight > 0);
}