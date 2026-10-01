namespace DecompilerServer.Services;

/// <summary>Size-bounded LRU storage. Factories run once per concurrent miss; oversized values remain usable without being retained.</summary>
internal sealed class BoundedCache<TKey, TValue> where TKey : notnull
{
    private readonly object _gate = new();
    private readonly Dictionary<TKey, (TValue Value, long Bytes, LinkedListNode<TKey> Node)> _entries;
    private readonly LinkedList<TKey> _recency = new();
    private readonly Func<TValue, long> _size;
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private long _bytes;
    private long _generation;
    private readonly Dictionary<TKey, Lazy<TValue>> _pending;

    public BoundedCache(int maxEntries, long maxBytes, Func<TValue, long> size, IEqualityComparer<TKey>? comparer = null)
    {
        if (maxEntries <= 0 || maxBytes < 64)
            throw new ArgumentOutOfRangeException(nameof(maxEntries), "Cache capacity must be positive and at least 64 bytes.");
        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
        _size = size;
        _entries = new(comparer);
        _pending = new(comparer);
    }

    public int Count { get { lock (_gate) return _entries.Count; } }
    public long Bytes { get { lock (_gate) return _bytes; } }
    public TValue[] Values { get { lock (_gate) return _entries.Values.Select(entry => entry.Value).ToArray(); } }

    public bool TryGetValue(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                _recency.Remove(entry.Node);
                _recency.AddLast(entry.Node);
                value = entry.Value;
                return true;
            }
            value = default!;
            return false;
        }
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
    {
        Lazy<TValue> pending;
        long generation;
        lock (_gate)
        {
            if (TryGetValue(key, out var value))
                return value;
            generation = _generation;
            if (!_pending.TryGetValue(key, out pending!))
                _pending.Add(key, pending = new Lazy<TValue>(() => factory(key), LazyThreadSafetyMode.ExecutionAndPublication));
        }
        try
        {
            var value = pending.Value;
            lock (_gate)
                if (generation == _generation)
                    TryAdd(key, value);
            return value;
        }
        finally
        {
            lock (_gate)
                if (_pending.GetValueOrDefault(key) == pending)
                    _pending.Remove(key);
        }
    }

    public bool TryAdd(TKey key, TValue value)
    {
        lock (_gate)
        {
            if (_entries.ContainsKey(key))
                return false;
            var bytes = Math.Max(64, _size(value)) + (key is string text ? 24L + text.Length * 2L : 32L);
            if (bytes > _maxBytes)
                return false;
            while (_entries.Count >= _maxEntries || _bytes + bytes > _maxBytes)
            {
                var oldest = _recency.First!;
                _bytes -= _entries[oldest.Value].Bytes;
                _entries.Remove(oldest.Value);
                _recency.RemoveFirst();
            }
            _entries.Add(key, (value, bytes, _recency.AddLast(key)));
            _bytes += bytes;
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _recency.Clear();
            _bytes = 0;
            _pending.Clear();
            _generation++;
        }
    }
}
