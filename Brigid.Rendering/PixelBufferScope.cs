#region
using System.Buffers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     RAII wrapper around <see cref="ArrayPool{Color}" /> buffers used for CPU-side texture pixel manipulation.
///     Callers rent a buffer (optionally initialized from an existing texture), mutate it, optionally upload it to a
///     texture, and the scope guarantees the buffer is returned to the pool on dispose.
/// </summary>
public readonly ref struct PixelBufferScope
{
    /// <summary>
    ///     Rented pixel buffer. Indexed row-major as <c>Pixels[y * Width + x]</c>.
    /// </summary>
    /// <remarks>
    ///     <see cref="ArrayPool{T}.Rent" /> may return a buffer larger than <see cref="Count" />; only the first
    ///     <see cref="Count" /> elements are valid. Use <see cref="AsSpan" /> for a bounds-safe view, or always index
    ///     <c>[0, Count)</c>.
    /// </remarks>
    public Color[] Pixels { get; }
    public int Count { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>
    ///     Returns a <see cref="Span{T}" /> over the valid portion of <see cref="Pixels" /> (indices <c>0..Count</c>).
    /// </summary>
    public Span<Color> AsSpan() => Pixels.AsSpan(0, Count);

    /// <summary>
    ///     Rents a buffer sized to <paramref name="source" /> and populates it with the texture's current pixels.
    /// </summary>
    public PixelBufferScope(Texture2D source)
    {
        Width = source.Width;
        Height = source.Height;
        Count = Width * Height;
        Pixels = ArrayPool<Color>.Shared.Rent(Count);

        try
        {
            source.GetData(Pixels, 0, Count);
        } catch
        {
            ArrayPool<Color>.Shared.Return(Pixels);

            throw;
        }
    }

    /// <summary>
    ///     Rents a buffer for a <paramref name="width" />x<paramref name="height" /> image without reading any source.
    ///     The rented buffer is NOT zeroed — callers must initialize any pixels they rely on (or call
    ///     <see cref="System.Array.Clear(System.Array, int, int)" /> on <see cref="Pixels" />).
    /// </summary>
    public PixelBufferScope(int width, int height)
    {
        Width = width;
        Height = height;
        Count = width * height;
        Pixels = ArrayPool<Color>.Shared.Rent(Count);
    }

    /// <summary>
    ///     Uploads the current <see cref="Pixels" /> contents to <paramref name="destination" /> via
    ///     <see cref="Texture2D.SetData{T}(T[], int, int)" />. The destination must have the same pixel count.
    /// </summary>
    public void CommitTo(Texture2D destination) => destination.SetData(Pixels, 0, Count);

    public void Dispose() => ArrayPool<Color>.Shared.Return(Pixels);
}
