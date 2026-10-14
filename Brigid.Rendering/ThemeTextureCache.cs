namespace Brigid.Rendering;

/// <summary>
///     The texture cache behind <see cref="ThemeArt" />. Separate from it, with the value type, the clock and the dispose
///     action given by the caller, so the eviction rules can be tested without a graphics device.
///     <para>
///         An entry is evicted when the cache is above its capacity and the entry is the least recently used. An evicted
///         value is not disposed at once: a deferred <c>SpriteBatch</c> can still hold it for a draw in the same frame.
///         It is disposed on a later call, when it has been out of the cache for <see cref="DisposeDelayMs" />.
///     </para>
/// </summary>
internal sealed class ThemeTextureCache<TKey, TValue>
    where TKey: notnull
    where TValue: class
{
    /// <summary>How long an evicted value waits before it is disposed. Much longer than one frame.</summary>
    public const long DisposeDelayMs = 1000;

    private readonly int Capacity;
    private readonly Func<long> Clock;
    private readonly Action<TValue> DisposeValue;
    private readonly Dictionary<TKey, Entry> Entries = new();
    private readonly List<(TValue Value, long EvictedAt)> Evicted = [];

    public ThemeTextureCache(int capacity, Func<long> clock, Action<TValue> disposeValue)
    {
        Capacity = capacity;
        Clock = clock;
        DisposeValue = disposeValue;
    }

    public int Count => Entries.Count;

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
        Entries[key] = new Entry(value, now);

        if (Entries.Count > Capacity)
            EvictLeastRecentlyUsed(key, now);

        return value;
    }

    /// <summary>Moves every value out of the cache. Each one is disposed after the delay, the same as an eviction.</summary>
    public void Clear()
    {
        var now = Clock();

        foreach (var entry in Entries.Values)
            if (entry.Value is not null)
                Evicted.Add((entry.Value, now));

        Entries.Clear();
    }

    //evict down to three quarters of the capacity, so a cache at its limit does not evict on every new entry. The
    //entry just added is never evicted: its value is about to be drawn.
    private void EvictLeastRecentlyUsed(TKey added, long now)
    {
        var target = Math.Max(1, Capacity * 3 / 4);

        foreach (var (key, entry) in Entries.OrderBy(pair => pair.Value.LastUsed).ToList())
        {
            if (Entries.Count <= target)
                break;

            if (EqualityComparer<TKey>.Default.Equals(key, added))
                continue;

            Entries.Remove(key);

            if (entry.Value is not null)
                Evicted.Add((entry.Value, now));
        }
    }

    private void DisposeExpired(long now)
    {
        for (var i = Evicted.Count - 1; i >= 0; i--)
        {
            if ((now - Evicted[i].EvictedAt) < DisposeDelayMs)
                continue;

            DisposeValue(Evicted[i].Value);
            Evicted.RemoveAt(i);
        }
    }

    private sealed class Entry(TValue? value, long lastUsed)
    {
        public TValue? Value { get; } = value;
        public long LastUsed { get; set; } = lastUsed;
    }
}
