#region
using Brigid.Data.Abstractions;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using DALib.Utility;
#endregion

namespace Brigid.Data.Repositories;

public sealed class PanelSpriteRepository : RepositoryBase
{
    private const int ITEMS_PER_FILE = 266;

    private readonly Palette Gui06Palette = Palette.FromEntry(DatArchives.Setoa["gui06.pal"]);

    private readonly PaletteLookup ItemPalettes = PaletteLookup.FromArchive("itempal", "item", DatArchives.Legend)
                                                               .Freeze();

    private string ConstructKeyForItemSpriteSheet(int fileId) => $"ITEMS_{fileId}";

    private int GetFileNumber(int itemId) => Convert.ToInt32(Math.Ceiling((decimal)itemId / ITEMS_PER_FILE));

    private Palettized<EpfFrame>? GetIconFrame(int spriteId, string sheetName)
    {
        var iconSheet = GetOrCreate(sheetName, () => EpfView.FromArchive(sheetName, DatArchives.Setoa));

        if (!iconSheet.TryGetValue(spriteId, out var frame))
            return null;

        return new Palettized<EpfFrame>
        {
            Entity = frame!,
            Palette = Gui06Palette
        };
    }

    public Palettized<EpfFrame>? GetItemSprite(int itemId)
    {
        //server-sent item sprites carry the 0x8000 "item display" flag; strip it to get the EPF index.
        itemId &= 0x7FFF;

        if (itemId == 0)
            return null;

        var fileId = GetFileNumber(itemId);
        var spriteSheet = GetItemSpriteSheet(fileId);
        var frameId = (itemId - 1) % ITEMS_PER_FILE;

        if (spriteSheet is null || !spriteSheet.TryGetValue(frameId, out var frame))
            return null;

        return new Palettized<EpfFrame>
        {
            Entity = frame!,
            Palette = ItemPalettes.GetPaletteForId(itemId)
        };
    }

    private EpfView? GetItemSpriteSheet(int fileId)
        => DatArchives.Legend.TryGetValue($"item{fileId:D3}.epf", out var entry)
            ? GetOrCreate(ConstructKeyForItemSpriteSheet(fileId), () => EpfView.FromEntry(entry))
            : null;

    public Palettized<EpfFrame>? GetSkillIcon(int spriteId) => GetIconFrame(spriteId, "skill001");
    public Palettized<EpfFrame>? GetSpellIcon(int spriteId) => GetIconFrame(spriteId, "spell001");

    //Learnable (skill002/spell002) and Locked (skill003/spell003) are no longer separate sheets — those visual
    //states are now produced by tinting the base icon at draw time. Artists ship a single source icon per sprite ID.
}