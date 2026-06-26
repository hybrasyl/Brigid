#region
using DALib.Data;
using DALib.Definitions;
using DALib.Drawing;
using DALib.Extensions;
using DALib.IO;
#endregion

namespace Brigid.Data.Models;

/// <summary>
///     Holds the raw bytes of an HPF archive entry, deferring decompression until <see cref="Decompress" /> is called.
/// </summary>
/// <remarks>
///     Separating archive reads (sequential, not thread-safe) from decompression (CPU-bound, parallelizable) enables
///     parallel tile loading.
/// </remarks>
public sealed class CompressedHpfFile
{
    private readonly byte[] RawBytes;

    /// <summary>
    ///     For uncompressed files, returns the pixel height. For compressed files, returns 0 (height unknown until
    ///     decompressed).
    /// </summary>
    public int EstimatedPixelHeight => IsCompressed ? 0 : (RawBytes.Length - 8) / CONSTANTS.HPF_TILE_WIDTH;

    /// <summary>
    ///     True if the raw bytes have the HPF compression magic header.
    /// </summary>
    public bool IsCompressed
        => RawBytes is [0x55, 0xAA, 0x02, 0xFF, ..];

    private CompressedHpfFile(byte[] rawBytes) => RawBytes = rawBytes;

    /// <summary>
    ///     Decompresses the raw bytes into an HpfFile. Safe to call from any thread.
    /// </summary>
    public HpfFile Decompress()
    {
        Span<byte> buffer = RawBytes;

        if (IsCompressed)
            Compression.DecompressHpf(ref buffer);

        return new HpfFile(
            buffer[..8]
                .ToArray(),
            buffer[8..]
                .ToArray());
    }

    public static CompressedHpfFile FromEntry(DataArchiveEntry entry)
    {
        using var segment = entry.ToStreamSegment();

        return new CompressedHpfFile(segment.ToArray());
    }
}