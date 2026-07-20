using System.Text;

namespace Brigid.UiPrimitivesReport;

/// <summary>Renders the artist-facing Markdown view of the authored-UI-primitive report.</summary>
internal static class MarkdownWriter
{
    public static string Write(Report r)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Brigid Authored UI Primitives — Reskin Reference");
        sb.AppendLine();
        sb.AppendLine($"_Generated {r.GeneratedUtc} (UTC) · {r.Stats.Panels} panels · " +
                      $"{r.Stats.AddressableRefs} addressable children · {r.Stats.SharedCustomControls} shared controls._");
        sb.AppendLine();
        sb.AppendLine("This is the inventory of UI panels/popups Brigid has authored and the named child controls a datf");
        sb.AppendLine("pack would target to reskin them. Regenerate with");
        sb.AppendLine("`dotnet run --project Tools/Brigid.UiPrimitivesReport`.");
        sb.AppendLine();
        sb.AppendLine("**Reference scheme.** " + r.RefScheme);
        sb.AppendLine();
        sb.AppendLine("**Legend.** `primitive` = base building block (UILabel/UIButton/UIImage/UITextBox/UIProgressBar/UIPanel); " +
                      "`shared` = a custom control reused by ≥2 panels (reskin once, applies everywhere — see the index below); " +
                      "`one-off` = a single-use/nested child. A ⬛ marks a child/panel that draws from a legacy `.dat` asset.");
        sb.AppendLine();

        WriteSharedIndex(sb, r);

        sb.AppendLine("## Authored panels / popups");
        sb.AppendLine();

        foreach (var p in r.Panels)
            WritePanel(sb, p);

        WriteLimitations(sb);

        return sb.ToString();
    }

    private static void WriteSharedIndex(StringBuilder sb, Report r)
    {
        var shared = r.SharedControls.Where(s => s.Shared).ToList();

        if (shared.Count == 0)
            return;

        sb.AppendLine("## Shared custom controls");
        sb.AppendLine();
        sb.AppendLine("Reusable controls the panels build on. Reskinning one of these covers every panel listed.");
        sb.AppendLine();
        sb.AppendLine("| Control | Used by | Source |");
        sb.AppendLine("|---|---|---|");

        foreach (var s in shared)
            sb.AppendLine($"| `{s.Type}` | {string.Join(", ", s.UsedBy)} | {Code(s.File)} |");

        sb.AppendLine();
    }

    private static void WritePanel(StringBuilder sb, PanelEntry p)
    {
        sb.AppendLine($"### `{p.Class}`{(p.DatBacked ? " ⬛" : "")}");
        sb.AppendLine();
        sb.AppendLine($"- **Ref base:** `{p.RefBase}`");
        sb.AppendLine($"- **Base:** `{p.Base}`");
        sb.AppendLine($"- **Source:** {Code(p.File)}");

        if (p.PrefabName is not null)
            sb.AppendLine($"- **Prefab control file:** `{p.PrefabName}` (legacy `.dat`-backed layout)");

        if (p.Assets.Count > 0)
            sb.AppendLine($"- **Legacy assets:** {string.Join(", ", p.Assets.Select(a => $"`{a}`"))}");

        if (!p.DatBacked)
            sb.AppendLine("- **Assets:** none — fully primitive-drawn");

        if (p.Summary is not null)
            sb.AppendLine($"- {p.Summary}");

        sb.AppendLine();

        if (p.Children.Count == 0)
            sb.AppendLine("_No individually addressable children._");
        else
        {
            sb.AppendLine("| Reskin ref | Type | Class | Notes |");
            sb.AppendLine("|---|---|---|---|");

            foreach (var c in p.Children)
                WriteChildRows(sb, c);
        }

        if (p.AnonymousChildren > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"_+{p.AnonymousChildren} inline unnamed child control(s) — not individually addressable._");
        }

        sb.AppendLine();
    }

    private static void WriteChildRows(StringBuilder sb, ChildRef c)
    {
        var repeated = c.Repeated ? (c.Count is { } n ? $"[{n}]" : "[]") : "";
        var notes = new List<string>();

        if (c.DatBacked && (c.PrefabName is not null))
            notes.Add($"⬛ prefab `{c.PrefabName}`");
        else if (c.DatBacked)
            notes.Add("⬛ .dat");

        if (c.Source == "prefab")
            notes.Add("prefab-only");

        if (c.Notes is not null)
            notes.Add(c.Notes);

        sb.AppendLine($"| `{c.Ref}` | `{c.Type}{repeated}` | {ClassLabel(c)} | {string.Join("; ", notes)} |");

        foreach (var nested in c.Children)
            WriteChildRows(sb, nested);
    }

    private static string ClassLabel(ChildRef c) => c.Classification switch
    {
        "shared" => "**shared**",
        "one-off" => "one-off",
        _ => "primitive"
    };

    private static void WriteLimitations(StringBuilder sb)
    {
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("### Scope & limitations");
        sb.AppendLine();
        sb.AppendLine("- Addressable children are those stored in a C# field/property or created by a prefab `CreateXxx(\"name\")`.");
        sb.AppendLine("  Inline unnamed children (built and added without a member) are counted but not individually addressable.");
        sb.AppendLine("- Array/list sizes resolve from integer literals and local `const int`s; cross-type consts stay unresolved.");
        sb.AppendLine("- Structural (syntax-only) analysis: a child's type is its declared type. Same-named nested classes in");
        sb.AppendLine("  different panels may not fully expand their sub-children.");
        sb.AppendLine();
    }

    private static string Code(string? s) => string.IsNullOrEmpty(s) ? "—" : $"`{s}`";
}
