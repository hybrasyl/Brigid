#region
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Models;

/// <summary>
///     A single legend mark entry for the Legend tab page.
/// </summary>
public sealed record LegendMarkEntry(
    string Text,
    Color Color,
    byte Icon = 0,
    string Key = "");