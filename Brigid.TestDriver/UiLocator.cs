#region
using Brigid.Controls.Components;
#endregion

namespace Brigid.TestDriver;

/// <summary>
///     Finds a control by its path of names from a screen root (BRIG-184): <c>mainmenu/buttons/continue</c> is the child
///     <c>continue</c> of the child <c>buttons</c> of the child <c>mainmenu</c> of the root. A path with one name finds the
///     first control with that name at any depth. Names compare with no letter case.
/// </summary>
internal static class UiLocator
{
    internal static UIElement? Find(UIPanel? root, string path)
    {
        if (root is null || string.IsNullOrWhiteSpace(path))
            return null;

        var names = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (names.Length == 1)
            return FindDeep(root, names[0]);

        UIElement current = root;

        foreach (var name in names)
        {
            if (current is not UIPanel panel)
                return null;

            var next = panel.Children.FirstOrDefault(child => string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase));

            if (next is null)
                return null;

            current = next;
        }

        return current;
    }

    /// <summary>The path of names from the root to <paramref name="element" />, for error messages.</summary>
    internal static string PathOf(UIElement element)
    {
        var names = new Stack<string>();

        for (UIElement? e = element; e?.Parent is not null; e = e.Parent)
            names.Push(string.IsNullOrEmpty(e.Name) ? $"({e.GetType().Name})" : e.Name);

        return string.Join('/', names);
    }

    private static UIElement? FindDeep(UIPanel panel, string name)
    {
        foreach (var child in panel.Children)
            if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
                return child;

        foreach (var child in panel.Children)
            if (child is UIPanel inner && FindDeep(inner, name) is { } found)
                return found;

        return null;
    }
}
