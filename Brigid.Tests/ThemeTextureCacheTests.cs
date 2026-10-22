#region
using Brigid.Rendering;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The texture cache behind <see cref="ThemeArt" />. The background draws at a pass scale of 1 and the UI at the
///     window scale in the same frame, so both sizes must stay cached. An evicted texture must not be disposed in the
///     frame that evicted it, because the deferred sprite batch can still draw it.
/// </summary>
public class ThemeTextureCacheTests
{
    private long Now;
    private readonly List<string> Created = [];
    private readonly List<string> Disposed = [];

    private ThemeTextureCache<string, string> NewCache(int capacity, long byteBudget = long.MaxValue)
        => new(capacity, byteBudget, SizeOf, () => Now, Disposed.Add);

    //a key "name:size" stands for a texture of that many bytes
    private static long SizeOf(string value) => value.Contains(':') ? long.Parse(value[(value.IndexOf(':') + 1)..]) : 1;

    private string? Get(ThemeTextureCache<string, string> cache, string key)
        => cache.GetOrCreate(
            key,
            () =>
            {
                Created.Add(key);

                return key;
            });

    [Fact]
    public void KeepsTwoScalesOfTheSamePart()
    {
        var cache = NewCache(16);

        //one frame draws the background at scale 1 and the buttons at scale 2, again and again
        for (var frame = 0; frame < 5; frame++)
        {
            Get(cache, "background@1");
            Get(cache, "button@2");
            Now += 16;
        }

        Assert.Equal(["background@1", "button@2"], Created);
        Assert.Empty(Disposed);
    }

    [Fact]
    public void CachesAMissingPart()
    {
        var cache = NewCache(16);
        var calls = 0;

        for (var i = 0; i < 3; i++)
            Assert.Null(
                cache.GetOrCreate(
                    "missing",
                    () =>
                    {
                        calls++;

                        return null;
                    }));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void EvictsTheLeastRecentlyUsedWhenFull()
    {
        var cache = NewCache(4);

        foreach (var key in new[] { "a", "b", "c", "d" })
        {
            Get(cache, key);
            Now++;
        }

        //use a again, so b is now the oldest
        Get(cache, "a");
        Now++;
        Get(cache, "e");

        //above capacity, the cache drops to three quarters of it: b and c go
        Assert.Equal(3, cache.Count);

        Get(cache, "a");
        Get(cache, "d");
        Get(cache, "e");
        Assert.Equal(["a", "b", "c", "d", "e"], Created);
    }

    [Fact]
    public void DisposesAnEvictedValueOnlyAfterTheDelay()
    {
        var cache = NewCache(1);

        Get(cache, "a");
        Get(cache, "b");

        //a is out of the cache, but a draw queued in this frame can still use it
        Assert.Empty(Disposed);

        Now += ThemeTextureCache<string, string>.DisposeDelayMs - 1;
        Get(cache, "b");
        Assert.Empty(Disposed);

        Now += 1;
        Get(cache, "b");
        Assert.Equal(["a"], Disposed);
    }

    [Fact]
    public void ClearDefersTheDisposeToo()
    {
        var cache = NewCache(8);

        Get(cache, "a");
        Get(cache, "b");
        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Empty(Disposed);

        Now += ThemeTextureCache<string, string>.DisposeDelayMs;
        Get(cache, "c");
        Assert.Equal(["a", "b"], Disposed.Order());
    }

    [Fact]
    public void EvictsTheLeastRecentlyUsedWhenOverTheByteBudget()
    {
        var cache = NewCache(256, 100);

        Get(cache, "background@800x600:40");
        Now += 16;
        Get(cache, "background@820x615:40");
        Now += 16;
        Get(cache, "background@840x630:40");

        Assert.Equal(1, cache.Count);
        Assert.Equal(40, cache.Bytes);
        Assert.Empty(Disposed);

        Now += ThemeTextureCache<string, string>.DisposeDelayMs;
        Get(cache, "background@840x630:40");

        Assert.Equal(["background@800x600:40", "background@820x615:40"], Disposed.Order());
    }

    [Fact]
    public void AResizeKeepsTheSmallPartsThatEachFrameUses()
    {
        var cache = NewCache(256, 100);

        //each frame of a resize draws the background at a new size, and the same small button
        for (var size = 0; size < 20; size++)
        {
            Get(cache, $"background@{size}:30");
            Get(cache, "button:1");
            Now += 16;
        }

        Assert.Single(Created, "button:1");
        Assert.True(cache.Bytes <= 100);
    }

    [Fact]
    public void KeepsTheEntryJustAdded_WhenItAloneIsOverTheBudget()
    {
        var cache = NewCache(256, 10);

        Assert.Equal("huge:50", Get(cache, "huge:50"));
        Assert.Equal(1, cache.Count);

        Get(cache, "huge2:50");

        Assert.Equal(1, cache.Count);
        Assert.Equal(50, cache.Bytes);
    }

    [Fact]
    public void ClearEmptiesTheBytes()
    {
        var cache = NewCache(256, 100);
        Get(cache, "background:40");

        cache.Clear();

        Assert.Equal(0, cache.Bytes);
    }

    [Fact]
    public void AtTheEndOfAFrame_DisposesTheWaitingValues_WhenTheyAreOverTheBudget()
    {
        var cache = NewCache(256, 100);

        //a resize: each frame draws the background at a new size, and the cache evicts the one before
        for (var size = 0; size < 6; size++)
        {
            Get(cache, $"background@{size}:60");
            Now += 16;
            cache.EndFrame();
        }

        //far less than the dispose delay has passed, but the waiting values went at the end of their frames
        Assert.True(cache.PendingBytes <= 100);
        Assert.NotEmpty(Disposed);
    }

    [Fact]
    public void WithinItsFrame_AnEvictedValueIsNotDisposed_EvenOverTheBudget()
    {
        var cache = NewCache(256, 50);

        Get(cache, "background@1:60");
        cache.EndFrame();
        Get(cache, "background@2:60");
        Get(cache, "background@3:60");

        //background@2 was evicted in this frame: a batch can still draw it, so it waits
        Assert.DoesNotContain("background@2:60", Disposed);

        cache.EndFrame();

        Assert.Contains("background@2:60", Disposed);
    }

    [Fact]
    public void UnderTheBudget_TheWaitingValuesKeepTheDelay()
    {
        var cache = NewCache(256, 100);

        Get(cache, "background@1:40");
        Get(cache, "background@2:40");
        Get(cache, "background@3:40");
        cache.EndFrame();

        Assert.Empty(Disposed);
        Assert.Equal(80, cache.PendingBytes);
    }
}
