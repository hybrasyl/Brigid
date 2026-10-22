#region
using System.Text.RegularExpressions;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>Assertions that the UI control tests share.</summary>
internal static partial class UiAssert
{
    //the id rule of the ui_panels layout plan; a dot is the path separator, so it is never part of a name
    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex ElementName();

    /// <summary>
    ///     Every child of <paramref name="panel" />, and of each child panel, has a lowercase name that is unique among its
    ///     siblings, so that a <c>ui_panels</c> pack can address it.
    /// </summary>
    public static void NamedTree(UIPanel panel)
    {
        var names = panel.Children.Select(child => child.Name).ToList();

        Assert.All(names, name => Assert.Matches(ElementName(), name));
        Assert.Equal(names.Count, names.Distinct().Count());

        //a TextButton or a tab is a panel too; its label is its own detail, not a separate target
        foreach (var child in panel.Children.OfType<UIPanel>().Where(child => child is not (TextButton or SelectableTab)))
            NamedTree(child);
    }
}
