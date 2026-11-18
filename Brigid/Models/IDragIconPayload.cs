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
}
