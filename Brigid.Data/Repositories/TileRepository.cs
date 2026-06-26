#region
using Brigid.Data.Models;
using Brigid.Data.Utilities;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using DALib.Definitions;
using DALib.Utility;
using System.Runtime.InteropServices;
#endregion

namespace Brigid.Data.Repositories;

public sealed class TileRepository
{
    private readonly TileAnimationTable BgAnimations = DatArchives.Seo.TryGetValue("gndani.tbl", out var bgAnimEntry)
        ? TileAnimationTable.FromEntry(bgAnimEntry)
        : new TileAnimationTable();

    private readonly TileAnimationTable FgAnimations = DatArchives.Ia.TryGetValue("stcani.tbl", out var fgAnimEntry)
        ? TileAnimationTable.FromEntry(fgAnimEntry)
        : new TileAnimationTable();

    private TilesetView Tileset = TilesetView.FromArchive("tilea", DatArchives.Seo);
    private bool UseSnowTileset;

    public PaletteLookup BackgroundPaletteLookup { get; } = PaletteLookup.FromArchive("mpt", DatArchives.Seo)
                                                                         .Freeze();

    public PaletteLookup ForegroundPaletteLookup { get; } = PaletteLookup.FromArchive("stc", DatArchives.Ia)
                                                                         .Freeze();

    /// <summary>
    ///     Background tile ID to ground attribute mapping (color tint, walk-blocking, foreground height override). Parsed from
    ///     gndattr.tbl.
    /// </summary>
    public Dictionary<int, GroundAttribute> GroundAttributes { get; } = DatArchives.Seo.TryGetValue("gndattr.tbl", out var gndAttrEntry)
        ? GroundAttributeParser.Parse(gndAttrEntry)
        : [];

    /// <summary>
    ///     SOTP (Sector Object Type Properties) raw byte data from the Ia archive. Each byte encodes tile properties such as
    ///     walkability for a given foreground tile index.
    /// </summary>
    public TileFlags[] SotpData { get; } = DatArchives.Ia.TryGetValue("sotp.dat", out var sotpEntry)
        ? MemoryMarshal.Cast<byte, TileFlags>(sotpEntry.ToSpan())
                       .ToArray()
        : [];

    public Palettized<Tile>? GetBackgroundTile(int tileId)
    {
        if ((tileId <= 0) || ((tileId - 1) >= Tileset.Count))
            return null;

        return new Palettized<Tile>
        {
            Entity = Tileset[tileId - 1],
            Palette = BackgroundPaletteLookup.GetPaletteForId(tileId + 1)
        };
    }

    public TileAnimationEntry? GetBgAnimation(int tileId) => BgAnimations.TryGetEntry(tileId, out var entry) ? entry : null;

    public TileAnimationEntry? GetFgAnimation(int tileId) => FgAnimations.TryGetEntry(tileId, out var entry) ? entry : null;

    public Palettized<CompressedHpfFile>? GetForegroundTile(int tileId)
    {
        if (!DatArchives.Ia.TryGetValue($"stc{tileId:D5}.hpf", out var entry))
            return null;

        return new Palettized<CompressedHpfFile>
        {
            Entity = CompressedHpfFile.FromEntry(entry),
            Palette = ForegroundPaletteLookup.GetPaletteForId(tileId + 1)
        };
    }

    public void ToggleSnowTileset()
    {
        UseSnowTileset = !UseSnowTileset;

        Tileset = TilesetView.FromArchive(UseSnowTileset ? "tileas" : "tilea", DatArchives.Seo);
    }
}