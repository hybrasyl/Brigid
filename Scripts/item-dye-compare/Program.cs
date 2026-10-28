#region
using System.Diagnostics;
using System.Globalization;
using Brigid.Data;
using Brigid.Data.AssetPacks;
using Brigid.Rendering;
using Chaos.DarkAges.Definitions;
using DALib.Drawing;
using SkiaSharp;
#endregion

//BRIG-41 pilot gates. For each item PNG in a folder (for example the Unity rip), render the item through the legacy
//EPF + palette dye and through the pack dye pass, for several dye colors, and report the pixels that differ. An item
//whose undyed images already differ has different source art; only differences that the dye adds are dye-pipeline
//errors. The tool also times a cold decode, detect and dye of an inventory of dyeable items.
//
//usage: dotnet run --project Scripts/item-dye-compare -- <game data folder> <png folder> [--colors 1,5,30] [--first N]

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: ItemDyeCompare <game data folder> <png folder> [--colors 1,5,30] [--first N]");

    return 2;
}

var dataPath = args[0];
var pngFolder = args[1];
var colors = new byte[] { 1, 5, 30 };
var first = int.MaxValue;

for (var i = 2; i < args.Length - 1; i++)
    switch (args[i])
    {
        case "--colors":
            colors = args[++i].Split(',').Select(c => byte.Parse(c, CultureInfo.InvariantCulture)).ToArray();

            break;
        case "--first":
            first = int.Parse(args[++i], CultureInfo.InvariantCulture);

            break;
    }

//the legacy tables are EUC-KR text, as in the client
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

//the registry and the ramp check write through the data-layer log seam; show those lines
DataDiagnostics.Log = Console.Error.WriteLine;
DataContext.Initialize(741, dataPath);

var table = DataContext.AislingDrawData.DyeColorTable;
Console.WriteLine($"# Item dye comparison{Environment.NewLine}");
Console.WriteLine($"Data: `{dataPath}`. PNGs: `{pngFolder}`. Colors: {string.Join(", ", colors)}.{Environment.NewLine}");

foreach (var line in ItemDyeRamp.DifferencesFrom(table[0].Colors))
    Console.WriteLine($"- ramp check: {line}");

var files = Directory.EnumerateFiles(pngFolder, "item*.png")
                     .Select(path => (path, ok: int.TryParse(Path.GetFileNameWithoutExtension(path)[4..], NumberStyles.None, CultureInfo.InvariantCulture, out var id), id))
                     .Where(f => f.ok && (f.id > 0) && (f.id < ItemSpriteIds.MODERN_FIRST))
                     .OrderBy(f => f.id)
                     .Take(first)
                     .ToList();

var counts = new Dictionary<string, int>();
var dyeErrorItems = new List<string>();
var ruleDisagreements = new List<string>();
var legacyOnly = new List<string>();
var dyeableIds = new List<(int Id, string Path)>();

void Count(string key) => counts[key] = counts.GetValueOrDefault(key) + 1;

foreach ((var path, _, var id) in files)
{
    using var png = SKImage.FromEncodedData(path);

    if (png is null)
    {
        Count("png does not decode");

        continue;
    }

    var packDyeable = ItemDyeRamp.ContainsRampPixel(png);
    var legacy = DataContext.PanelSprites.GetItemSprite(id);

    if (legacy is null)
    {
        Count("no legacy sprite");

        continue;
    }

    var legacyDyeable = DataContext.AislingDrawData.IsDyeable(legacy.Palette);

    //the legacy rule needs a dye-shaped palette and sprite pixels in the dye slots; the palette half is checked here
    if (packDyeable && !legacyDyeable)
        ruleDisagreements.Add($"{id}: the PNG has ramp pixels, the legacy palette has no dye ramp");

    if (!packDyeable)
    {
        Count("not dyeable (no ramp pixel)");

        //the other direction: the legacy client dyes the item, but the PNG has no ramp pixel, so a pack leaves it undyed
        if (legacyDyeable && table.Contains(colors[0]))
        {
            using var undyed = Graphics.RenderImage(legacy.Entity, legacy.Palette);
            using var dyed = Graphics.RenderImage(legacy.Entity, DataContext.AislingDrawData.ApplyDye(legacy.Palette, (DisplayColor)colors[0]));

            if (Diff(undyed, dyed, out _).Count > 0)
                legacyOnly.Add($"{id}: the legacy client dyes it, the PNG has no ramp pixel");
        }

        continue;
    }

    dyeableIds.Add((id, path));
    Count("dyeable");

    using var legacyUndyed = Graphics.RenderImage(legacy.Entity, legacy.Palette);
    var artDiff = Diff(png, legacyUndyed, out var sizeNote);

    if (sizeNote is not null)
    {
        Count("art size differs from legacy");

        continue;
    }

    if (artDiff.Count > 0)
        Count("source art differs from legacy");

    var dyeErrors = 0;

    foreach (var color in colors)
    {
        if (!table.Contains(color))
            continue;

        using var packDyed = ItemDyePass.ApplyDyeFindReplace(png, table[color]);
        using var legacyDyed = Graphics.RenderImage(legacy.Entity, DataContext.AislingDrawData.ApplyDye(legacy.Palette, (DisplayColor)color));
        var dyedDiff = Diff(packDyed, legacyDyed, out _);
        dyedDiff.ExceptWith(artDiff);
        dyeErrors += dyedDiff.Count;
    }

    if (dyeErrors > 0)
    {
        Count("dye adds differences");
        dyeErrorItems.Add($"{id}: {dyeErrors} pixel(s) over {colors.Length} color(s)");
    }
    else
        Count("dye matches legacy");
}

Console.WriteLine($"{Environment.NewLine}## Counts ({files.Count} PNGs below sprite {ItemSpriteIds.MODERN_FIRST}){Environment.NewLine}");

foreach ((var key, var value) in counts.OrderBy(kv => kv.Key))
    Console.WriteLine($"- {key}: {value}");

Console.WriteLine($"{Environment.NewLine}## Dye-pipeline differences{Environment.NewLine}");
Console.WriteLine(dyeErrorItems.Count == 0 ? "None." : string.Join(Environment.NewLine, dyeErrorItems.Take(50).Select(l => $"- {l}")));

Console.WriteLine($"{Environment.NewLine}## Rule disagreements: the PNG has ramp pixels, legacy does not dye{Environment.NewLine}");
Console.WriteLine(ruleDisagreements.Count == 0 ? "None." : string.Join(Environment.NewLine, ruleDisagreements.Take(50).Select(l => $"- {l}")));

Console.WriteLine($"{Environment.NewLine}## Rule disagreements: legacy dyes, the PNG has no ramp pixel{Environment.NewLine}");
Console.WriteLine(legacyOnly.Count == 0 ? "None." : string.Join(Environment.NewLine, legacyOnly.Take(50).Select(l => $"- {l}")));

//cold path of one inventory of dyed icons: decode, detect, dye. the GPU upload is not in this number
const int INVENTORY = 60;
var sample = dyeableIds.Take(INVENTORY).ToList();

if (sample.Count > 0)
{
    var bytes = sample.Select(s => File.ReadAllBytes(s.Path)).ToList();
    var watch = Stopwatch.StartNew();

    for (var i = 0; i < bytes.Count; i++)
    {
        using var image = SKImage.FromEncodedData(bytes[i]);
        _ = ItemDyeRamp.ContainsRampPixel(image);
        using var dyed = ItemDyePass.ApplyDyeFindReplace(image, table[colors[i % colors.Length]]);
    }

    watch.Stop();
    Console.WriteLine($"{Environment.NewLine}## Cold decode + detect + dye{Environment.NewLine}");
    Console.WriteLine($"{sample.Count} dyeable icons: {watch.Elapsed.TotalMilliseconds:F1} ms in total, {watch.Elapsed.TotalMilliseconds / sample.Count:F3} ms per icon.");
}

return 0;

//compares the opaque content of two images, each cropped to its visible bounds. gives the positions that differ, or
//a size note when the cropped sizes differ (then the art is not the same art, and no position is compared)
static HashSet<(int, int)> Diff(SKImage a, SKImage b, out string? sizeNote)
{
    using var ba = SKBitmap.FromImage(a);
    using var bb = SKBitmap.FromImage(b);
    var ra = VisibleBounds(ba);
    var rb = VisibleBounds(bb);
    var result = new HashSet<(int, int)>();
    sizeNote = null;

    if ((ra.Width != rb.Width) || (ra.Height != rb.Height))
    {
        sizeNote = $"{ra.Width}x{ra.Height} vs {rb.Width}x{rb.Height}";

        return result;
    }

    for (var y = 0; y < ra.Height; y++)
        for (var x = 0; x < ra.Width; x++)
        {
            var pa = ba.GetPixel(ra.Left + x, ra.Top + y);
            var pb = bb.GetPixel(rb.Left + x, rb.Top + y);
            var visibleA = pa.Alpha > 0;
            var visibleB = pb.Alpha > 0;

            if ((visibleA != visibleB) || (visibleA && ((pa.Red != pb.Red) || (pa.Green != pb.Green) || (pa.Blue != pb.Blue))))
                result.Add((x, y));
        }

    return result;
}

static SKRectI VisibleBounds(SKBitmap bitmap)
{
    int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;

    for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
            if (bitmap.GetPixel(x, y).Alpha > 0)
            {
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }

    return right < 0 ? SKRectI.Empty : new SKRectI(left, top, right + 1, bottom + 1);
}
