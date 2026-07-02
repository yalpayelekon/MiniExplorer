namespace MiniExplorer.Helpers;

internal sealed class BoundedLruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _entries;
    private readonly LinkedList<Entry> _usage = new();
    private readonly object _sync = new();

    public BoundedLruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
        _entries = new Dictionary<TKey, LinkedListNode<Entry>>(comparer);
    }

    internal int Count
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                MarkAsRecentlyUsed(existing);
                return existing.Value.Value;
            }
        }

        var value = valueFactory(key);

        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                MarkAsRecentlyUsed(existing);
                return existing.Value.Value;
            }

            var node = _usage.AddFirst(new Entry(key, value));
            _entries.Add(key, node);
            EvictIfNeeded();
            return value;
        }
    }

    public bool Remove(TKey key)
    {
        lock (_sync)
        {
            if (!_entries.Remove(key, out var node))
            {
                return false;
            }

            _usage.Remove(node);
            return true;
        }
    }

    internal bool ContainsKey(TKey key)
    {
        lock (_sync)
        {
            return _entries.ContainsKey(key);
        }
    }

    private void MarkAsRecentlyUsed(LinkedListNode<Entry> node)
    {
        _usage.Remove(node);
        _usage.AddFirst(node);
    }

    private void EvictIfNeeded()
    {
        if (_entries.Count <= _capacity)
        {
            return;
        }

        var leastRecentlyUsed = _usage.Last!;
        _usage.RemoveLast();
        _entries.Remove(leastRecentlyUsed.Value.Key);
    }

    private sealed record Entry(TKey Key, TValue Value);
}
