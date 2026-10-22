namespace Brigid.Data.AssetPacks;

/// <summary>
///     The rules for item sprite ids (BRIG-77). Legacy icons use ids 1 to 16226. Modern icons use ids from
///     <see cref="MODERN_FIRST" /> to <see cref="MAX" />. On the wire the server adds 0x8000 to an item sprite and sends
///     the sum as a u16, so the client removes that flag before a lookup.
/// </summary>
public static class ItemSpriteIds
{
    /// <summary>The first modern item sprite id.</summary>
    public const int MODERN_FIRST = 20001;

    /// <summary>The largest item sprite id: <c>sprite + 0x8000</c> must fit in a u16.</summary>
    public const int MAX = 32767;

    //the server adds 0x8000 to every item sprite on the wire, so the item id is the low 15 bits
    private const int WIRE_ID_MASK = 0x7FFF;

    /// <summary>The item sprite id of a value that can carry the 0x8000 wire flag.</summary>
    public static int ToSpriteId(int sprite) => sprite & WIRE_ID_MASK;
}
