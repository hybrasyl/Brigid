using System.Text;
using System.Text.Json;
using Brigid.UiPrimitivesReport;

// Repeatable inventory of Brigid's authored UI primitives — the panels/popups the team built and, for each,
// the named child controls a datf pack would address for reskinning (e.g. "bankshoppanel.titlelabel").
// Pure source analysis via Roslyn: no game data, no MonoGame — point it at the repo and run.
//
// Usage: dotnet run --project Tools/Brigid.UiPrimitivesReport -- [--repo-root <dir>] [--out-dir <dir>]

var repoRoot = ArgValue(args, "--repo-root") ?? RepoLocator.Discover();

if (repoRoot is null)
{
    Console.Error.WriteLine("Could not locate the repo root (no Brigid.slnx found walking up). Pass --repo-root <dir>.");
    return 1;
}

var controlsDir = Path.Combine(repoRoot, "Brigid", "Controls");

if (!Directory.Exists(controlsDir))
{
    Console.Error.WriteLine($"Controls directory not found: {controlsDir}");
    return 1;
}

var outDir = ArgValue(args, "--out-dir") ?? Path.Combine(repoRoot, "Scripts", "output");
Directory.CreateDirectory(outDir);

var report = UiModelBuilder.Build(repoRoot, controlsDir);

var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
var baseName = $"primitivesdump{stamp}";
var jsonPath = Path.Combine(outDir, baseName + ".json");
var mdPath = Path.Combine(outDir, baseName + ".md");

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, jsonOptions), new UTF8Encoding(false));
File.WriteAllText(mdPath, MarkdownWriter.Write(report), new UTF8Encoding(false));

Console.WriteLine("Authored UI primitives report:");
Console.WriteLine($"  panels/popups:    {report.Stats.Panels}");
Console.WriteLine($"  addressable refs: {report.Stats.AddressableRefs}");
Console.WriteLine($"  shared controls:  {report.Stats.SharedCustomControls} (of {report.SharedControls.Count} custom child types)");
Console.WriteLine($"  -> {mdPath}");
Console.WriteLine($"  -> {jsonPath}");

return 0;

static string? ArgValue(string[] args, string name)
{
    var i = Array.IndexOf(args, name);

    return (i >= 0) && (i + 1 < args.Length) ? args[i + 1] : null;
}
