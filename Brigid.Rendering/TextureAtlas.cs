#region
using System.Collections.Frozen;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     A rectangular sub-region within an atlas page texture, identified by the page texture and source rectangle.
/// </summary>
public readonly record struct AtlasRegion(Texture2D Atlas, Rectangle SourceRect);

/// <summary>
///     Packs multiple small textures into one or more large atlas textures. Supports grid packing (uniform size) and shelf
///     packing (variable size). After calling <see cref="Build" />, regions can be looked up by key.
/// </summary>
public sealed class TextureAtlas : IDisposable
{
    private const int BYTES_PER_PIXEL = 4;
    private const int DEFAULT_MAX_PAGE_SIZE = 2048;
    private const int MAX_SHELF_ENTRY_SIZE = 512;

    private readonly Dictionary<int, AtlasRegion> BuildIntRegions = [];
    private readonly Dictionary<string, AtlasRegion> BuildRegions = new(StringComparer.OrdinalIgnoreCase);

    private readonly int CellHeight;
    private readonly int CellWidth;
    private readonly GraphicsDevice Device;
    private readonly int MaxPageSize;
    private readonly PackingMode Mode;
    private readonly List<Texture2D> Pages = [];
    private readonly List<PendingEntry> PendingEntries = [];

    private FrozenDictionary<int, AtlasRegion> IntRegions = FrozenDictionary<int, AtlasRegion>.Empty;
    private FrozenDictionary<string, AtlasRegion> Regions = FrozenDictionary<string, AtlasRegion>.Empty;

    /// <summary>
    ///     The total number of entries in the built atlas. Only meaningful after <see cref="Build" /> has been called.
    /// </summary>
    public int EntryCount => Regions.Count + IntRegions.Count;

    /// <summary>
    ///     The number of GPU page textures backing the atlas. Only meaningful after <see cref="Build" /> has been called.
    /// </summary>
    public int PageCount => Pages.Count;

    public TextureAtlas(GraphicsDevice device, PackingMode mode, int maxPageSize = DEFAULT_MAX_PAGE_SIZE)
    {
        Device = device;
        Mode = mode;
        MaxPageSize = maxPageSize;
    }

    public TextureAtlas(
        GraphicsDevice device,
        PackingMode mode,
        int cellWidth,
        int cellHeight,
        int maxPageSize = DEFAULT_MAX_PAGE_SIZE)
        : this(device, mode, maxPageSize)
    {
        CellWidth = cellWidth;
        CellHeight = cellHeight;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var page in Pages)
            page.Dispose();

        Pages.Clear();
        Regions = FrozenDictionary<string, AtlasRegion>.Empty;
        IntRegions = FrozenDictionary<int, AtlasRegion>.Empty;
        BuildRegions.Clear();
        BuildIntRegions.Clear();
        PendingEntries.Clear();
    }

    /// <summary>
    ///     Stages a Texture2D to be packed on the next <see cref="Build" /> call. Reads pixel data from the GPU immediately.
    /// </summary>
    public void Add(string key, Texture2D source)
    {
        var pixels = new Color[source.Width * source.Height];
        source.GetData(pixels);

        PendingEntries.Add(
            new PendingEntry(
                key,
                null,
                pixels,
                null,
                source.Width,
                source.Height));
    }

    /// <summary>
    ///     Adds raw pixel data with a string key to be packed into the atlas on the next Build() call.
    /// </summary>
    public void Add(
        string key,
        Color[] pixels,
        int width,
        int height)
        => PendingEntries.Add(
            new PendingEntry(
                key,
                null,
                pixels,
                null,
                width,
                height));

    /// <summary>
    ///     Stages a Texture2D with an integer key to be packed on the next <see cref="Build" /> call. Reads pixel data from
    ///     the GPU immediately.
    /// </summary>
    public void Add(int key, Texture2D source)
    {
        var pixels = new Color[source.Width * source.Height];
        source.GetData(pixels);

        PendingEntries.Add(
            new PendingEntry(
                null,
                key,
                pixels,
                null,
                source.Width,
                source.Height));
    }

    /// <summary>
    ///     Adds raw pixel data with an integer key to be packed into the atlas on the next Build() call.
    /// </summary>
    public void Add(
        int key,
        Color[] pixels,
        int width,
        int height)
        => PendingEntries.Add(
            new PendingEntry(
                null,
                key,
                pixels,
                null,
                width,
                height));

    /// <summary>
    ///     Adds an SKImage to be packed into the atlas on the next Build() call. The image's pixels are blitted directly into
    ///     the atlas page during Build — no intermediate pixel extraction. The caller retains ownership of the image.
    /// </summary>
    public void Add(int key, SKImage image)
        => PendingEntries.Add(
            new PendingEntry(
                null,
                key,
                null,
                image,
                image.Width,
                image.Height));

    /// <summary>
    ///     Stages an SKImage with a string key to be packed on the next <see cref="Build" /> call. The image's pixels are
    ///     blitted directly into the atlas page during Build — no intermediate pixel extraction. The caller retains ownership.
    /// </summary>
    public void Add(string key, SKImage image)
        => PendingEntries.Add(
            new PendingEntry(
                key,
                null,
                null,
                image,
                image.Width,
                image.Height));

    /// <summary>
    ///     Writes a pending entry's pixels into the page buffer. Uses direct ReadPixels for SKImage entries (no intermediate
    ///     allocation) or array copy for Color[] entries.
    /// </summary>
    private static void BlitEntry(
        PendingEntry entry,
        Color[] pagePixels,
        int pageWidth,
        nint basePtr,
        int pageStride,
        int destX,
        int destY)
    {
        if (entry.Image is not null)
        {
            var byteOffset = (destY * pageWidth + destX) * BYTES_PER_PIXEL;

            var dstInfo = new SKImageInfo(
                entry.Width,
                entry.Height,
                SKColorType.Rgba8888,
                SKAlphaType.Premul);
            entry.Image.ReadPixels(dstInfo, basePtr + byteOffset, pageStride);
        } else if (entry.Pixels is not null)
            CopyPixels(
                entry.Pixels,
                entry.Width,
                entry.Height,
                pagePixels,
                pageWidth,
                destX,
                destY);
    }

    /// <summary>
    ///     Builds the atlas page textures from all pending entries. After this call, regions are available via TryGetRegion.
    /// </summary>
    public void Build()
    {
        if (PendingEntries.Count == 0)
            return;

        BuildRegions.Clear();
        BuildIntRegions.Clear();

        switch (Mode)
        {
            case PackingMode.Grid:
                BuildGrid();

                break;

            case PackingMode.Shelf:
                BuildShelf();

                break;
        }

        Regions = BuildRegions.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        IntRegions = BuildIntRegions.ToFrozenDictionary();
        BuildRegions.Clear();
        BuildIntRegions.Clear();
        PendingEntries.Clear();
    }

    private void BuildGrid()
    {
        if ((CellWidth <= 0) || (CellHeight <= 0))
            return;

        var tilesPerRow = MaxPageSize / CellWidth;

        if (tilesPerRow <= 0)
            return;

        var totalEntries = PendingEntries.Count;
        var tilesPerPage = tilesPerRow * (MaxPageSize / CellHeight);

        if (tilesPerPage <= 0)
            return;

        var pageCount = (totalEntries + tilesPerPage - 1) / tilesPerPage;

        for (var page = 0; page < pageCount; page++)
        {
            var pageStart = page * tilesPerPage;
            var pageEntryCount = Math.Min(tilesPerPage, totalEntries - pageStart);
            var pageRows = (pageEntryCount + tilesPerRow - 1) / tilesPerRow;
            var pageCols = Math.Min(pageEntryCount, tilesPerRow);
            var pageWidth = pageCols * CellWidth;
            var pageHeight = pageRows * CellHeight;

            var pixels = new Color[pageWidth * pageHeight];
            var pageTexture = new Texture2D(Device, pageWidth, pageHeight);

            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);

            try
            {
                var basePtr = handle.AddrOfPinnedObject();
                var pageStride = pageWidth * BYTES_PER_PIXEL;

                for (var i = 0; i < pageEntryCount; i++)
                {
                    var entry = PendingEntries[pageStart + i];
                    var col = i % tilesPerRow;
                    var row = i / tilesPerRow;
                    var destX = col * CellWidth;
                    var destY = row * CellHeight;

                    BlitEntry(
                        entry,
                        pixels,
                        pageWidth,
                        basePtr,
                        pageStride,
                        destX,
                        destY);

                    var sourceRect = new Rectangle(
                        destX,
                        destY,
                        entry.Width,
                        entry.Height);
                    var region = new AtlasRegion(pageTexture, sourceRect);

                    if (entry.StringKey is not null)
                        BuildRegions[entry.StringKey] = region;

                    if (entry.IntKey.HasValue)
                        BuildIntRegions[entry.IntKey.Value] = region;
                }
            } finally
            {
                handle.Free();
            }

            pageTexture.SetData(pixels);
            Pages.Add(pageTexture);
        }
    }

    private void BuildShelf()
    {
        //sort by height descending for better shelf packing
        PendingEntries.Sort((a, b) => b.Height.CompareTo(a.Height));

        var currentX = 0;
        var currentY = 0;
        var shelfHeight = 0;
        var pageEntries = new List<(PendingEntry Entry, int X, int Y)>();
        var maxWidthUsed = 0;

        foreach (var entry in PendingEntries)
        {
            //skip entries too large for atlas packing
            if ((entry.Width > MAX_SHELF_ENTRY_SIZE) || (entry.Height > MAX_SHELF_ENTRY_SIZE))
                continue;

            //does it fit on the current shelf?
            if ((currentX + entry.Width) > MaxPageSize)
            {
                //start a new shelf
                currentX = 0;
                currentY += shelfHeight;
                shelfHeight = 0;
            }

            //does it fit on the current page?
            if ((currentY + entry.Height) > MaxPageSize)
            {
                //flush current page and start a new one
                if (pageEntries.Count > 0)
                    FlushShelfPage(pageEntries, maxWidthUsed, currentY + shelfHeight);

                pageEntries.Clear();
                currentX = 0;
                currentY = 0;
                shelfHeight = 0;
                maxWidthUsed = 0;
            }

            pageEntries.Add((entry, currentX, currentY));
            shelfHeight = Math.Max(shelfHeight, entry.Height);
            currentX += entry.Width;
            maxWidthUsed = Math.Max(maxWidthUsed, currentX);
        }

        //flush remaining entries
        if (pageEntries.Count > 0)
            FlushShelfPage(pageEntries, maxWidthUsed, currentY + shelfHeight);
    }

    private static void CopyPixels(
        Color[] src,
        int srcW,
        int srcH,
        Color[] dest,
        int destW,
        int destX,
        int destY)
    {
        for (var row = 0; row < srcH; row++)
        {
            var srcOffset = row * srcW;
            var destOffset = (destY + row) * destW + destX;

            Array.Copy(
                src,
                srcOffset,
                dest,
                destOffset,
                srcW);
        }
    }

    private void FlushShelfPage(List<(PendingEntry Entry, int X, int Y)> entries, int width, int height)
    {
        if ((width <= 0) || (height <= 0))
            return;

        var pixels = new Color[width * height];
        var pageTexture = new Texture2D(Device, width, height);

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);

        try
        {
            var basePtr = handle.AddrOfPinnedObject();
            var pageStride = width * BYTES_PER_PIXEL;

            foreach ((var entry, var x, var y) in entries)
            {
                BlitEntry(
                    entry,
                    pixels,
                    width,
                    basePtr,
                    pageStride,
                    x,
                    y);

                var sourceRect = new Rectangle(
                    x,
                    y,
                    entry.Width,
                    entry.Height);
                var region = new AtlasRegion(pageTexture, sourceRect);

                if (entry.StringKey is not null)
                    BuildRegions[entry.StringKey] = region;

                if (entry.IntKey.HasValue)
                    BuildIntRegions[entry.IntKey.Value] = region;
            }
        } finally
        {
            handle.Free();
        }

        pageTexture.SetData(pixels);
        Pages.Add(pageTexture);
    }

    /// <summary>
    ///     Returns the atlas region for the given string key, or null if not found.
    /// </summary>
    public AtlasRegion? TryGetRegion(string key) => Regions.TryGetValue(key, out var region) ? region : null;

    /// <summary>
    ///     Returns the atlas region for the given integer key, or null if not found.
    /// </summary>
    public AtlasRegion? TryGetRegion(int key) => IntRegions.TryGetValue(key, out var region) ? region : null;


    private readonly record struct PendingEntry(
        string? StringKey,
        int? IntKey,
        Color[]? Pixels,
        SKImage? Image,
        int Width,
        int Height);
}