#region
using Brigid.Controls.Components;
using Brigid.Rendering;
using SkiaSharp;
#endregion

namespace Brigid.Controls.World.Popups.Profile;

/// <summary>
///     The portrait image of a profile tab: the player's own, from the local character folder (as on retail, BRIG-106), or
///     another player's, from the server. The data is a JPEG or another image format that SkiaSharp decodes.
/// </summary>
internal static class ProfilePortrait
{
    /// <summary>
    ///     Shows <paramref name="data" /> in <paramref name="image" />, in place of the portrait it shows now. Empty data, or
    ///     data that does not decode, shows no portrait.
    /// </summary>
    public static void Show(UIImage? image, byte[]? data)
    {
        if (image is null)
            return;

        image.Texture?.Dispose();
        image.Texture = null;

        using var decoded = Decode(data);

        if (decoded is not null)
            image.Texture = TextureConverter.ToTexture2D(decoded);
    }

    /// <summary>
    ///     The decoded portrait, or null when <paramref name="data" /> is empty or does not decode. Internal for the tests.
    /// </summary>
    internal static SKImage? Decode(byte[]? data) => data is { Length: > 0 } ? SKImage.FromEncodedData(data) : null;
}
