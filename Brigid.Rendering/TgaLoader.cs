#region
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Minimal TGA loader for BMFont atlas output. Supports the formats BMFont actually emits:
///     type 3 (uncompressed grayscale, 8bpp — alpha-only white-on-transparent) and type 2 (uncompressed true-color,
///     32bpp BGRA). Top-left or bottom-left origin honored via the descriptor byte.
/// </summary>
public static class TgaLoader
{
    public static Color[] Load(string path, out int width, out int height)
    {
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length < 18)
            throw new InvalidDataException($"TGA too small: {path}");

        var idLength = bytes[0];
        var imageType = bytes[2];
        width = bytes[12] | (bytes[13] << 8);
        height = bytes[14] | (bytes[15] << 8);
        var bpp = bytes[16];
        var descriptor = bytes[17];
        var topDown = (descriptor & 0x20) != 0;
        var dataStart = 18 + idLength;

        var pixels = new Color[width * height];

        switch (imageType)
        {
            //uncompressed grayscale — treat the single byte as alpha, color is white
            case 3 when bpp == 8:
                DecodeGrayscale(bytes, dataStart, width, height, topDown, pixels);

                break;

            //uncompressed true-color, 32bpp BGRA
            case 2 when bpp == 32:
                DecodeBgra32(bytes, dataStart, width, height, topDown, pixels);

                break;

            default:
                throw new InvalidDataException($"Unsupported TGA format: type={imageType} bpp={bpp} in {path}");
        }

        return pixels;
    }

    //Output is premultiplied alpha to match SpriteBatch.Begin defaults elsewhere in the pipeline.
    //For white-on-transparent BMFont glyphs, RGB == alpha.

    private static void DecodeBgra32(
        byte[] bytes,
        int dataStart,
        int width,
        int height,
        bool topDown,
        Color[] pixels)
    {
        for (var y = 0; y < height; y++)
        {
            var srcY = topDown ? y : height - 1 - y;

            for (var x = 0; x < width; x++)
            {
                var srcOffset = dataStart + (srcY * width + x) * 4;
                var b = bytes[srcOffset + 0];
                var g = bytes[srcOffset + 1];
                var r = bytes[srcOffset + 2];
                var a = bytes[srcOffset + 3];
                pixels[y * width + x] = new Color(
                    (byte)(r * a / 255),
                    (byte)(g * a / 255),
                    (byte)(b * a / 255),
                    a);
            }
        }
    }

    private static void DecodeGrayscale(
        byte[] bytes,
        int dataStart,
        int width,
        int height,
        bool topDown,
        Color[] pixels)
    {
        for (var y = 0; y < height; y++)
        {
            var srcY = topDown ? y : height - 1 - y;

            for (var x = 0; x < width; x++)
            {
                var alpha = bytes[dataStart + srcY * width + x];
                pixels[y * width + x] = new Color(alpha, alpha, alpha, alpha);
            }
        }
    }
}
