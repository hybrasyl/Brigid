#region
using Brigid.Controls.Components;
using Brigid.Networking;
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
    ///     The largest width and height of a portrait. The player's own file has no size limit, and a larger image can be
    ///     too large for a texture, so it shows no portrait.
    /// </summary>
    public const int MAX_SIDE = 1024;

    /// <summary>
    ///     Shows <paramref name="data" /> in <paramref name="image" />, in place of the portrait it shows now. Empty data,
    ///     data that does not decode, or an image larger than <see cref="MAX_SIDE" /> shows no portrait.
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
    ///     The decoded portrait, or null when <paramref name="data" /> is empty, does not decode, or is larger than
    ///     <see cref="MAX_SIDE" />. The size comes from the image header, so a large image is not decoded.
    /// </summary>
    public static SKImage? Decode(byte[]? data)
    {
        if (data is not { Length: > 0 })
            return null;

        using var encoded = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(encoded);

        if (codec is null)
            return null;

        if ((codec.Info.Width > MAX_SIDE) || (codec.Info.Height > MAX_SIDE))
        {
            NoticeDebugLog.Write($"[profile] the portrait is {codec.Info.Width}x{codec.Info.Height}, larger than {MAX_SIDE} on a side; it does not show");

            return null;
        }

        return SKImage.FromEncodedData(encoded);
    }
}
