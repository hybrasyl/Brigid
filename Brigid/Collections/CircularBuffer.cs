#region
using System.Collections;
#endregion

namespace Brigid.Collections;

/// <summary>
///     Fixed-capacity ring buffer that overwrites the oldest entry when full. Supports O(1) Add and indexed access.
///     Implements <see cref="IReadOnlyList{T}" /> so it can be passed to consumers expecting indexed enumeration.
/// </summary>
public sealed class CircularBuffer<T> : IReadOnlyList<T>
{
    private readonly T[] Buffer;
    private int Head;

    public int Count { get; private set; }

    public CircularBuffer(int capacity) => Buffer = new T[capacity];

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Count);

            return Buffer[(Head - Count + index + Buffer.Length) % Buffer.Length];
        }
    }

    public void Add(T item)
    {
        Buffer[Head] = item;
        Head = (Head + 1) % Buffer.Length;

        if (Count < Buffer.Length)
            Count++;
    }

    public void Clear()
    {
        Array.Clear(Buffer, 0, Buffer.Length);
        Head = 0;
        Count = 0;
    }

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator : IEnumerator<T>
    {
        private readonly CircularBuffer<T> Buffer;
        private int Index;

        internal Enumerator(CircularBuffer<T> buffer)
        {
            Buffer = buffer;
            Index = -1;
        }

        public readonly T Current => Buffer[Index];
        readonly object IEnumerator.Current => Current!;

        public bool MoveNext() => ++Index < Buffer.Count;

        public void Reset() => Index = -1;

        public readonly void Dispose() { }
    }
}