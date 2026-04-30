using DALib.Data;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using SkiaSharp;

const int ITEMS_PER_SHEET = 266;
const int DYE_SLOT_START = 98;
const int DYE_SLOT_END = 103; // inclusive

// canonical purple ramp at palette indices 98..103 — confirms a palette is "dye-shaped"
SKColor[] CanonicalPurpleRamp = [
    new(0xB3, 0x93, 0xC7),
    new(0x9B, 0x7B, 0xB7),
    new(0x8F, 0x5B, 0xA3),
    new(0x7F, 0x3B, 0x93),
    new(0x47, 0x23, 0x5F),
    new(0x37, 0x00, 0x5B),
];

bool RampMatchesCanonical(Palette p)
{
    for (var i = 0; i < CanonicalPurpleRamp.Length; i++)
    {
        var c = p[DYE_SLOT_START + i];
        var ref_ = CanonicalPurpleRamp[i];
        if (c.Red != ref_.Red || c.Green != ref_.Green || c.Blue != ref_.Blue)
            return false;
    }
    return true;
}

(int pixels, int opaque, int dyeSlot, int slotMask, int width, int height) FrameStats(EpfFrame frame)
{
    var width = frame.PixelWidth;
    var height = frame.PixelHeight;
    if (width <= 0 || height <= 0)
        return (0, 0, 0, 0, width, height);

    var pixels = width * height;
    var bound = Math.Min(pixels, frame.Data.Length);
    int opaque = 0, dyeSlot = 0, slotMask = 0;
    for (var i = 0; i < bound; i++)
    {
        var b = frame.Data[i];
        if (b == 0) continue; // index 0 = transparent per DALib SimpleRender
        opaque++;
        if (b >= DYE_SLOT_START && b <= DYE_SLOT_END)
        {
            dyeSlot++;
            slotMask |= 1 << (b - DYE_SLOT_START); // bits 0..5 indicate which of 98..103 are used
        }
    }
    return (pixels, opaque, dyeSlot, slotMask, width, height);
}

string RampSig(Palette p)
{
    var s = new System.Text.StringBuilder();
    for (var i = DYE_SLOT_START; i <= DYE_SLOT_END; i++)
        s.Append($"#{p[i].Red:X2}{p[i].Green:X2}{p[i].Blue:X2} ");
    return s.ToString().TrimEnd();
}

var positional = args.Where(a => !a.StartsWith("--")).ToArray();
var emitCsv = args.FirstOrDefault(a => a.StartsWith("--csv="))?.Substring("--csv=".Length);

var dataPath = positional.Length > 0
    ? positional[0]
    : Environment.GetEnvironmentVariable("DA_ASSET_PATH")
      ?? throw new ArgumentException("Pass data path as first arg, or set DA_ASSET_PATH.");

var legendPath = Path.Combine(dataPath, "legend.dat");
if (!File.Exists(legendPath))
    throw new FileNotFoundException($"legend.dat not found at {legendPath}");

var legend = DataArchive.FromFile(legendPath);
var lookup = PaletteLookup.FromArchive("itempal", "item", legend).Freeze();

Console.WriteLine($"Source: {legendPath}");
Console.WriteLine($"Dye-slot indices: {DYE_SLOT_START}..{DYE_SLOT_END}");
Console.WriteLine();

// Scan legacy item sheets (1..54). Unity rip extends to 61 with new items that have no legacy palette.
const int MAX_LEGACY_SHEET = 54;
var perItem = new List<(int Id, int Sheet, int Frame, int Palette, bool RampMatches, int W, int H, int Opaque, int DyeSlot, int SlotMask, bool Exists)>();
var debugSheet = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--detail-sheet="))?.Substring("--detail-sheet=".Length), out var ds) ? ds : -1;

for (var sheetNum = 1; sheetNum <= MAX_LEGACY_SHEET; sheetNum++)
{
    EpfView view;
    try
    {
        view = EpfView.FromArchive($"item{sheetNum:D3}", legend);
    }
    catch
    {
        continue; // sheet missing
    }

    for (var frameIdx = 0; frameIdx < ITEMS_PER_SHEET; frameIdx++)
    {
        var id = (sheetNum - 1) * ITEMS_PER_SHEET + frameIdx + 1;

        if (!view.TryGetValue(frameIdx, out var frame) || frame is null)
        {
            perItem.Add((id, sheetNum, frameIdx, -1, false, 0, 0, 0, 0, 0, false));
            continue;
        }

        Palette palette;
        int paletteNum;
        try
        {
            paletteNum = lookup.Table.GetPaletteNumber(id);
            palette = lookup.GetPaletteForId(id);
        }
        catch
        {
            perItem.Add((id, sheetNum, frameIdx, -1, false, 0, 0, 0, 0, 0, true));
            continue;
        }

        var rampOk = RampMatchesCanonical(palette);
        var (_, opaque, dyeSlot, slotMask, w, h) = FrameStats(frame);
        perItem.Add((id, sheetNum, frameIdx, paletteNum, rampOk, w, h, opaque, dyeSlot, slotMask, true));
    }
}

const int ALL_SIX_SLOTS = 0b111111;
// Summary
var existing = perItem.Where(x => x.Exists).ToArray();
var dyeableAny = existing.Where(x => x.RampMatches && x.DyeSlot > 0).ToArray();
var dyeableAllSix = existing.Where(x => x.RampMatches && x.SlotMask == ALL_SIX_SLOTS).ToArray();
var rampOnlyNotUsing = existing.Where(x => x.RampMatches && x.DyeSlot == 0).ToArray();
var nonDyeRamp = existing.Where(x => !x.RampMatches).ToArray();

Console.WriteLine($"Total item slots scanned: {perItem.Count}");
Console.WriteLine($"Items existing in EPFs:   {existing.Length}");
Console.WriteLine($"  Dyeable LOOSE (ramp + ANY of 98..103):    {dyeableAny.Length}");
Console.WriteLine($"  Dyeable STRICT (ramp + ALL SIX of 98..103): {dyeableAllSix.Length}");
Console.WriteLine($"  Has canonical ramp but no dye-slot pixels: {rampOnlyNotUsing.Length}");
Console.WriteLine($"  Non-canonical ramp (palette doesn't have purple): {nonDyeRamp.Length}");
Console.WriteLine();

// Group by palette: count dyeable / non-dyeable / no-pixels per palette
var byPalette = existing.GroupBy(x => x.Palette).OrderBy(g => g.Key);
Console.WriteLine($"Palette breakdown (palette → items, ramp signature):");
foreach (var group in byPalette)
{
    var sample = group.First();
    var palette = lookup.Palettes[sample.Palette];
    var ramp = RampSig(palette);
    var canonical = RampMatchesCanonical(palette) ? "PURPLE" : "      ";
    var loose = group.Count(x => x.RampMatches && x.DyeSlot > 0);
    var strict = group.Count(x => x.RampMatches && x.SlotMask == ALL_SIX_SLOTS);
    var noSlotCount = group.Count(x => x.RampMatches && x.DyeSlot == 0);
    var totalCount = group.Count();
    Console.WriteLine($"  pal#{group.Key,-3}  total={totalCount,5}  loose={loose,5}  strict-all-6={strict,5}  no-slots={noSlotCount,5}  [{canonical}]");
}
Console.WriteLine();

// Sheet-level summary
Console.WriteLine($"Per-sheet dyeable counts (LOOSE = any-slot, STRICT = all-six-slots):");
foreach (var sheetGroup in existing.GroupBy(x => x.Sheet).OrderBy(g => g.Key))
{
    var total = sheetGroup.Count();
    var loose = sheetGroup.Count(x => x.RampMatches && x.DyeSlot > 0);
    var strict = sheetGroup.Count(x => x.RampMatches && x.SlotMask == ALL_SIX_SLOTS);
    var pals = string.Join(",", sheetGroup.Select(x => x.Palette).Distinct().OrderBy(p => p));
    Console.WriteLine($"  sheet {sheetGroup.Key,2}: items={total,3}  loose={loose,3}  strict={strict,3}  palettes=[{pals}]");
}

// Cross-reference: how does each rule fare against a known-dyeable list provided as --known=path
var knownPath = args.FirstOrDefault(a => a.StartsWith("--known="))?.Substring("--known=".Length);
if (knownPath is not null && File.Exists(knownPath))
{
    var known = File.ReadAllLines(knownPath)
        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
        .Select(l => int.Parse(l.Trim()))
        .ToHashSet();

    var looseSet = dyeableAny.Select(x => x.Id).ToHashSet();
    var strictSet = dyeableAllSix.Select(x => x.Id).ToHashSet();

    var knownInLoose = known.Count(id => looseSet.Contains(id));
    var knownInStrict = known.Count(id => strictSet.Contains(id));
    var knownMissingLoose = known.Where(id => !looseSet.Contains(id)).OrderBy(x => x).ToArray();
    var knownMissingStrict = known.Where(id => !strictSet.Contains(id)).OrderBy(x => x).ToArray();
    var looseExtra = looseSet.Where(id => !known.Contains(id)).OrderBy(x => x).ToArray();
    var strictExtra = strictSet.Where(id => !known.Contains(id)).OrderBy(x => x).ToArray();

    Console.WriteLine($"\n--- Cross-reference vs known-dyeable list ({known.Count} items in {knownPath}) ---");
    Console.WriteLine($"LOOSE  rule:  matched={knownInLoose}/{known.Count}  missed={knownMissingLoose.Length}  extra={looseExtra.Length}");
    Console.WriteLine($"STRICT rule:  matched={knownInStrict}/{known.Count}  missed={knownMissingStrict.Length}  extra={strictExtra.Length}");

    if (knownMissingLoose.Length > 0)
    {
        Console.WriteLine($"\n  LOOSE-rule misses ({knownMissingLoose.Length}, known dyeable but no dye-slot pixels found):");
        foreach (var id in knownMissingLoose)
        {
            var item = perItem.FirstOrDefault(x => x.Id == id);
            if (item.Exists)
                Console.WriteLine($"    item{id:D5}  sheet={item.Sheet} pal=#{item.Palette} ramp={(item.RampMatches ? "OK" : "no")} {item.W}x{item.H} opaque={item.Opaque} dye={item.DyeSlot} slots={System.Numerics.BitOperations.PopCount((uint)item.SlotMask)}");
            else
                Console.WriteLine($"    item{id:D5}  <not in EPF>");
        }
    }
    if (knownMissingStrict.Length > 0 && knownMissingStrict.Length <= 80)
        Console.WriteLine($"\n  Strict-rule misses ({knownMissingStrict.Length}, known dyeable but doesn't use all 6 slots): {string.Join(", ", knownMissingStrict)}");
    if (strictExtra.Length > 0 && strictExtra.Length <= 30)
        Console.WriteLine($"\n  Strict-rule extras (rule says dyeable but not in user list): {string.Join(", ", strictExtra)}");
}

if (debugSheet > 0)
{
    Console.WriteLine();
    Console.WriteLine($"--- Sheet {debugSheet} per-item detail (sorted by dye-slot pixel %, descending) ---");
    var sheetItems = existing
        .Where(x => x.Sheet == debugSheet)
        .Select(x => new {
            x.Id,
            x.Frame,
            x.Palette,
            x.RampMatches,
            x.SlotMask,
            x.W,
            x.H,
            x.Opaque,
            x.DyeSlot,
            Pct = x.Opaque == 0 ? 0.0 : 100.0 * x.DyeSlot / x.Opaque
        })
        .OrderByDescending(x => x.Pct)
        .ThenByDescending(x => x.DyeSlot)
        .ToArray();
    Console.WriteLine($"  {"id",-7} {"frame",-5} {"pal",-3} {"ramp",-5} {"WxH",-9} {"opaque",-7} {"dye",-5} {"slots/6",-7} {"strict",-6} {"dye%",-6}");
    foreach (var x in sheetItems)
    {
        var distinct = System.Numerics.BitOperations.PopCount((uint)x.SlotMask);
        var strict = x.SlotMask == ALL_SIX_SLOTS ? "YES" : "no ";
        Console.WriteLine($"  {x.Id,-7} {x.Frame,-5} {x.Palette,-3} {(x.RampMatches ? "OK" : "no"),-5} {x.W}x{x.H,-7} {x.Opaque,-7} {x.DyeSlot,-5} {distinct,-7} {strict,-6} {x.Pct,5:F2}%");
    }
}

if (emitCsv is not null)
{
    using var writer = new StreamWriter(emitCsv);
    writer.WriteLine("item_id,sheet,frame_index,palette_number,palette_has_purple_ramp,opaque_pixels,dye_slot_pixels,dye_slot_pct,slot_mask,distinct_slots,dyeable_loose,dyeable_strict_all_six");
    foreach (var x in perItem.Where(x => x.Exists))
    {
        var loose = x.RampMatches && x.DyeSlot > 0;
        var strict = x.RampMatches && x.SlotMask == ALL_SIX_SLOTS;
        var pct = x.Opaque == 0 ? 0.0 : 100.0 * x.DyeSlot / x.Opaque;
        var distinctSlots = System.Numerics.BitOperations.PopCount((uint)x.SlotMask);
        writer.WriteLine($"{x.Id},{x.Sheet},{x.Frame},{x.Palette},{x.RampMatches},{x.Opaque},{x.DyeSlot},{pct:F2},{x.SlotMask},{distinctSlots},{loose},{strict}");
    }
    Console.WriteLine($"\nWrote {existing.Length} rows to {emitCsv}");
}
