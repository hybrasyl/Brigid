#region
using System.Collections.Frozen;
#endregion

namespace Chaos.Client.Definitions;

/// <summary>
///     Hardcoded door tile mapping (66 pairs). Maps closed foreground tile IDs to their open counterparts via
///     bidirectional lookup.
/// </summary>
public static class DoorTable
{
    private static readonly FrozenDictionary<short, short> ClosedToOpen;
    private static readonly FrozenDictionary<short, short> OpenToClosed;

    static DoorTable()
    {
        //sorted by closed sprite id. see doors.md in the document repo for door-group context (axis, tile count, center-only flag).
        var pairs = new (short Closed, short Open)[]
        {
            (1993, 1996), (1994, 1997),
            (2000, 2003), (2001, 2004),
            (2163, 2167), (2164, 2168), (2165, 2169),
            (2196, 2192), (2197, 2193), (2198, 2194),
            (2227, 2231), (2228, 2232), (2229, 2233),
            (2260, 2264), (2261, 2265), (2262, 2266),
            (2291, 2295), (2292, 2296), (2293, 2297),
            (2328, 2324), (2329, 2325), (2330, 2326),
            (2436, 2432),
            (2461, 2465),
            (2673, 2680), (2674, 2681),
            (2688, 2695), (2689, 2696),
            (2714, 2721), (2715, 2722),
            (2727, 2734), (2728, 2735),
            (2761, 2768), (2762, 2769),
            (2776, 2783), (2777, 2784),
            (2850, 2857), (2851, 2858), (2852, 2859),
            (2874, 2881), (2875, 2882), (2876, 2883),
            (2898, 2904),
            (2929, 2923),
            (2946, 2952),
            (2971, 2977),
            (2994, 3000),
            (3019, 3025),
            (3059, 3067),
            (3090, 3098),
            (3119, 3127),
            (3150, 3158),
            (3179, 3187),
            (3210, 3218),
            (8262, 8263),
            (11916, 11919), (11917, 11920),
            (11944, 11941), (11945, 11942),
            (12183, 12179),
            (12266, 12271),
            (12273, 12234),
            (12379, 11448),
            (12380, 11445),
            (12484, 12485),
            (12689, 12693),
            (12703, 12707),
            (13833, 13825),
            (14874, 14904), (14875, 14905), (14876, 14906), (14877, 14907),
            (14878, 14908), (14879, 14909), (14880, 14910), (14881, 14911),
            (15334, 15364), (15335, 15365), (15336, 15366), (15337, 15367),
            (15338, 15368), (15339, 15369), (15340, 15370), (15341, 15371),
            (18411, 18415), (18412, 18416),
            (18424, 18429), (18425, 18430),
            (18445, 18447), (18446, 18448),
            (18466, 18470), (18467, 18471),
            (18489, 18492), (18490, 18493),
            (18496, 18499), (18497, 18500),
            (18503, 18506), (18504, 18507),
            (18509, 18513), (18510, 18514),
            (18516, 18521), (18517, 18522),
            (18524, 18529), (18525, 18530),
            (18533, 18536), (18534, 18537),
            (18539, 18543), (18540, 18544),
            (18559, 18562), (18560, 18563),
            (18565, 18570), (18566, 18571),
            (18576, 18580), (18577, 18581),
            (18589, 18594), (18590, 18595),
            (18610, 18612), (18611, 18613),
            (18631, 18635), (18632, 18636),
            (18649, 18697), (18695, 18698),
            (18652, 18655), (18653, 18656),
            (18659, 18662), (18660, 18663),
            (18666, 18669), (18667, 18670),
            (18672, 18676), (18673, 18677),
            (18679, 18683), (18680, 18684),
            (18686, 18690), (18687, 18691),
            (18700, 18704), (18701, 18705),
            (18708, 18711), (18709, 18712),
            (18714, 18718), (18715, 18719)
        };

        var closedToOpen = new Dictionary<short, short>(pairs.Length);
        var openToClosed = new Dictionary<short, short>(pairs.Length);

        foreach ((var closed, var open) in pairs)
        {
            closedToOpen[closed] = open;
            openToClosed[open] = closed;
        }

        ClosedToOpen = closedToOpen.ToFrozenDictionary();
        OpenToClosed = openToClosed.ToFrozenDictionary();
    }

    /// <summary>
    ///     Gets the closed tile ID for an open tile. Returns null if the tile is not a recognized door.
    /// </summary>
    public static short? GetClosedTileId(short openTileId) => OpenToClosed.TryGetValue(openTileId, out var closed) ? closed : null;

    /// <summary>
    ///     Gets the open tile ID for a closed tile. Returns null if the tile is not a recognized door.
    /// </summary>
    public static short? GetOpenTileId(short closedTileId) => ClosedToOpen.TryGetValue(closedTileId, out var open) ? open : null;

    /// <summary>
    ///     Returns true when the tile id matches either the closed or open variant of any catalogued door pair.
    /// </summary>
    public static bool IsDoorTile(short tileId) => ClosedToOpen.ContainsKey(tileId) || OpenToClosed.ContainsKey(tileId);

    /// <summary>
    ///     Enumerates door counterparts of a given tile id. If the tile is a known closed door, yields its open variant.
    ///     If the tile is a known open door, yields its closed variant. Empty otherwise. Used by map preload to ensure
    ///     both sides of every door end up in the foreground atlas.
    /// </summary>
    public static IEnumerable<short> GetVariants(short tileId)
    {
        if (ClosedToOpen.TryGetValue(tileId, out var open))
            yield return open;

        if (OpenToClosed.TryGetValue(tileId, out var closed))
            yield return closed;
    }
}
