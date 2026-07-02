using MiniExplorer.Helpers;

namespace MiniExplorer.Tests;

public sealed class BoundedLruCacheTests
{
    [Fact]
    public void GetOrAdd_EvictsLeastRecentlyUsedEntryAtCapacity()
    {
        const int capacity = 512;
        var cache = new BoundedLruCache<int, string>(capacity);

        for (var key = 0; key <= capacity; key++)
        {
            cache.GetOrAdd(key, value => value.ToString());
        }

        Assert.Equal(capacity, cache.Count);
        Assert.False(cache.ContainsKey(0));
        Assert.True(cache.ContainsKey(1));
        Assert.True(cache.ContainsKey(capacity));
    }

    [Fact]
    public void GetOrAdd_CacheHitUpdatesRecency()
    {
        var cache = new BoundedLruCache<int, string>(2);

        cache.GetOrAdd(1, _ => "one");
        cache.GetOrAdd(2, _ => "two");
        cache.GetOrAdd(1, _ => throw new InvalidOperationException());
        cache.GetOrAdd(3, _ => "three");

        Assert.True(cache.ContainsKey(1));
        Assert.False(cache.ContainsKey(2));
        Assert.True(cache.ContainsKey(3));
    }

    [Fact]
    public void Remove_ForcesFactoryToRunAgain()
    {
        var cache = new BoundedLruCache<string, int>(2);
        var factoryCalls = 0;

        cache.GetOrAdd("icon", _ => ++factoryCalls);
        cache.Remove("icon");
        var refreshed = cache.GetOrAdd("icon", _ => ++factoryCalls);

        Assert.Equal(2, refreshed);
        Assert.Equal(2, factoryCalls);
    }
}
