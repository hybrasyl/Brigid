#region
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Models;

/// <summary>
///     A drag payload that shows an icon at the pointer while it drags. The world screen draws the icon of any such payload,
///     so a new window's drag needs no draw of its own.
/// </summary>
public interface IDragIconPayload
{
    /// <summary>The icon at the pointer, or null for no icon.</summary>
    Texture2D? Icon { get; }

    /// <summary>
    ///     An <c>icon.*</c> role that the world screen draws at the pointer when <see cref="Icon" /> is null (the gold bag of
    ///     the Inventory window), or null for none.
    /// </summary>
    string? IconRole => null;
}
