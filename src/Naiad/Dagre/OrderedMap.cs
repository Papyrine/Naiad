/// <summary>
/// A string-keyed map that preserves insertion order across additions and removals. Insertion order is the
/// property the layout's deterministic output depends on.
/// </summary>
/// <remarks>
/// A chained hash table over one entry array that is only ever appended to: a removed entry is unlinked
/// from its bucket and left behind as a dead slot, so <see cref="Remove"/> is O(1) and the survivors keep
/// their relative order. Dead slots are squeezed out when the array is next rebuilt. The runtime's
/// <see cref="OrderedDictionary{TKey,TValue}"/> keeps its entries contiguous instead, which makes every
/// removal shift the tail of the array and re-link its buckets — O(n) each, and so quadratic for the layout
/// phases that delete nodes in bulk (undoing normalization removes every dummy node, front to back).
/// Nothing is allocated until the first insertion, because a layout builds many thousands of these as small
/// per-node maps, a good share of which stay empty.
/// </remarks>
sealed class OrderedMap<TValue> : IEnumerable<KeyValuePair<string, TValue>>
{
    const int initialCapacity = 4;

    // Below this many slots a sparse array is not worth compacting on removal.
    const int minCompactSlots = 16;

    // buckets[hash & mask] holds the 1-based index of the newest entry in that chain (0 = empty chain).
    int[]? buckets;
    Entry[]? entries;

    // Slots handed out so far, live or dead; the next insertion lands at entries[used].
    int used;
    int version;

    public int Count { get; private set; }

    public bool ContainsKey(string key) => Find(key) >= 0;

    public TValue this[string key]
    {
        get
        {
            var index = Find(key);
            if (index < 0)
            {
                throw new KeyNotFoundException($"The given key '{key}' was not present in the map.");
            }

            return entries![index].Value;
        }
        // Updates an existing key in place (keeping its position) and appends an unseen key at the end.
        set
        {
            var index = Find(key);
            if (index >= 0)
            {
                entries![index].Value = value;
                return;
            }

            Append(key, value);
        }
    }

    public bool TryGetValue(string key, out TValue value)
    {
        var index = Find(key);
        if (index >= 0)
        {
            value = entries![index].Value;
            return true;
        }

        value = default!;
        return false;
    }

    public TValue? GetValueOrDefault(string key)
    {
        var index = Find(key);
        if (index >= 0)
        {
            return entries![index].Value;
        }

        return default;
    }

    public void Remove(string key)
    {
        if (buckets == null)
        {
            return;
        }

        var hash = key.GetHashCode();
        ref var bucket = ref buckets[hash & (buckets.Length - 1)];
        var previous = -1;
        var index = bucket - 1;
        while (index >= 0)
        {
            ref var entry = ref entries![index];
            if (entry.Hash == hash &&
                entry.Key == key)
            {
                if (previous < 0)
                {
                    bucket = entry.Next + 1;
                }
                else
                {
                    entries[previous].Next = entry.Next;
                }

                // A null key marks the slot dead; clearing the value lets go of whatever it referenced.
                entry = default;
                Count--;
                version++;

                // Once three quarters of the slots are dead, squeeze them out so enumeration does not keep
                // stepping over them. The rebuild is paid for by the removals that emptied the slots.
                if (used >= minCompactSlots &&
                    Count < used / 4)
                {
                    Rebuild(CapacityFor(Count * 2));
                }

                return;
            }

            previous = index;
            index = entry.Next;
        }
    }

    /// <summary>Keys in insertion order (a snapshot, safe to mutate the map while iterating the result).</summary>
    public List<string> Keys()
    {
        var keys = new List<string>(Count);
        for (var i = 0; i < used; i++)
        {
            if (entries![i].Key is { } key)
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    /// <summary>Values in insertion order (a snapshot).</summary>
    public List<TValue> Values()
    {
        var values = new List<TValue>(Count);
        for (var i = 0; i < used; i++)
        {
            ref var entry = ref entries![i];
            if (entry.Key != null)
            {
                values.Add(entry.Value);
            }
        }

        return values;
    }

    /// <summary>Key/value pairs in insertion order (a snapshot, safe to mutate the map while iterating the
    /// result). Carries each value alongside its key, so callers avoid a second lookup per key.</summary>
    public List<KeyValuePair<string, TValue>> Entries()
    {
        var pairs = new List<KeyValuePair<string, TValue>>(Count);
        for (var i = 0; i < used; i++)
        {
            ref var entry = ref entries![i];
            if (entry.Key is { } key)
            {
                pairs.Add(new(key, entry.Value));
            }
        }

        return pairs;
    }

    /// <summary>Keys in insertion order, enumerated without allocating a snapshot list. Unlike
    /// <see cref="Keys"/> the map must not be mutated while the result is iterated.</summary>
    public KeyEnumerable EnumerateKeys() => new(this);

    /// <summary>Values in insertion order, enumerated without allocating a snapshot list. Unlike
    /// <see cref="Values"/> the map must not be mutated while the result is iterated.</summary>
    public ValueEnumerable EnumerateValues() => new(this);

    // A struct GetEnumerator so `foreach (var kv in map)` in the hot layout passes does not box. The explicit
    // interface implementations remain for IEnumerable/LINQ callers.
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<KeyValuePair<string, TValue>> IEnumerable<KeyValuePair<string, TValue>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    int Find(string key)
    {
        if (buckets == null)
        {
            return -1;
        }

        var hash = key.GetHashCode();
        var index = buckets[hash & (buckets.Length - 1)] - 1;
        while (index >= 0)
        {
            ref var entry = ref entries![index];
            if (entry.Hash == hash &&
                entry.Key == key)
            {
                return index;
            }

            index = entry.Next;
        }

        return -1;
    }

    void Append(string key, TValue value)
    {
        if (entries == null)
        {
            buckets = new int[initialCapacity];
            entries = new Entry[initialCapacity];
        }
        else if (used == entries.Length)
        {
            // Out of slots. If at least half of them are dead, reclaiming those is enough; otherwise double.
            if (Count <= used / 2)
            {
                Rebuild(entries.Length);
            }
            else
            {
                Rebuild(entries.Length * 2);
            }
        }

        var hash = key.GetHashCode();
        ref var bucket = ref buckets![hash & (buckets.Length - 1)];
        entries[used] = new()
        {
            Key = key,
            Value = value,
            Hash = hash,
            Next = bucket - 1
        };
        used++;
        bucket = used;
        Count++;
        version++;
    }

    // Copies the live entries, in order, into fresh arrays of the given power-of-two capacity.
    void Rebuild(int capacity)
    {
        var newBuckets = new int[capacity];
        var newEntries = new Entry[capacity];
        var next = 0;
        for (var i = 0; i < used; i++)
        {
            ref var entry = ref entries![i];
            if (entry.Key == null)
            {
                continue;
            }

            ref var bucket = ref newBuckets[entry.Hash & (capacity - 1)];
            newEntries[next] = entry with { Next = bucket - 1 };
            next++;
            bucket = next;
        }

        buckets = newBuckets;
        entries = newEntries;
        used = next;
    }

    static int CapacityFor(int count)
    {
        var capacity = initialCapacity;
        while (capacity < count)
        {
            capacity *= 2;
        }

        return capacity;
    }

    struct Entry
    {
        // Null once the entry has been removed.
        public string? Key;
        public TValue Value;
        public int Hash;

        // Index of the next entry in the same bucket chain, or -1 at the end of the chain.
        public int Next;
    }

    public struct Enumerator(OrderedMap<TValue> map) : IEnumerator<KeyValuePair<string, TValue>>
    {
        readonly int version = map.version;
        int index;

        public KeyValuePair<string, TValue> Current { get; private set; }

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (version != map.version)
            {
                throw new InvalidOperationException("The map was modified while it was being enumerated.");
            }

            while (index < map.used)
            {
                ref var entry = ref map.entries![index];
                index++;
                if (entry.Key is { } key)
                {
                    Current = new(key, entry.Value);
                    return true;
                }
            }

            Current = default;
            return false;
        }

        public void Reset()
        {
            index = 0;
            Current = default;
        }

        public readonly void Dispose()
        {
        }
    }

    public readonly struct KeyEnumerable(OrderedMap<TValue> map)
    {
        public Enumerator GetEnumerator() => new(map.GetEnumerator());

        public struct Enumerator(OrderedMap<TValue>.Enumerator inner)
        {
            OrderedMap<TValue>.Enumerator inner = inner;

            public readonly string Current => inner.Current.Key;

            public bool MoveNext() => inner.MoveNext();
        }
    }

    public readonly struct ValueEnumerable(OrderedMap<TValue> map)
    {
        public Enumerator GetEnumerator() => new(map.GetEnumerator());

        public struct Enumerator(OrderedMap<TValue>.Enumerator inner)
        {
            OrderedMap<TValue>.Enumerator inner = inner;

            public readonly TValue Current => inner.Current.Value;

            public bool MoveNext() => inner.MoveNext();
        }
    }
}
