using System.Collections;

namespace LogTail.Core.Buffer;

public sealed class RingBuffer<T> : IReadOnlyList<T>
{
    private readonly T[] _buffer;
    private readonly Lock _gate = new();
    private int _head;    // index of oldest item
    private int _count;

    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _buffer = new T[capacity];
        Capacity = capacity;
        _head = 0;
        _count = 0;
    }

    public int Capacity { get; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    public T this[int index]
    {
        get
        {
            lock (_gate)
            {
                if ((uint)index >= (uint)_count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _buffer[(_head + index) % Capacity];
            }
        }
    }

    public void Add(T item)
    {
        lock (_gate)
        {
            var writeIndex = (_head + _count) % Capacity;

            if (_count == Capacity)
            {
                // Evict oldest — advance head
                _buffer[writeIndex] = item;
                _head = (_head + 1) % Capacity;
            }
            else
            {
                _buffer[writeIndex] = item;
                _count++;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_buffer);
            _head = 0;
            _count = 0;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        T[] snapshot;
        lock (_gate)
        {
            snapshot = new T[_count];
            for (int i = 0; i < _count; i++)
            {
                snapshot[i] = _buffer[(_head + i) % Capacity];
            }
        }

        foreach (var item in snapshot)
        {
            yield return item;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
