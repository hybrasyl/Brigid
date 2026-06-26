#region
using Brigid.Data;
using Brigid.Data.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Rendering.Definitions;

/// <summary>
///     Converts <see cref="LegendColor" /> enum values to MonoGame <see cref="Color" />. Call <see cref="Initialize" />
///     after <see cref="LegendPalette" /> is loaded.
/// </summary>
public static class LegendColors
{
    public static Color AlmostBlack { get; private set; }
    public static Color Black { get; private set; }
    public static Color Blush { get; private set; }
    public static Color Brown { get; private set; }
    public static Color BurntOrange { get; private set; }
    public static Color CanaryYellow { get; private set; }
    public static Color Charcoal { get; private set; }
    public static Color Copper { get; private set; }
    public static Color CornflowerBlue { get; private set; }
    public static Color Cream { get; private set; }
    public static Color Cyan { get; private set; }
    public static Color DarkGray { get; private set; }
    public static Color DarkGreen { get; private set; }
    public static Color DarkOlive { get; private set; }
    public static Color DarkPurple { get; private set; }
    public static Color DeepLavender { get; private set; }
    public static Color DimGray { get; private set; }
    public static Color DustyOrange { get; private set; }
    public static Color Gold { get; private set; }
    public static Color Gray { get; private set; }
    public static Color HotPink { get; private set; }
    public static Color Indigo { get; private set; }
    public static Color Jet { get; private set; }
    public static Color LightGray { get; private set; }
    public static Color LightLavender { get; private set; }
    public static Color Lime { get; private set; }
    public static Color Olive { get; private set; }
    public static Color Orange { get; private set; }
    public static Color Orchid { get; private set; }
    public static Color PaleSilver { get; private set; }
    public static Color PastelYellow { get; private set; }
    public static Color Peach { get; private set; }
    public static Color Platinum { get; private set; }
    public static Color Red { get; private set; }
    public static Color Scarlet { get; private set; }
    public static Color Seafoam { get; private set; }
    public static Color Silver { get; private set; }
    public static Color SpringGreen { get; private set; }
    public static Color Tan { get; private set; }
    public static Color Teal { get; private set; }
    public static Color White { get; private set; }

    public static Color Get(LegendColor color)
    {
        var sk = LegendPalette.GetColor(color);

        return new Color(sk.Red, sk.Green, sk.Blue);
    }

    public static Color Get(int paletteIndex)
    {
        var sk = LegendPalette.GetColor(paletteIndex);

        return new Color(sk.Red, sk.Green, sk.Blue);
    }

    public static void Initialize()
    {
        foreach (var value in Enum.GetValues<LegendColor>())
        {
            var color = Get(value);

            switch (value)
            {
                case LegendColor.Black:
                    Black = color;

                    break;
                case LegendColor.Cyan:
                    Cyan = color;

                    break;
                case LegendColor.Red:
                    Red = color;

                    break;
                case LegendColor.White:
                    White = color;

                    break;
                case LegendColor.Platinum:
                    Platinum = color;

                    break;
                case LegendColor.LightGray:
                    LightGray = color;

                    break;
                case LegendColor.PaleSilver:
                    PaleSilver = color;

                    break;
                case LegendColor.Silver:
                    Silver = color;

                    break;
                case LegendColor.Gray:
                    Gray = color;

                    break;
                case LegendColor.DimGray:
                    DimGray = color;

                    break;
                case LegendColor.DarkGray:
                    DarkGray = color;

                    break;
                case LegendColor.Charcoal:
                    Charcoal = color;

                    break;
                case LegendColor.Jet:
                    Jet = color;

                    break;
                case LegendColor.AlmostBlack:
                    AlmostBlack = color;

                    break;
                case LegendColor.Blush:
                    Blush = color;

                    break;
                case LegendColor.HotPink:
                    HotPink = color;

                    break;
                case LegendColor.Scarlet:
                    Scarlet = color;

                    break;
                case LegendColor.Peach:
                    Peach = color;

                    break;
                case LegendColor.DustyOrange:
                    DustyOrange = color;

                    break;
                case LegendColor.Copper:
                    Copper = color;

                    break;
                case LegendColor.Cream:
                    Cream = color;

                    break;
                case LegendColor.PastelYellow:
                    PastelYellow = color;

                    break;
                case LegendColor.CanaryYellow:
                    CanaryYellow = color;

                    break;
                case LegendColor.Gold:
                    Gold = color;

                    break;
                case LegendColor.SpringGreen:
                    SpringGreen = color;

                    break;
                case LegendColor.Seafoam:
                    Seafoam = color;

                    break;
                case LegendColor.CornflowerBlue:
                    CornflowerBlue = color;

                    break;
                case LegendColor.Indigo:
                    Indigo = color;

                    break;
                case LegendColor.LightLavender:
                    LightLavender = color;

                    break;
                case LegendColor.DeepLavender:
                    DeepLavender = color;

                    break;
                case LegendColor.Orchid:
                    Orchid = color;

                    break;
                case LegendColor.DarkPurple:
                    DarkPurple = color;

                    break;
                case LegendColor.Olive:
                    Olive = color;

                    break;
                case LegendColor.DarkOlive:
                    DarkOlive = color;

                    break;
                case LegendColor.Lime:
                    Lime = color;

                    break;
                case LegendColor.DarkGreen:
                    DarkGreen = color;

                    break;
                case LegendColor.Orange:
                    Orange = color;

                    break;
                case LegendColor.BurntOrange:
                    BurntOrange = color;

                    break;
                case LegendColor.Tan:
                    Tan = color;

                    break;
                case LegendColor.Brown:
                    Brown = color;

                    break;
                case LegendColor.Teal:
                    Teal = color;

                    break;
            }
        }
    }
}