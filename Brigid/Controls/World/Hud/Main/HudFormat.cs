#region
using System.Globalization;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>The text of the top bar and the meters of the new HUD (BRIG-127 D15, BRIG-148). Numbers use the invariant culture; the clock uses the player's.</summary>
internal static class HudFormat
{
    private const uint MILLION = 1_000_000;
    private const uint BILLION = 1_000_000_000;

    /// <summary>The gold, as <see cref="Amount" />.</summary>
    public static string Gold(uint gold) => Amount(gold);

    /// <summary>
    ///     An amount (the gold, a currency), with thousands separators up to 999,999, then in short form (1.2M, 12M, 1.2B).
    ///     The short form drops the digits after it and never rounds up, so it never shows more than the player has.
    /// </summary>
    public static string Amount(uint amount)
        => amount switch
        {
            < MILLION      => amount.ToString("N0", CultureInfo.InvariantCulture),
            < 10 * MILLION => $"{amount / MILLION}.{amount / (MILLION / 10) % 10}M",
            < BILLION      => $"{amount / MILLION}M",
            _              => $"{amount / BILLION}.{amount / (BILLION / 10) % 10}B"
        };

    /// <summary>The tooltip of the gold box: the full amount when the box shows the short form, else null.</summary>
    public static string? GoldTooltip(uint gold) => FullAmount(gold) is { } full ? $"{full} gold" : null;

    /// <summary>The full amount, for a tooltip, when <see cref="Amount" /> shows the short form; else null.</summary>
    public static string? FullAmount(uint amount) => amount < MILLION ? null : Number(amount);

    public static string Weight(int current, int max) => $"{current}/{max}";

    /// <summary>The value of a meter: the current and the maximum, with thousands separators.</summary>
    public static string Meter(uint current, uint max) => $"{Number(current)} / {Number(max)}";

    /// <summary>A number with thousands separators.</summary>
    public static string Number(ulong value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The level of the player, as the experience bar and the player plate show it.</summary>
    public static string Level(byte level) => $"Level {level}";

    /// <summary>The zone name and the player's tile; before a zone name arrives, the tile only.</summary>
    public static string Zone(string name, int x, int y) => name.Length == 0 ? $"({x}, {y})" : $"{name} ({x}, {y})";

    /// <summary>
    ///     The local time in the short time pattern of <paramref name="culture" />, with no seconds. ICU 72 and later put a
    ///     narrow no-break space (U+202F) before AM and PM, which a UI face can lack; the clock uses a plain space.
    /// </summary>
    public static string Clock(DateTime local, CultureInfo culture)
        => local.ToString(culture.DateTimeFormat.ShortTimePattern, culture).Replace('\u202F', ' ').Replace('\u00A0', ' ');
}
