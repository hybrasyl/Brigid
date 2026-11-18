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
    string Key = "")
{
    /// <summary>
    ///     True for a mark of the invisible colour, which the world screen gives as <see cref="Color.Transparent" />. Other
    ///     players do not see the mark; the player sees their own, and the Legend window shows it in the disabled text colour.
    /// </summary>
    public bool IsHidden => Color == Color.Transparent;
}