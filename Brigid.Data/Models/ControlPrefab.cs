#region
using System.Collections.ObjectModel;
using DALib.Drawing;
using SkiaSharp;
#endregion

namespace Brigid.Data.Models;

/// <summary>
///     A resolved UI control definition paired with its pre-rendered images.
/// </summary>
public sealed class ControlPrefab : IDisposable
{
    /// <summary>
    ///     The parsed control definition from DALib.
    /// </summary>
    public required Control Control { get; init; }

    /// <summary>
    ///     Pre-rendered images corresponding 1:1 to <see cref="Control.Images" /> entries.
    /// </summary>
    public required IReadOnlyList<SKImage> Images { get; init; }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var image in Images)
            image.Dispose();
    }
}

/// <summary>
///     A named collection of <see cref="ControlPrefab" /> entries parsed from a single control file (.txt).
/// </summary>
/// <remarks>
///     The first control with type <see cref="DALib.Definitions.ControlType.Anchor" /> defines the overall panel bounds.
/// </remarks>
public sealed class ControlPrefabSet(string name) : KeyedCollection<string, ControlPrefab>(StringComparer.OrdinalIgnoreCase), IDisposable
{
    public string Name { get; } = name;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var prefab in this)
            prefab.Dispose();
    }

    /// <inheritdoc />
    protected override string GetKeyForItem(ControlPrefab item) => item.Control.Name;
}