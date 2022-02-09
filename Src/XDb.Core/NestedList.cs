using System;
using System.Collections;
using System.Collections.Generic;

namespace XDb.Core;

internal sealed class NestedListEnumerator<T> : IEnumerator<T>, IEnumerator
{
    private readonly List<List<T>> _inner;
    private int _currentList = 0;
    private int _currentIndex = 0;

    public NestedListEnumerator(List<List<T>> inner)
    {
        _inner = inner;
    }

    public T Current => _inner[_currentList][_currentIndex];

    #nullable disable
    object IEnumerator.Current => this.Current;
    #nullable restore

    public void Dispose()
    { }

    public bool MoveNext()
    {
        var currentList = _inner[_currentList];
        _currentIndex++;
        if (_currentIndex >= currentList.Count)
        {
            _currentList++;
            _currentIndex = 0;
            if (_currentList >= _inner.Count)
            {
                return false;
            }
        }

        return true;
    }

    public void Reset()
    {
        _currentList = 0;
        _currentIndex = 0;
    }
}

internal sealed class NestedList<T> : IReadOnlyList<T>
{
    private readonly List<List<T>> _inner;
    private readonly int _count;

    public NestedList(List<List<T>> inner, int count)
    {
        _inner = inner;
        _count = count;
    }

    public T this[int index]
    {
        get
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var count = _inner.Count;
            for (var i = 0; i < count; i++)
            {
                var current = _inner[i];
                if (index < current.Count)
                {
                    return current[index];
                }

                index -= current.Count;
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    public int Count => _count;

    public IEnumerator<T> GetEnumerator() => new NestedListEnumerator<T>(_inner);

    IEnumerator IEnumerable.GetEnumerator() => new NestedListEnumerator<T>(_inner);
}
