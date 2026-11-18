#region
using System.Globalization;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>The text of the top bar and the meters of the new HUD (BRIG-127 D15, BRIG-148). Numbers use the invariant culture; the clock uses the player's.</summary>
internal static class HudFormat
{
    private const uint MILLION = 1_000_000;
    private const uint BILLION = 1_000_000_000;

    /// <summary>
    ///     The gold, with thousands separators up to 999,999, then in short form (1.2M, 12M, 1.2B). The short form drops the
    ///     digits after it and never rounds up, so it never shows more gold than the player has.
    /// </summary>
    public static string Gold(uint gold)
        => gold switch
        {
            < MILLION      => gold.ToString("N0", CultureInfo.InvariantCulture),
            < 10 * MILLION => $"{gold / MILLION}.{gold / (MILLION / 10) % 10}M",
            < BILLION      => $"{gold / MILLION}M",
            _              => $"{gold / BILLION}.{gold / (BILLION / 10) % 10}B"
        };

    /// <summary>The tooltip of the gold box: the full amount when the box shows the short form, else null.</summary>
    public static string? GoldTooltip(uint gold) => gold < MILLION ? null : $"{gold.ToString("N0", CultureInfo.InvariantCulture)} gold";

    public static string Weight(int current, int max) => $"{current}/{max}";

    /// <summary>The value of a meter: the current and the maximum, with thousands separators.</summary>
    public static string Meter(uint current, uint max)
        => $"{current.ToString("N0", CultureInfo.InvariantCulture)} / {max.ToString("N0", CultureInfo.InvariantCulture)}";

    /// <summary>The zone name and the player's tile; before a zone name arrives, the tile only.</summary>
    public static string Zone(string name, int x, int y) => name.Length == 0 ? $"({x}, {y})" : $"{name} ({x}, {y})";

    /// <summary>
    ///     The local time in the short time pattern of <paramref name="culture" />, with no seconds. ICU 72 and later put a
    ///     narrow no-break space (U+202F) before AM and PM, which a UI face can lack; the clock uses a plain space.
    /// </summary>
    public static string Clock(DateTime local, CultureInfo culture)
        => local.ToString(culture.DateTimeFormat.ShortTimePattern, culture).Replace('\u202F', ' ').Replace('\u00A0', ' ');
}
