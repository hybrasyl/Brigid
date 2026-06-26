namespace Brigid.Models;

/// <summary>
///     Tracks whether consecutive clicks target the same tile. Call <see cref="Click" /> when a click occurs; returns true
///     if the same tile was clicked previously. Double-click timing is handled by the InputDispatcher.
/// </summary>
public struct TileClickTracker
{
    private int LastTileX;
    private int LastTileY;

    public bool Click(int tileX, int tileY)
    {
        var sameTile = (tileX == LastTileX) && (tileY == LastTileY);
        LastTileX = tileX;
        LastTileY = tileY;

        return sameTile;
    }
}