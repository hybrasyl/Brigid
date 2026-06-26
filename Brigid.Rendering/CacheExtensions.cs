namespace Brigid.Rendering;

public static class CacheExtensions
{
    public static TValue GetOrAdd<TKey, TValue>(
        this Dictionary<TKey, TValue> cache,
        TKey key,
        Func<TKey, TValue> factory)
        where TKey : notnull
    {
        if (cache.TryGetValue(key, out var existing))
            return existing;

        var created = factory(key);
        cache[key] = created;

        return created;
    }

    public static void DisposeAndClear<TKey, TValue>(this Dictionary<TKey, TValue> cache)
        where TKey : notnull
        where TValue : IDisposable?
    {
        foreach (var value in cache.Values)
            value?.Dispose();

        cache.Clear();
    }
}
