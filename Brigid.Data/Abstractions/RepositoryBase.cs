#region
using Microsoft.Extensions.Caching.Memory;
#endregion

namespace Brigid.Data.Abstractions;

public abstract class RepositoryBase
{
    protected MemoryCache Cache { get; } = new(new MemoryCacheOptions());

    private readonly Lock CacheLock = new();

    protected virtual void ConfigureEntry(ICacheEntry entry)
        => entry.SetPriority(CacheItemPriority.Normal)
                .SetSlidingExpiration(TimeSpan.FromMinutes(15))
                .RegisterPostEvictionCallback(HandleDisposableEntries);

    /// <summary>
    ///     Returns the cached value for <paramref name="key" />, or creates it with <paramref name="factory" /> and caches
    ///     it. Throws when the factory returns null, and caches nothing then. For an asset that can be absent, use
    ///     <see cref="GetOrCreateOrNull{T}" />.
    /// </summary>
    protected virtual T GetOrCreate<T>(string key, Func<T> factory)
    {
        if (Cache.TryGetValue(key, out T? cached))
            return cached!;

        using var scope = CacheLock.EnterScope();

        if (Cache.TryGetValue(key, out cached))
            return cached!;

        return Add(key, factory() ?? throw new NullReferenceException());
    }

    /// <summary>
    ///     Returns the cached value for <paramref name="key" />, or creates it with <paramref name="factory" />. Returns
    ///     null when the factory returns null. Caches only a value that is not null, so a later call looks again.
    /// </summary>
    protected T? GetOrCreateOrNull<T>(string key, Func<T?> factory) where T : class
    {
        if (Cache.TryGetValue(key, out T? cached) && cached is not null)
            return cached;

        using var scope = CacheLock.EnterScope();

        if (Cache.TryGetValue(key, out cached) && cached is not null)
            return cached;

        return factory() is { } value ? Add(key, value) : null;
    }

    protected virtual T GetOrCreate<T>(int key, Func<T> factory)
    {
        if (Cache.TryGetValue(key, out T? cached))
            return cached!;

        using var scope = CacheLock.EnterScope();

        if (Cache.TryGetValue(key, out cached))
            return cached!;

        return Add(key, factory() ?? throw new NullReferenceException());
    }

    //zero-alloc cache hit path: callers pass TState as a struct and a static lambda, avoiding closure+Func allocation on every call
    protected T GetOrCreate<T, TState>(int key, TState state, Func<TState, T> factory)
    {
        if (Cache.TryGetValue(key, out T? cached))
            return cached!;

        using var scope = CacheLock.EnterScope();

        if (Cache.TryGetValue(key, out cached))
            return cached!;

        return Add(key, factory(state) ?? throw new NullReferenceException());
    }

    protected T GetOrCreate<T, TState>(string key, TState state, Func<TState, T> factory)
    {
        if (Cache.TryGetValue(key, out T? cached))
            return cached!;

        using var scope = CacheLock.EnterScope();

        if (Cache.TryGetValue(key, out cached))
            return cached!;

        return Add(key, factory(state) ?? throw new NullReferenceException());
    }

    //the caller holds CacheLock and has checked the value is not null
    private T Add<T>(object key, T value)
    {
        using (var entry = Cache.CreateEntry(key))
        {
            ConfigureEntry(entry);
            entry.Value = value;
        }

        return value;
    }

    protected static void HandleDisposableEntries(
        object key,
        object? value,
        EvictionReason reason,
        object? state)
    {
        if (value is IDisposable disposable)
            disposable.Dispose();
    }

    /// <summary>
    ///     Removes a cached entry by key.
    /// </summary>
    public void Invalidate(string key) => Cache.Remove(key);
}
