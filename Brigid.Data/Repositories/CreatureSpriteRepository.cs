#region
using System.Collections.Concurrent;
using System.Collections.Frozen;
using Brigid.Data.Abstractions;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using DALib.Utility;
#endregion

namespace Brigid.Data.Repositories;

public sealed class CreatureSpriteRepository : RepositoryBase
{
    private readonly IDictionary<int, Palette> Palettes = Palette.FromArchive("mns", DatArchives.Hades)
                                                                 .ToFrozenDictionary();

    //direct strongly-typed cache avoids MemoryCache's object-key boxing on the per-frame hot path
    //null entries are cached to suppress retries on corrupt/missing sprite ids
    private readonly ConcurrentDictionary<int, Palettized<MpfView>?> SpriteCache = new();

    public Palettized<MpfView>? GetCreatureSprite(int spriteId)
    {
        if (spriteId == 0)
            return null;

        if (SpriteCache.TryGetValue(spriteId, out var cached))
            return cached;

        Palettized<MpfView>? sprite;

        try
        {
            sprite = LoadCreatureSprite(spriteId);
        }
        //rule 6 exemption: corrupt asset -> graceful null fallback (no validate-before-parse path)
        catch
        {
            sprite = null;
        }

        SpriteCache.TryAdd(spriteId, sprite);

        return sprite;
    }

    private Palettized<MpfView>? LoadCreatureSprite(int spriteId)
    {
        if (!DatArchives.Hades.TryGetValue($"mns{spriteId:D3}.mpf", out var entry))
            return null;

        var mpfFile = MpfView.FromEntry(entry);

        if (Palettes.TryGetValue(mpfFile.PaletteNumber, out var palette))
            return new Palettized<MpfView>
            {
                Entity = mpfFile,
                Palette = palette
            };

        return null;
    }
}