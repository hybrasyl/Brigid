using System.Reflection;
using Brigid.Controls.Components;
using Xunit;

namespace Brigid.Tests;

/// <summary>
///     Drift guard for the authored-UI-primitive report (<c>Tools/Brigid.UiPrimitivesReport</c>). The generator
///     enumerates a panel's prefab children by scanning for the <c>PrefabPanel.CreateXxx("name")</c> factory calls,
///     using a hard-coded factory→type table (<c>UiModelBuilder.PrefabFactories</c>). If a new prefab factory is
///     added to <see cref="PrefabPanel" /> and the report isn't taught about it, those children silently vanish from
///     the report. This test fails when the factory surface changes so the table stays in lock-step — mirroring the
///     KeybindCatalog invariant. When it fails: update <c>PrefabFactories</c> in the tool, then this expected set.
/// </summary>
public sealed class UiPrimitivesReportTests
{
    //the protected prefab-child factories the report scans for. Keep identical to the tool's PrefabFactories keys.
    private static readonly string[] ExpectedFactories =
        ["CreateButton", "CreateImage", "CreateLabel", "CreateProgressBar", "CreateTextBox"];

    [Fact]
    public void PrefabFactorySurface_MatchesReportScanner()
    {
        var actual = typeof(PrefabPanel)
                     .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                     .Where(m => m.IsFamily && m.Name.StartsWith("Create", StringComparison.Ordinal))
                     .Select(m => m.Name)
                     .Distinct()
                     .OrderBy(n => n, StringComparer.Ordinal)
                     .ToArray();

        Assert.Equal(ExpectedFactories.OrderBy(n => n, StringComparer.Ordinal), actual);
    }
}
