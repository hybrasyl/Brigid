using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Brigid.UiPrimitivesReport;

/// <summary>Merged view of a (possibly partial, possibly nested) class declaration under Controls/.</summary>
internal sealed class ClassRecord(string name)
{
    public string Name { get; } = name;
    public List<ClassDeclarationSyntax> Nodes { get; } = [];
    public string FileRel { get; init; } = "";
    public bool InComponents { get; init; }
    public bool IsNested { get; init; }
    public string? ParentName { get; init; }
    public bool IsAbstract { get; set; }
    public string? BaseName { get; set; }
    public string? Summary { get; set; }
}

/// <summary>Resolves inheritance and primitive-ness across the parsed classes by walking simple-name base chains.</summary>
internal sealed class TypeResolver(Dictionary<string, ClassRecord> classes)
{
    private readonly Dictionary<(string, string), bool> DerivesCache = new();

    //base building blocks live in Controls/Components — a child typed as one of these is a "primitive".
    public bool IsPrimitive(string type) => classes.TryGetValue(type, out var rec) && rec.InComponents;

    public bool IsUiElement(string type) => (type == "UIElement") || DerivesFrom(type, "UIElement");

    public bool IsUiPanel(string type) => (type == "UIPanel") || DerivesFrom(type, "UIPanel");

    public bool DerivesFrom(string type, string target)
    {
        if (DerivesCache.TryGetValue((type, target), out var cached))
            return cached;

        DerivesCache[(type, target)] = false; //guard against cycles

        var result = false;
        var current = type;
        var guard = 0;

        while ((guard++ < 64) && classes.TryGetValue(current, out var rec) && (rec.BaseName is { } baseName))
        {
            if (baseName == target)
            {
                result = true;

                break;
            }

            current = baseName;
        }

        DerivesCache[(type, target)] = result;

        return result;
    }
}

/// <summary>Extracts simple type names from (possibly array/nullable/generic) type syntax.</summary>
internal static class TypeName
{
    public static string Simple(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax g => g.Identifier.Text,
        QualifiedNameSyntax q => Simple(q.Right),
        NullableTypeSyntax n => Simple(n.ElementType),
        ArrayTypeSyntax a => Simple(a.ElementType),
        _ => type.ToString()
    };

    private static readonly HashSet<string> Collections = new(StringComparer.Ordinal)
    {
        "List", "IReadOnlyList", "IList", "ICollection", "IEnumerable", "IReadOnlyCollection"
    };

    /// <summary>Returns the element control type and whether the declaration holds many (array or collection).</summary>
    public static (string Element, bool Repeated) Unwrap(TypeSyntax type)
    {
        switch (type)
        {
            case NullableTypeSyntax n:
                return Unwrap(n.ElementType);

            case ArrayTypeSyntax a:
                return (Simple(a.ElementType), true);

            case GenericNameSyntax g when Collections.Contains(g.Identifier.Text)
                                          && (g.TypeArgumentList.Arguments.Count == 1):
                return (Simple(g.TypeArgumentList.Arguments[0]), true);

            default:
                return (Simple(type), false);
        }
    }
}
