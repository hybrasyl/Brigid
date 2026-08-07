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

    /// <summary>
    ///     Resolves the frame span an animated control's <c>&lt;IMAGE&gt;</c> list denotes — the first entry's frame
    ///     through the last entry's frame, inclusive.
    /// </summary>
    /// <remarks>
    ///     <c>&lt;IMAGE&gt;</c> is an explicit ordered list of visual states, not a frame range, so a control that is
    ///     animated rather than stateful carries only the endpoints of its span: <c>_nstart</c>'s LOGO declares
    ///     <c>_nslogo.spf</c> 0 and 19 for a 20-frame loop. Expanding it belongs to the consuming panel, as it does to
    ///     the retail pane class. Entries after the first are read for their frame only; a list that does not ascend
    ///     yields the first entry alone.
    /// </remarks>
    public static (string ImageName, int FirstFrame, int FrameCount) ResolveAnimationSpan(
        IReadOnlyList<(string ImageName, int FrameIndex)>? images)
    {
        if (images is null or { Count: 0 })
            return (string.Empty, 0, 0);

        (var imageName, var firstFrame) = images[0];
        var lastFrame = images[^1].FrameIndex;

        return (imageName, firstFrame, Math.Max(lastFrame - firstFrame + 1, 1));
    }

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