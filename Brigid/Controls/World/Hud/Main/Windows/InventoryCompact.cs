namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     The compact of the Inventory window (BRIG-150, BRIG-127 D32): the moves that put the items in the first slots of the
///     inventory, or in the last slots, in their order. Each move goes into an empty slot, so the server can do the moves
///     one after the other. It does not merge stacks (Sabrael, 2026-11-18).
/// </summary>
internal static class InventoryCompact
{
    /// <summary>
    ///     The moves for the inventory <paramref name="occupied" /> (index 0 is slot 1), as (from, to) slot pairs from 1.
    /// </summary>
    /// <param name="toEnd">False for the first slots (a left click on C), true for the last slots (a right click).</param>
    public static IReadOnlyList<(byte From, byte To)> Moves(IReadOnlyList<bool> occupied, bool toEnd)
    {
        var moves = new List<(byte From, byte To)>();
        var count = occupied.Count;

        //every slot that the scan passed and did not fill is empty, so the target is empty when an item moves to it
        if (toEnd)
        {
            var target = count;

            for (var slot = count; slot >= 1; slot--)
                if (occupied[slot - 1])
                {
                    if (slot != target)
                        moves.Add(((byte)slot, (byte)target));

                    target--;
                }
        } else
        {
            var target = 1;

            for (var slot = 1; slot <= count; slot++)
                if (occupied[slot - 1])
                {
                    if (slot != target)
                        moves.Add(((byte)slot, (byte)target));

                    target++;
                }
        }

        return moves;
    }
}
