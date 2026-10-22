namespace Brigid.Rendering;

/// <summary>
///     The texture cache behind <see cref="ThemeArt" />. Separate from it, with the value type, the clock and the dispose
///     action given by the caller, so the eviction rules can be tested without a graphics device.
///     <para>
///         An entry is evicted when the cache is above its capacity, or above its byte budget, and the entry is the least
///         recently used. The byte budget matters for a part drawn at the window size (a background): each window size is
///         another entry, so a resize would otherwise keep a full-window texture for each size it passed. An evicted
///         value is not disposed at once: a deferred <c>SpriteBatch</c> can still hold it for a draw in the same frame.
///         It is disposed on a later call, when it has been out of the cache for <see cref="DisposeDelayMs" />. When the
///         values that wait are more than the byte budget (a resize evicts a full-window value on each frame), the ones
///         evicted before the last <see cref="EndFrame" /> go at once: no batch can hold them after that frame.
///     </para>
/// </summary>
internal sealed class ThemeTextureCache<TKey, TValue>
    where TKey: notnull
    where TValue: class
{
    /// <summary>How long an evicted value waits before it is disposed. Much longer than one frame.</summary>
    public const long DisposeDelayMs = 1000;

    private readonly int Capacity;
    private readonly long ByteBudget;
    private readonly Func<TValue, long> SizeOf;
    private readonly Func<long> Clock;
    private readonly Action<TValue> DisposeValue;
    private readonly Dictionary<TKey, Entry> Entries = new();
    private readonly List<(TValue Value, long Bytes, long EvictedAt, long Frame)> Evicted = [];
    private long Frame;

    public ThemeTextureCache(int capacity, long byteBudget, Func<TValue, long> sizeOf, Func<long> clock, Action<TValue> disposeValue)
    {
        Capacity = capacity;
        ByteBudget = byteBudget;
        SizeOf = sizeOf;
        Clock = clock;
        DisposeValue = disposeValue;
    }

    public int Count => Entries.Count;

    /// <summary>The bytes of the values in the cache. A value that waits to be disposed is not in it.</summary>
    public long Bytes { get; private set; }

    /// <summary>The bytes of the evicted values that wait to be disposed.</summary>
    public long PendingBytes { get; private set; }

    /// <summary>
    ///     Marks the end of a frame, after the last sprite batch ends. When the values that wait for dispose are over the
    ///     byte budget, it disposes the ones evicted before this call.
    /// </summary>
    public void EndFrame()
    {
        Frame++;
        DisposeExpired(Clock());
    }

    /// <summary>
    ///     Gets the value for <paramref name="key" />, or makes it with <paramref name="create" />. A null result is cached
    ///     too, so a part that does not exist is not looked up again.
    /// </summary>
    public TValue? GetOrCreate(TKey key, Func<TValue?> create)
    {
        var now = Clock();
        DisposeExpired(now);

        if (Entries.TryGetValue(key, out var entry))
        {
            entry.LastUsed = now;

            return entry.Value;
        }

        var value = create();
        var bytes = value is null ? 0 : SizeOf(value);
        Entries[key] = new Entry(value, bytes, now);
        Bytes += bytes;

        if ((Entries.Count > Capacity) || (Bytes > ByteBudget))
            EvictLeastRecentlyUsed(key, now);

        return value;
    }

    /// <summary>Moves every value out of the cache. Each one is disposed after the delay, the same as an eviction.</summary>
    public void Clear()
    {
        var now = Clock();

        foreach (var entry in Entries.Values)
            if (entry.Value is not null)
                AddEvicted(entry.Value, entry.Bytes, now);

        Entries.Clear();
        Bytes = 0;
    }

    //evict down to three quarters of the capacity and of the byte budget, so a cache at its limit does not evict on
    //every new entry. The entry just added is never evicted: its value is about to be drawn.
    private void EvictLeastRecentlyUsed(TKey added, long now)
    {
        var target = Math.Max(1, Capacity * 3 / 4);
        var byteTarget = ByteBudget / 4 * 3;

        foreach (var (key, entry) in Entries.OrderBy(pair => pair.Value.LastUsed).ToList())
        {
            if ((Entries.Count <= target) && (Bytes <= byteTarget))
                break;

            if (EqualityComparer<TKey>.Default.Equals(key, added))
                continue;

            Entries.Remove(key);
            Bytes -= entry.Bytes;

            if (entry.Value is not null)
                AddEvicted(entry.Value, entry.Bytes, now);
        }
    }

    private void AddEvicted(TValue value, long bytes, long now)
    {
        Evicted.Add((value, bytes, now, Frame));
        PendingBytes += bytes;
    }

    //a value waits out the delay, or, when the waiting values are over the budget, only to the end of its frame
    private void DisposeExpired(long now)
    {
        var overBudget = PendingBytes > ByteBudget;

        for (var i = Evicted.Count - 1; i >= 0; i--)
        {
            var (value, bytes, evictedAt, frame) = Evicted[i];

            if (((now - evictedAt) < DisposeDelayMs) && !(overBudget && (frame < Frame)))
                continue;

            DisposeValue(value);
            PendingBytes -= bytes;
            Evicted.RemoveAt(i);
        }
    }

    private sealed class Entry(TValue? value, long bytes, long lastUsed)
    {
        public TValue? Value { get; } = value;
        public long Bytes { get; } = bytes;
        public long LastUsed { get; set; } = lastUsed;
    }
}
