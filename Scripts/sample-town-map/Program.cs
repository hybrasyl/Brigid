using DALib.Data;
using DALib.Drawing;
using SkiaSharp;

const int CANVAS_W = 520;
const int CANVAS_H = 260;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: sample-town-map <data-path> [mapId[xWIDTHxHEIGHT] ...]");
    Console.Error.WriteLine("  ex: sample-town-map E:/Games/DarkAges 500 502x91x60 3080");
    Console.Error.WriteLine("  defaults to: 1 500 3080 if none given");
    return 1;
}

var dataPath = args[0];
var mapSpecs = args.Length > 1
    ? args[1..]
    : new[] { "1", "500", "3080" };

var seoPath = Path.Combine(dataPath, "seo.dat");
var iaPath = Path.Combine(dataPath, "ia.dat");
var mapsDir = Path.Combine(dataPath, "maps");
var outputDir = Path.Combine(AppContext.BaseDirectory, "output");
Directory.CreateDirectory(outputDir);

Console.WriteLine($"data-path: {dataPath}");
Console.WriteLine($"seo:       {seoPath}");
Console.WriteLine($"ia:        {iaPath}");
Console.WriteLine($"maps:      {mapsDir}");
Console.WriteLine($"output:    {outputDir}");
Console.WriteLine();

using var seoDat = DataArchive.FromFile(seoPath);
using var iaDat = DataArchive.FromFile(iaPath);

foreach (var spec in mapSpecs)
{
    // spec is either "<mapId>" (assume square) or "<mapId>x<W>x<H>"
    var parts = spec.Split('x');
    var mapId = int.Parse(parts[0]);
    int width, height;

    var mapPath = Path.Combine(mapsDir, $"lod{mapId}.map");
    if (!File.Exists(mapPath))
    {
        Console.WriteLine($"[skip] lod{mapId}.map not found");
        continue;
    }

    var fileBytes = new FileInfo(mapPath).Length;
    var totalTiles = fileBytes / 6;

    if (parts.Length == 3)
    {
        width = int.Parse(parts[1]);
        height = int.Parse(parts[2]);
        if (width * height != totalTiles)
        {
            Console.WriteLine($"[skip] lod{mapId}.map: explicit {width}x{height}={width * height} != file tiles {totalTiles}");
            continue;
        }
    }
    else
    {
        var side = (int)Math.Sqrt(totalTiles);
        if (side * side != totalTiles)
        {
            Console.WriteLine($"[skip] lod{mapId}.map: non-square ({totalTiles} tiles); pass {mapId}xWxH");
            continue;
        }
        width = side;
        height = side;
    }

    Console.WriteLine($"[render] lod{mapId}.map  {width}x{height} ({totalTiles} tiles)");

    var map = MapFile.FromFile(mapPath, width, height);

    var sw = System.Diagnostics.Stopwatch.StartNew();
    using var nativeImage = Graphics.RenderMap(map, seoDat, iaDat, foregroundPadding: 64);
    sw.Stop();
    Console.WriteLine($"           native: {nativeImage.Width}x{nativeImage.Height}  ({sw.ElapsedMilliseconds}ms)");

    // fit to canvas while preserving aspect
    var scale = Math.Min((float)CANVAS_W / nativeImage.Width, (float)CANVAS_H / nativeImage.Height);
    var fitW = Math.Max(1, (int)(nativeImage.Width * scale));
    var fitH = Math.Max(1, (int)(nativeImage.Height * scale));

    using var canvas = new SKBitmap(CANVAS_W, CANVAS_H, SKColorType.Rgba8888, SKAlphaType.Premul);
    using (var skcanvas = new SKCanvas(canvas))
    {
        skcanvas.Clear(SKColors.Transparent);
        var destRect = new SKRect(
            (CANVAS_W - fitW) / 2f,
            (CANVAS_H - fitH) / 2f,
            (CANVAS_W - fitW) / 2f + fitW,
            (CANVAS_H - fitH) / 2f + fitH);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        skcanvas.DrawImage(nativeImage, destRect, paint);
    }

    var outPath = Path.Combine(outputDir, $"town_{mapId:D5}.png");
    using (var img = SKImage.FromBitmap(canvas))
    using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
    using (var fs = File.Create(outPath))
        data.SaveTo(fs);

    Console.WriteLine($"           wrote:  {outPath}");

    // also dump the native (un-scaled) image alongside, for inspection
    var nativePath = Path.Combine(outputDir, $"town_{mapId:D5}_native.png");
    using (var data = nativeImage.Encode(SKEncodedImageFormat.Png, 100))
    using (var fs = File.Create(nativePath))
        data.SaveTo(fs);
    Console.WriteLine($"           native: {nativePath}");
}

return 0;
