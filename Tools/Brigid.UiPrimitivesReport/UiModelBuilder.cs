using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Brigid.UiPrimitivesReport;

/// <summary>
///     Roslyn syntax analysis of <c>Brigid/Controls</c>. Discovers the authored panels/popups (non-primitive classes
///     that derive from <c>UIPanel</c>) and, for each, the named child controls a datf pack would address for a
///     reskin. Purely structural — no semantic model, no compilation — so it needs neither game data nor a build.
/// </summary>
internal static class UiModelBuilder
{
    private const string RefScheme =
        "Reskin reference = {panelclass}.{child}, lowercased (e.g. bankshoppanel.titlelabel). `child` is the C# " +
        "field/property the control is stored in, or the legacy control-file name for prefab children. Nested " +
        "children extend the path (bankshoppanel.rows.iconimage). Array/list children are marked repeated.";

    //the CreateXxx prefab factories on PrefabPanel and the control type each yields.
    private static readonly Dictionary<string, string> PrefabFactories = new(StringComparer.Ordinal)
    {
        ["CreateButton"] = "UIButton",
        ["CreateImage"] = "UIImage",
        ["CreateLabel"] = "UILabel",
        ["CreateTextBox"] = "UITextBox",
        ["CreateProgressBar"] = "UIProgressBar"
    };

    //override-aware / legacy-asset load calls whose first string arg names a .dat sprite the control draws.
    private static readonly HashSet<string> AssetLoadCalls = new(StringComparer.Ordinal)
    {
        "GetEpfTexture", "GetSpfTexture", "GetPrefabTexture", "GetEpfImages", "GetSpfImage"
    };

    public static Report Build(string repoRoot, string controlsDir)
    {
        var classes = ParseClasses(repoRoot, controlsDir);
        var resolver = new TypeResolver(classes);
        var consts = BuildConstInts(classes);

        //the authored panels/popups: concrete, non-primitive classes that derive from UIPanel, declared top-level.
        var panelRecords = classes.Values
                                   .Where(c => !c.InComponents && !c.IsNested && !c.IsAbstract && resolver.IsUiPanel(c.Name))
                                   .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                                   .ToList();

        var panels = panelRecords.Select(rec => BuildPanel(rec, classes, resolver, consts)).ToList();

        //resolve custom (non-primitive) child types to shared (used by >= 2 panels) vs one-off, and index them.
        var usage = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var panel in panels)
            foreach (var type in CollectCustomChildTypes(panel.Children, resolver))
                (usage.TryGetValue(type, out var set) ? set : usage[type] = new SortedSet<string>(StringComparer.Ordinal))
                    .Add(panel.Class);

        var finalized = panels.Select(p => p with { Children = ClassifyCustom(p.Children, usage) }).ToList();

        var shared = usage.Select(kv => new SharedControl(
                              kv.Key,
                              kv.Value.Count >= 2,
                              classes.TryGetValue(kv.Key, out var rec) ? rec.FileRel : null,
                              kv.Value.ToList()))
                          .OrderByDescending(s => s.UsedBy.Count)
                          .ThenBy(s => s.Type, StringComparer.OrdinalIgnoreCase)
                          .ToList();

        var stats = new ReportStats(
            finalized.Count,
            shared.Count(s => s.Shared),
            finalized.Sum(p => CountRefs(p.Children)),
            finalized.Count(p => p.DatBacked));

        return new Report(
            DateTime.UtcNow.ToString("O"),
            repoRoot.Replace('\\', '/'),
            RefScheme,
            stats,
            finalized,
            shared);
    }

    // ── parsing ──────────────────────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, ClassRecord> ParseClasses(string repoRoot, string controlsDir)
    {
        var map = new Dictionary<string, ClassRecord>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(controlsDir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var root = CSharpSyntaxTree.ParseText(text).GetRoot();
            var rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            var inComponents = rel.Contains("/Controls/Components/", StringComparison.OrdinalIgnoreCase);

            foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var name = cls.Identifier.Text;

                if (!map.TryGetValue(name, out var rec))
                    map[name] = rec = new ClassRecord(name)
                    {
                        FileRel = rel,
                        InComponents = inComponents,
                        IsNested = cls.Parent is ClassDeclarationSyntax,
                        ParentName = (cls.Parent as ClassDeclarationSyntax)?.Identifier.Text
                    };

                rec.Nodes.Add(cls);
                rec.IsAbstract |= cls.Modifiers.Any(SyntaxKind.AbstractKeyword);
                rec.BaseName ??= FirstBaseName(cls);
                rec.Summary ??= SummaryOf(cls);
            }
        }

        return map;
    }

    private static string? FirstBaseName(ClassDeclarationSyntax cls)
    {
        var first = cls.BaseList?.Types.FirstOrDefault()?.Type;

        return first is null ? null : TypeName.Simple(first);
    }

    private static string? SummaryOf(ClassDeclarationSyntax cls)
    {
        foreach (var trivia in cls.GetLeadingTrivia())
        {
            if (trivia.GetStructure() is not DocumentationCommentTriviaSyntax doc)
                continue;

            var text = string.Concat(doc.DescendantTokens()
                                        .Where(t => t.IsKind(SyntaxKind.XmlTextLiteralToken))
                                        .Select(t => t.Text));
            var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            if (!string.IsNullOrWhiteSpace(collapsed))
                return collapsed.Length > 240 ? collapsed[..240] + "…" : collapsed;
        }

        return null;
    }

    // ── per-panel extraction ────────────────────────────────────────────────────────────────────────────────

    private static PanelEntry BuildPanel(
        ClassRecord rec,
        Dictionary<string, ClassRecord> classes,
        TypeResolver resolver,
        Dictionary<string, int> consts)
    {
        var refBase = rec.Name.ToLowerInvariant();
        var (children, anon) = ExtractChildren(rec, refBase, classes, resolver, consts);

        //panel-level .dat backing: a PrefabPanel loads a control file (base("_x")); any control may load sprites directly.
        var prefabName = PrefabBaseArg(rec);
        var assets = AssetLiterals(rec);
        var datBacked = resolver.DerivesFrom(rec.Name, "PrefabPanel") || (prefabName is not null) || (assets.Count > 0)
                        || children.Any(c => c.DatBacked);

        return new PanelEntry(
            rec.Name,
            refBase,
            rec.BaseName ?? "UIElement",
            rec.FileRel,
            rec.Summary,
            datBacked,
            prefabName,
            assets,
            anon,
            children);
    }

    private static (List<ChildRef> Children, int Anonymous) ExtractChildren(
        ClassRecord rec,
        string refPrefix,
        Dictionary<string, ClassRecord> classes,
        TypeResolver resolver,
        Dictionary<string, int> consts)
    {
        var children = new List<ChildRef>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        //1) field/property-backed children: the clean, addressable set (member name -> ref token).
        foreach (var (member, typeSyntax) in MemberDeclarations(rec))
        {
            var (elem, repeated) = TypeName.Unwrap(typeSyntax);

            if (!resolver.IsUiElement(elem) || !seen.Add(member))
                continue;

            var prefab = PrefabCreateArgFor(rec, member);
            var count = repeated ? ArraySizeFor(rec, member, consts) : null;
            var classification = ClassifyType(elem, rec.Name, classes, resolver);

            //recurse into a nested child class defined inside THIS panel (rows -> iconimage/namelabel).
            IReadOnlyList<ChildRef> nested = [];

            if (classes.TryGetValue(elem, out var elemRec) && elemRec.IsNested && (elemRec.ParentName == rec.Name))
                (nested, _) = ExtractChildren(elemRec, $"{refPrefix}.{member.ToLowerInvariant()}", classes, resolver, consts);

            children.Add(new ChildRef(
                $"{refPrefix}.{member.ToLowerInvariant()}",
                member,
                elem,
                classification,
                "field",
                repeated,
                count,
                prefab,
                prefab is not null,
                repeated && count is null ? "array size not statically resolved" : null,
                nested));
        }

        //2) prefab CreateXxx("name") children NOT already linked to a field child above (addressed by control-file name).
        var linkedPrefabNames = children.Where(c => c.PrefabName is not null)
                                        .Select(c => c.PrefabName!)
                                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (method, name) in PrefabCreateCalls(rec))
        {
            if (linkedPrefabNames.Contains(name) || !seen.Add(name))
                continue;

            children.Add(new ChildRef(
                $"{refPrefix}.{name.ToLowerInvariant()}",
                name,
                PrefabFactories[method],
                "primitive",
                "prefab",
                false,
                null,
                name,
                true,
                "prefab control-file child (not stored in a field)",
                []));
        }

        //3) count inline unnamed UI children (new <uiType> not tied to a tracked member) — not individually addressable.
        var anon = rec.Nodes.SelectMany(n => n.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                            .Count(oc => resolver.IsUiElement(TypeName.Simple(oc.Type))
                                         && oc.Parent is not (AssignmentExpressionSyntax or EqualsValueClauseSyntax));

        return (children, anon);
    }

    private static string ClassifyType(
        string type,
        string ownerPanel,
        Dictionary<string, ClassRecord> classes,
        TypeResolver resolver)
    {
        if (resolver.IsPrimitive(type))
            return "primitive";

        //nested inside this panel, or defined but used by only this panel, resolves to shared/one-off in the second pass.
        if (classes.TryGetValue(type, out var rec) && rec.IsNested && (rec.ParentName == ownerPanel))
            return "one-off";

        return "custom"; //placeholder — ClassifyCustom promotes to shared/one-off from cross-panel usage
    }

    // ── shared-usage second pass ────────────────────────────────────────────────────────────────────────────

    private static IEnumerable<string> CollectCustomChildTypes(IReadOnlyList<ChildRef> children, TypeResolver resolver)
    {
        foreach (var c in children)
        {
            if (c.Classification == "custom" && !resolver.IsPrimitive(c.Type))
                yield return c.Type;

            foreach (var nested in CollectCustomChildTypes(c.Children, resolver))
                yield return nested;
        }
    }

    private static IReadOnlyList<ChildRef> ClassifyCustom(
        IReadOnlyList<ChildRef> children,
        Dictionary<string, SortedSet<string>> usage)
        => children.Select(c => c with
        {
            Classification = c.Classification == "custom"
                ? (usage.TryGetValue(c.Type, out var set) && (set.Count >= 2) ? "shared" : "one-off")
                : c.Classification,
            Children = ClassifyCustom(c.Children, usage)
        }).ToList();

    private static int CountRefs(IReadOnlyList<ChildRef> children)
        => children.Sum(c => 1 + CountRefs(c.Children));

    // ── syntax helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static IEnumerable<(string Member, TypeSyntax Type)> MemberDeclarations(ClassRecord rec)
    {
        foreach (var node in rec.Nodes)
            foreach (var member in node.Members)
                switch (member)
                {
                    case FieldDeclarationSyntax f:
                        foreach (var v in f.Declaration.Variables)
                            yield return (v.Identifier.Text, f.Declaration.Type);

                        break;

                    case PropertyDeclarationSyntax p:
                        yield return (p.Identifier.Text, p.Type);

                        break;
                }
    }

    //the prefab control-file name a field was assigned from: `Member = CreateXxx("name")`.
    private static string? PrefabCreateArgFor(ClassRecord rec, string member)
    {
        foreach (var assign in rec.Nodes.SelectMany(n => n.DescendantNodes().OfType<AssignmentExpressionSyntax>()))
            if ((assign.Left is IdentifierNameSyntax id) && (id.Identifier.Text == member)
                                                         && (assign.Right is InvocationExpressionSyntax inv)
                                                         && IsPrefabFactory(inv, out _, out var name))
                return name;

        //also the field-initializer form: `Member = CreateXxx("name");` in a declarator.
        foreach (var decl in rec.Nodes.SelectMany(n => n.DescendantNodes().OfType<VariableDeclaratorSyntax>()))
            if ((decl.Identifier.Text == member) && (decl.Initializer?.Value is InvocationExpressionSyntax inv)
                                                 && IsPrefabFactory(inv, out _, out var name))
                return name;

        return null;
    }

    private static IEnumerable<(string Method, string Name)> PrefabCreateCalls(ClassRecord rec)
    {
        foreach (var inv in rec.Nodes.SelectMany(n => n.DescendantNodes().OfType<InvocationExpressionSyntax>()))
            if (IsPrefabFactory(inv, out var method, out var name))
                yield return (method, name);
    }

    private static bool IsPrefabFactory(InvocationExpressionSyntax inv, out string method, out string name)
    {
        method = MethodName(inv);
        name = "";

        if (!PrefabFactories.ContainsKey(method))
            return false;

        if (inv.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax { } lit
            && lit.IsKind(SyntaxKind.StringLiteralExpression))
        {
            name = lit.Token.ValueText;

            return name.Length > 0;
        }

        return false;
    }

    //array size for a repeated member: `Member = new T[N]` or field initializer `= new T[N]`, N literal or a const int.
    private static int? ArraySizeFor(ClassRecord rec, string member, Dictionary<string, int> consts)
    {
        foreach (var arr in rec.Nodes.SelectMany(n => n.DescendantNodes().OfType<ArrayCreationExpressionSyntax>()))
        {
            var assignedTo = arr.Parent switch
            {
                AssignmentExpressionSyntax { Left: IdentifierNameSyntax aid } => aid.Identifier.Text,
                EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax vd } => vd.Identifier.Text,
                _ => null
            };

            if (assignedTo != member)
                continue;

            switch (arr.Type.RankSpecifiers.FirstOrDefault()?.Sizes.FirstOrDefault())
            {
                case LiteralExpressionSyntax { Token.Value: int n }:
                    return n;

                case IdentifierNameSyntax id when consts.TryGetValue(id.Identifier.Text, out var cv):
                    return cv;
            }
        }

        return null;
    }

    //const int values declared anywhere under Controls/, resolved through one-level identifier chains
    //(e.g. MAX_VISIBLE_TABS = VISIBLE_ROWS = 8). Cross-type refs (UserOptions.SETTING_COUNT) stay unresolved.
    private static Dictionary<string, int> BuildConstInts(Dictionary<string, ClassRecord> classes)
    {
        var raw = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);

        foreach (var f in classes.Values.SelectMany(c => c.Nodes).SelectMany(n => n.Members.OfType<FieldDeclarationSyntax>()))
        {
            if (!f.Modifiers.Any(SyntaxKind.ConstKeyword) || (f.Declaration.Type is not PredefinedTypeSyntax p)
                                                          || !p.Keyword.IsKind(SyntaxKind.IntKeyword))
                continue;

            foreach (var v in f.Declaration.Variables)
                if (v.Initializer?.Value is { } value)
                    raw[v.Identifier.Text] = value;
        }

        var resolved = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var pass = 0; pass < 4; pass++)
            foreach (var (name, expr) in raw)
            {
                if (resolved.ContainsKey(name))
                    continue;

                if (expr is LiteralExpressionSyntax { Token.Value: int n })
                    resolved[name] = n;
                else if (expr is IdentifierNameSyntax id && resolved.TryGetValue(id.Identifier.Text, out var cv))
                    resolved[name] = cv;
            }

        return resolved;
    }

    private static string? PrefabBaseArg(ClassRecord rec)
    {
        foreach (var ctor in rec.Nodes.SelectMany(n => n.Members.OfType<ConstructorDeclarationSyntax>()))
        {
            var arg = ctor.Initializer?.ArgumentList.Arguments.FirstOrDefault()?.Expression;

            if (arg is LiteralExpressionSyntax { } lit && lit.IsKind(SyntaxKind.StringLiteralExpression)
                                                       && lit.Token.ValueText.StartsWith('_'))
                return lit.Token.ValueText;
        }

        return null;
    }

    private static IReadOnlyList<string> AssetLiterals(ClassRecord rec)
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var inv in rec.Nodes.SelectMany(n => n.DescendantNodes().OfType<InvocationExpressionSyntax>()))
            if (AssetLoadCalls.Contains(MethodName(inv))
                && inv.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax { } lit
                && lit.IsKind(SyntaxKind.StringLiteralExpression))
                found.Add(lit.Token.ValueText);

        return found.ToList();
    }

    private static string MethodName(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax g => g.Identifier.Text,
        _ => ""
    };
}
