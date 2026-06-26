#region
using System.Collections.Frozen;
using Brigid.Data.Models;
using Brigid.Data.Utilities;
using Chaos.DarkAges.Definitions;
using DALib.Data;
using DALib.Definitions;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using SkiaSharp;
using CONSTANTS = DALib.Definitions.CONSTANTS;
#endregion

namespace Brigid.Data.Repositories;

/// <summary>
///     Provides aisling palette lookups, dye color table, equipment EPF loading from Khan archives, and special sprite data
///     (swimming, rest positions, emotions).
/// </summary>
public sealed class AislingDrawDataRepository
{
    private readonly Dictionary<string, EpfView?> EpfCache = [];

    public AbilityAnimationTable AbilityAnimations { get; }

    public IDictionary<int, Palette> BodyPalettes { get; } = Palette.FromArchive("palm", DatArchives.Khanpal)
                                                                    .ToFrozenDictionary();

    private SKColor[]? DefaultDyeColors { get; }

    public ColorTable DyeColorTable { get; }

    public PaletteLookup PalB { get; } = PaletteLookup.FromArchive("palb", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalC { get; } = PaletteLookup.FromArchive("palc", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalE { get; } = PaletteLookup.FromArchive("pale", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalF { get; } = PaletteLookup.FromArchive("palf", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalH { get; } = PaletteLookup.FromArchive("palh", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalI { get; } = PaletteLookup.FromArchive("pali", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalL { get; } = PaletteLookup.FromArchive("pall", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalP { get; } = PaletteLookup.FromArchive("palp", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalU { get; } = PaletteLookup.FromArchive("palu", DatArchives.Khanpal)
                                                      .Freeze();

    public PaletteLookup PalW { get; } = PaletteLookup.FromArchive("palw", DatArchives.Khanpal)
                                                      .Freeze();

    public AislingDrawDataRepository()
    {
        if (DatArchives.Legend.TryGetValue("color0.tbl", out var entry))
            DyeColorTable = ColorTable.FromEntry(entry);
        else
            DyeColorTable = [];

        //cache the default (undyed) colors — entry 0 contains the placeholder colors at palette slots 98-103
        if (DyeColorTable.Contains(0))
            DefaultDyeColors = DyeColorTable[0].Colors;

        if (DatArchives.Legend.TryGetValue("Skill_e.tbl", out var skillEntry))
        {
            DatArchives.Legend.TryGetValue("Skill_i.tbl", out var overcoatEntry);
            AbilityAnimations = AbilityAnimationTableParser.Parse(skillEntry, overcoatEntry);
        } else
            AbilityAnimations = new AbilityAnimationTable([]);
    }

    /// <summary>
    ///     Returns a dyed copy of the palette for the given color, or the original palette if dyeing is not applicable.
    /// </summary>
    public Palette ApplyDye(Palette basePalette, DisplayColor color)
    {
        if (color == DisplayColor.Default)
            return basePalette;

        if (!IsDyeable(basePalette))
            return basePalette;

        var colorIndex = (int)color;

        if (!DyeColorTable.Contains(colorIndex))
            return basePalette;

        return basePalette.Dye(DyeColorTable[colorIndex]);
    }

    /// <summary>
    ///     Clears the cached EPF files. Call on map change to free memory.
    /// </summary>
    public void ClearEpfCache() => EpfCache.Clear();

    private static DataArchive GetArchive(char typeLetter, bool isMale)
        => typeLetter switch
        {
            >= 'a' and <= 'd' => isMale ? DatArchives.Khanmad : DatArchives.Khanwad,
            >= 'e' and <= 'h' => isMale ? DatArchives.Khanmeh : DatArchives.Khanweh,
            >= 'i' and <= 'm' => isMale ? DatArchives.Khanmim : DatArchives.Khanwim,
            >= 'n' and <= 's' => isMale ? DatArchives.Khanmns : DatArchives.Khanwns,
            >= 't' and <= 'z' => isMale ? DatArchives.Khanmtz : DatArchives.Khanwtz,
            _                 => isMale ? DatArchives.Khanmad : DatArchives.Khanwad
        };

    /// <summary>
    ///     Loads an equipment EPF file from the Khan archive determined by type letter and gender.
    /// </summary>
    /// <param name="typeLetter">
    ///     Equipment type letter (a-z) mapping to a specific Khan archive pair
    /// </param>
    /// <param name="isMale">
    ///     Selects the male (khanm*) or female (khanw*) archive variant
    /// </param>
    /// <param name="fileName">
    ///     EPF entry name without extension (e.g. "ma00101")
    /// </param>
    public EpfView? GetEquipmentEpf(char typeLetter, bool isMale, string fileName)
    {
        if (EpfCache.TryGetValue(fileName, out var cached))
            return cached;

        var archive = GetArchive(typeLetter, isMale);

        if (!archive.TryGetValue($"{fileName}.epf", out var entry))
        {
            EpfCache[fileName] = null;

            return null;
        }

        var epf = EpfView.FromEntry(entry);
        EpfCache[fileName] = epf;

        return epf;
    }

    /// <summary>
    ///     Maps an equipment type letter to its corresponding palette lookup (e.g. 'e' -&gt; PalE, 'f' -&gt; PalF).
    /// </summary>
    public PaletteLookup GetPaletteLookup(char typeLetter)
        => typeLetter switch
        {
            'a' or 'b' or 'n' => PalB,
            'c' or 'g' or 'j' => PalC,
            'e'               => PalE,
            'f'               => PalF,
            'h'               => PalH,
            'i'               => PalI,
            'l'               => PalL,
            'p' or 's'        => PalP,
            'u'               => PalU,
            'w'               => PalW,
            _                 => PalB
        };

    /// <summary>
    ///     Returns true if the palette supports dye application (contains unmodified placeholder dye slots).
    /// </summary>
    public bool IsDyeable(Palette palette)
    {
        if (DefaultDyeColors is null)
            return false;

        for (var i = 0; i < DefaultDyeColors.Length; i++)
            if (palette[CONSTANTS.PALETTE_DYE_INDEX_START + i] != DefaultDyeColors[i])
                return false;

        return true;
    }

    #region Swimming
    /// <summary>
    ///     Swimming sprite data: the sprite sheet, its palette, and the widest frame width for positioning.
    /// </summary>
    public readonly record struct SwimSpriteData(EpfFile Epf, Palette Palette, int MaxFrameWidth);

    private const string SWIM_MALE_EPF = "mb00501.epf";
    private const string SWIM_FEMALE_EPF = "wb00501.epf";

    private SwimSpriteData? SwimFemaleData;
    private SwimSpriteData? SwimMaleData;
    private bool SwimLoaded;

    /// <summary>
    ///     Returns the swimming sprite data for the given gender, or null if the swim EPF is unavailable.
    /// </summary>
    public SwimSpriteData? GetSwimData(bool isFemale)
    {
        EnsureSwimLoaded();

        return isFemale ? SwimFemaleData : SwimMaleData;
    }

    private void EnsureSwimLoaded()
    {
        if (SwimLoaded)
            return;

        SwimLoaded = true;

        if (DatArchives.Khanmad.TryGetValue(SWIM_MALE_EPF, out var maleEntry)
            && maleEntry.TryGetNumericIdentifier(out var maleId, 3))
        {
            var epf = EpfFile.FromEntry(maleEntry);
            var palette = PalB.GetPaletteForId(maleId, KhanPalOverrideType.Male);
            SwimMaleData = new SwimSpriteData(epf, palette, epf.Max(f => f.PixelWidth + Math.Max((int)f.Left, 0)));
        }

        if (DatArchives.Khanwad.TryGetValue(SWIM_FEMALE_EPF, out var femaleEntry)
            && femaleEntry.TryGetNumericIdentifier(out var femaleId, 3))
        {
            var epf = EpfFile.FromEntry(femaleEntry);
            var palette = PalB.GetPaletteForId(femaleId, KhanPalOverrideType.Female);
            SwimFemaleData = new SwimSpriteData(epf, palette, epf.Max(f => f.PixelWidth + Math.Max((int)f.Left, 0)));
        }
    }
    #endregion

    #region Rest Position
    private readonly SpfFile?[] RestFemaleEmoteSpf = new SpfFile?[4];
    private readonly SpfFile?[] RestFemaleSpf = new SpfFile?[4];
    private readonly SpfFile?[] RestMaleEmoteSpf = new SpfFile?[4];
    private readonly SpfFile?[] RestMaleSpf = new SpfFile?[4];
    private bool RestLoaded;

    /// <summary>
    ///     Returns the rest position SPF file for the given gender and seat position. Returns null if unavailable.
    /// </summary>
    /// <param name="isFemale">True for female sprite variant.</param>
    /// <param name="position">Rest seat position (1-3).</param>
    /// <param name="isEmote">True to get the emote overlay variant instead of the base body sprite.</param>
    public SpfFile? GetRestSpf(bool isFemale, int position, bool isEmote)
    {
        EnsureRestLoaded();

        if (position is < 1 or > 3)
            return null;

        if (isEmote)
            return isFemale ? RestFemaleEmoteSpf[position] : RestMaleEmoteSpf[position];

        return isFemale ? RestFemaleSpf[position] : RestMaleSpf[position];
    }

    private void EnsureRestLoaded()
    {
        if (RestLoaded)
            return;

        RestLoaded = true;

        for (var i = 1; i <= 3; i++)
        {
            if (DatArchives.Khanmad.TryGetValue($"m_r_{i:D3}.spf", out var maleEntry))
                RestMaleSpf[i] = SpfFile.FromEntry(maleEntry);

            if (DatArchives.Khanmad.TryGetValue($"m_r_{i:D3}e.spf", out var maleEmoteEntry))
                RestMaleEmoteSpf[i] = SpfFile.FromEntry(maleEmoteEntry);

            if (DatArchives.Khanwad.TryGetValue($"w_r_{i:D3}.spf", out var femaleEntry))
                RestFemaleSpf[i] = SpfFile.FromEntry(femaleEntry);

            if (DatArchives.Khanwad.TryGetValue($"w_r_{i:D3}e.spf", out var femaleEmoteEntry))
                RestFemaleEmoteSpf[i] = SpfFile.FromEntry(femaleEmoteEntry);
        }
    }
    #endregion

    #region Emotions
    /// <summary>
    ///     The emotion overlay EPF (emot01.epf) from the Legend archive.
    /// </summary>
    public EpfView? EmotionsEpf { get; } = LoadEmotionsEpf();

    private static EpfView? LoadEmotionsEpf()
    {
        if (!DatArchives.Legend.TryGetValue("emot01.epf", out var entry))
            return null;

        return EpfView.FromEntry(entry);
    }
    #endregion
}