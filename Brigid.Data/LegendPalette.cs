#region
using Brigid.Data.Definitions;
using DALib.Drawing;
using SkiaSharp;
#endregion

namespace Brigid.Data;

/// <summary>
///     Provides named and indexed color access to the legend.pal palette from Legend.dat.
/// </summary>
public static class LegendPalette
{
    private static Palette Palette = null!;

    /// <summary>
    ///     Text color code char-to-LegendColor mapping. Valid codes are 'a' through 'x'.
    /// </summary>
    private static readonly LegendColor?[] TextColorMap = BuildTextColorMap();

    private static LegendColor?[] BuildTextColorMap()
    {
        //24 slots for 'a' (0) through 'x' (23)
        var map = new LegendColor?[24];

        map['a' - 'a'] = LegendColor.Silver;
        map['b' - 'a'] = LegendColor.Scarlet;
        map['c' - 'a'] = LegendColor.Gold;
        map['d' - 'a'] = LegendColor.DarkGreen;
        map['e' - 'a'] = LegendColor.CornflowerBlue;
        map['f' - 'a'] = LegendColor.Indigo;
        map['g' - 'a'] = LegendColor.LightGray;
        map['h' - 'a'] = LegendColor.Silver;
        map['i' - 'a'] = LegendColor.Gray;
        map['j' - 'a'] = LegendColor.DimGray;
        map['k' - 'a'] = LegendColor.DarkGray;
        map['l' - 'a'] = LegendColor.Charcoal;
        map['m' - 'a'] = LegendColor.Jet;
        map['n' - 'a'] = LegendColor.AlmostBlack;
        map['o' - 'a'] = LegendColor.HotPink;
        map['p' - 'a'] = LegendColor.DarkPurple;
        map['q' - 'a'] = LegendColor.Lime;
        map['r' - 'a'] = LegendColor.DarkGreen;
        map['s' - 'a'] = LegendColor.Orange;
        map['t' - 'a'] = LegendColor.Brown;
        map['u' - 'a'] = LegendColor.White;
        map['v' - 'a'] = LegendColor.CornflowerBlue;
        map['w' - 'a'] = LegendColor.HotPink;
        map['x' - 'a'] = LegendColor.Black;

        return map;
    }

    /// <summary>
    ///     Resolves a named legend color to its ARGB value from the loaded palette.
    /// </summary>
    public static SKColor GetColor(LegendColor color) => Palette[(byte)color];

    /// <summary>
    ///     Resolves a raw palette index (0-255) to its ARGB value.
    /// </summary>
    public static SKColor GetColor(int index) => Palette[index];

    /// <summary>
    ///     Maps a text color code character (the letter after {=) to its LegendColor, or null if invalid.
    /// </summary>
    public static LegendColor? GetTextColor(char code)
    {
        var index = code - 'a';

        if (index is < 0 or > 23)
            return null;

        return TextColorMap[index];
    }

    /// <summary>
    ///     Returns the underlying Palette instance for direct rendering use.
    /// </summary>
    public static Palette GetPalette() => Palette;

    public static void Initialize() => Palette = Palette.FromEntry(DatArchives.Legend["legend.pal"]);
}