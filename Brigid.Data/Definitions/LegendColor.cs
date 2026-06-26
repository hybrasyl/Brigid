namespace Brigid.Data.Definitions;

/// <summary>
///     Named colors from legend.pal (palette bank 0). The underlying byte value is the palette index.
/// </summary>
public enum LegendColor : byte
{
    /// <summary>
    ///     (0, 0, 0)
    /// </summary>
    Black = 0x00,

    /// <summary>
    ///     (0, 255, 255)
    /// </summary>
    Cyan = 0x01,

    /// <summary>
    ///     (255, 0, 0)
    /// </summary>
    Red = 0x02,

    /// <summary>
    ///     (255, 255, 255)
    /// </summary>
    White = 0x10,

    /// <summary>
    ///     (235, 235, 235)
    /// </summary>
    Platinum = 0x11,

    /// <summary>
    ///     (219, 219, 219)
    /// </summary>
    LightGray = 0x12,

    /// <summary>
    ///     (203, 203, 203)
    /// </summary>
    PaleSilver = 0x13,

    /// <summary>
    ///     (187, 187, 187)
    /// </summary>
    Silver = 0x14,

    /// <summary>
    ///     (151, 151, 151)
    /// </summary>
    Gray = 0x16,

    /// <summary>
    ///     (119, 119, 119)
    /// </summary>
    DimGray = 0x18,

    /// <summary>
    ///     (83, 83, 83)
    /// </summary>
    DarkGray = 0x1A,

    /// <summary>
    ///     (51, 51, 51)
    /// </summary>
    Charcoal = 0x1C,

    /// <summary>
    ///     (15, 15, 15)
    /// </summary>
    Jet = 0x1E,

    /// <summary>
    ///     (3, 3, 3)
    /// </summary>
    AlmostBlack = 0x1F,

    /// <summary>
    ///     (255, 231, 235)
    /// </summary>
    Blush = 0x20,

    /// <summary>
    ///     (247, 91, 143)
    /// </summary>
    HotPink = 0x24,

    /// <summary>
    ///     (203, 0, 23)
    /// </summary>
    Scarlet = 0x28,

    /// <summary>
    ///     (255, 179, 119)
    /// </summary>
    Peach = 0x31,

    /// <summary>
    ///     (235, 139, 67)
    /// </summary>
    DustyOrange = 0x32,

    /// <summary>
    ///     (203, 131, 75)
    /// </summary>
    Copper = 0x3F,

    /// <summary>
    ///     (255, 255, 219)
    /// </summary>
    Cream = 0x40,

    /// <summary>
    ///     (251, 255, 115)
    /// </summary>
    PastelYellow = 0x43,

    /// <summary>
    ///     (255, 243, 71)
    /// </summary>
    CanaryYellow = 0x44,

    /// <summary>
    ///     (255, 231, 59)
    /// </summary>
    Gold = 0x45,

    /// <summary>
    ///     (183, 231, 103)
    /// </summary>
    SpringGreen = 0x4B,

    /// <summary>
    ///     (115, 183, 175)
    /// </summary>
    Seafoam = 0x54,

    /// <summary>
    ///     (127, 167, 243)
    /// </summary>
    CornflowerBlue = 0x58,

    /// <summary>
    ///     (35, 27, 155)
    /// </summary>
    Indigo = 0x5C,

    /// <summary>
    ///     (219, 195, 239)
    /// </summary>
    LightLavender = 0x60,

    /// <summary>
    ///     (143, 91, 163)
    /// </summary>
    DeepLavender = 0x64,

    /// <summary>
    ///     (227, 167, 227)
    /// </summary>
    Orchid = 0x69,

    /// <summary>
    ///     (119, 27, 119)
    /// </summary>
    DarkPurple = 0x6D,

    /// <summary>
    ///     (143, 151, 55)
    /// </summary>
    Olive = 0x77,

    /// <summary>
    ///     (55, 67, 7)
    /// </summary>
    DarkOlive = 0x7D,

    /// <summary>
    ///     (0, 255, 0)
    /// </summary>
    Lime = 0x80,

    /// <summary>
    ///     (0, 99, 0)
    /// </summary>
    DarkGreen = 0x89,

    /// <summary>
    ///     (243, 143, 27)
    /// </summary>
    Orange = 0x97,

    /// <summary>
    ///     (239, 111, 11)
    /// </summary>
    BurntOrange = 0x98,

    /// <summary>
    ///     (171, 119, 91)
    /// </summary>
    Tan = 0xA0,

    /// <summary>
    ///     (99, 55, 27)
    /// </summary>
    Brown = 0xA7,

    /// <summary>
    ///     (27, 127, 127)
    /// </summary>
    Teal = 0xFF
}