using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace XDb.Core;

internal struct ThreadSafeMap<TKey, TValue>
{
    public static ThreadSafeMap<TKey, TValue> Create()
        => new ThreadSafeMap<TKey, TValue>
        {
            _inner = new ConcurrentDictionary<TKey, TValue>(),
        };

    private ConcurrentDictionary<TKey, TValue> _inner;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(TKey key, out TValue value)
        => _inner.TryGetValue(key, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        => _inner.GetOrAdd(key, valueFactory);
}
