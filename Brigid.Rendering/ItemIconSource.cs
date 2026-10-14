#region
using Brigid.Data;
using Brigid.Data.AssetPacks;
using Chaos.DarkAges.Definitions;
using DALib.Drawing;
using SkiaSharp;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Gives the image of an item sprite, for every item surface: ground items and the dialog portrait
///     (<see cref="ItemRenderer" />), and the inventory, bank, shop, item menu, exchange and equipment icons
///     (<see cref="UiRenderer.GetItemIcon" />). It removes the item flag of a wire value, asks the <c>item_icons</c> packs
///     in registration order, and then reads the legacy EPF. A pack entry that does not decode passes the lookup to the
///     next pack.
/// </summary>
public static class ItemIconSource
{
    /// <summary>
    ///     Loads the image for <paramref name="sprite" /> with the server's dye <paramref name="color" />, or returns
    ///     null when no pack and no legacy sheet has the sprite (and for sprite 0, which means no sprite). The caller
    ///     owns the returned image.
    /// </summary>
    public static ItemImage? Load(int sprite, byte color)
    {
        var spriteId = ItemSpriteIds.ToSpriteId(sprite);

        if (spriteId == 0)
            return null;

        foreach (var pack in AssetPackRegistry.ItemPacks)
            if (TryLoadFromPack(pack, spriteId, color, out var image))
                return new ItemImage(image, true, 0, 0);

        return LoadFromLegacy(spriteId, color);
    }

    private static bool TryLoadFromPack(ItemPack pack, int spriteId, byte color, out SKImage image)
    {
        image = null!;

        if (!pack.HasItem(spriteId) || !pack.TryGetItemImage(spriteId, out var decoded) || (decoded is null))
            return false;

        image = DyeFromPack(pack, spriteId, decoded, color);

        return true;
    }

    //the pack that gives the image also decides the dye. gives the dyed image and disposes the source, or gives the
    //source when there is no dye
    private static SKImage DyeFromPack(ItemPack pack, int spriteId, SKImage source, byte color)
    {
        if ((color == 0) || !pack.IsDyeable(spriteId, source) || !DataContext.AislingDrawData.DyeColorTable.Contains(color))
            return source;

        var dyed = ItemDyePass.ApplyDyeFindReplace(source, DataContext.AislingDrawData.DyeColorTable[color]);
        source.Dispose();

        return dyed;
    }

    private static ItemImage? LoadFromLegacy(int spriteId, byte color)
    {
        var palettized = DataContext.PanelSprites.GetItemSprite(spriteId);

        if (palettized is null)
            return null;

        //ApplyDye leaves a palette without the dye ramp as it is, so a dye cannot overwrite unrelated colors
        var palette = DataContext.AislingDrawData.ApplyDye(palettized.Palette, (DisplayColor)color);
        var image = Graphics.RenderImage(palettized.Entity, palette);

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        return image is null ? null : new ItemImage(image, false, palettized.Entity.Left, palettized.Entity.Top);
    }
}

/// <summary>
///     An item image and where it came from. A pack image is tightly cropped, so its frame offsets are 0. A legacy
///     image keeps its EPF frame's Left/Top padding.
/// </summary>
public readonly record struct ItemImage(SKImage Image, bool IsFromPack, int FrameLeft, int FrameTop);
