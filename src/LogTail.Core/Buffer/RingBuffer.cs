using System.Collections;

namespace LogTail.Core.Buffer;

/// <summary>
/// A bounded ring buffer that auto-grows on overflow up to a configured maximum capacity.
/// <para>
/// Backed by a single array; oldest entries are evicted when the buffer fills, but if
/// <c>Capacity &lt; MaxCapacity</c> the buffer doubles in size instead of evicting so callers
/// can preserve the entire tail for search/filter operations.
/// </para>
/// <para>
/// All mutating and reading operations are thread-safe. Enumeration snapshots the buffer
/// once under the lock, so an in-flight <see cref="Add"/> that triggers a grow does not
/// invalidate an active enumerator.
/// </para>
/// </summary>
public sealed class RingBuffer<T> : IReadOnlyList<T>
{
    private readonly Lock _gate = new();
    private T[] _buffer;
    private int _head;    // index of oldest item
    private int _count;

    /// <summary>
    /// Creates a new ring buffer with auto-grow disabled (capped at <paramref name="capacity"/>).
    /// </summary>
    public RingBuffer(int capacity)
        : this(capacity, capacity, growFactor: 2.0)
    {
    }

    /// <summary>
    /// Creates a new ring buffer that starts at <paramref name="initialCapacity"/> and may
    /// grow up to <paramref name="maxCapacity"/> when the buffer would otherwise evict
    /// the oldest entry. Growth multiplies the current capacity by
    /// <paramref name="growFactor"/>, clamped to <paramref name="maxCapacity"/>.
    /// </summary>
    /// <param name="initialCapacity">Starting capacity. Must be &gt; 0.</param>
    /// <param name="maxCapacity">Hard cap on growth. Must be &gt;= <paramref name="initialCapacity"/>.</param>
    /// <param name="growFactor">Multiplier applied per grow. Must be &gt;= 1.0.</param>
    public RingBuffer(int initialCapacity, int maxCapacity, double growFactor = 2.0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);
        if (maxCapacity < initialCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCapacity),
                $"maxCapacity ({maxCapacity}) must be >= initialCapacity ({initialCapacity}).");
        }

        if (growFactor < 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(growFactor),
                $"growFactor must be >= 1.0, got {growFactor}.");
        }

        _buffer = new T[initialCapacity];
        Capacity = initialCapacity;
        MaxCapacity = maxCapacity;
        GrowFactor = growFactor;
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// Raised when the buffer grows to accommodate new items. Subscribers receive the new
    /// capacity. This event fires under the internal lock; keep handlers short and avoid
    /// re-entrant calls to this buffer.
    /// </summary>
    public event EventHandler<int>? Grew;

    /// <summary>Current capacity of the buffer (may increase over time as the buffer grows).</summary>
    public int Capacity { get; private set; }

    /// <summary>Maximum capacity the buffer will grow to. Once reached, oldest items are evicted.</summary>
    public int MaxCapacity { get; }

    /// <summary>Multiplier applied per grow operation. Always &gt;= 1.0.</summary>
    public double GrowFactor { get; }

    /// <summary>True when <see cref="Capacity"/> equals <see cref="MaxCapacity"/>.</summary>
    public bool IsAtMaxCapacity
    {
        get
        {
            lock (_gate)
            {
                return Capacity == MaxCapacity;
            }
        }
    }

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
        EventHandler<int>? grewHandler;
        int newCapacity;

        lock (_gate)
        {
            // Buffer not yet full: simple append.
            if (_count < Capacity)
            {
                var writeIndex = (_head + _count) % Capacity;
                _buffer[writeIndex] = item;
                _count++;
                return;
            }

            // Buffer at current capacity: try to grow, otherwise evict.
            if (Capacity < MaxCapacity)
            {
                newCapacity = ComputeGrowCapacity(Capacity, MaxCapacity, GrowFactor);
                GrowInternal(newCapacity);
                grewHandler = Grew;
            }
            else
            {
                grewHandler = null;
                newCapacity = Capacity;
            }

            // Append the new item; if we just grew, the tail slot is empty. If we
            // couldn't grow (already at max), overwrite the oldest and advance head.
            var slot = (_head + _count) % Capacity;
            _buffer[slot] = item;
            if (Capacity == newCapacity && _count == Capacity)
            {
                // Still at max, drop oldest.
                _head = (_head + 1) % Capacity;
            }
            else
            {
                _count++;
            }
        }

        // Fire event outside the lock so subscribers can safely call back into the
        // buffer (e.g. for inspection) without deadlocking.
        grewHandler?.Invoke(this, newCapacity);
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
        int count;
        int capacity;

        lock (_gate)
        {
            count = _count;
            capacity = Capacity;
            snapshot = new T[count];
            for (int i = 0; i < count; i++)
            {
                snapshot[i] = _buffer[(_head + i) % capacity];
            }
        }

        foreach (var item in snapshot)
        {
            yield return item;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Replaces the backing array with one of <paramref name="newCapacity"/>, preserving all
    /// items in oldest-to-newest order. Caller must hold <see cref="_gate"/>.
    /// </summary>
    private void GrowInternal(int newCapacity)
    {
        var newBuffer = new T[newCapacity];

        // Copy in order: oldest at index 0, newest at index _count-1.
        if (_count > 0)
        {
            var firstChunk = Math.Min(_count, Capacity - _head);
            var secondChunk = _count - firstChunk;
            Array.Copy(_buffer, _head, newBuffer, 0, firstChunk);
            if (secondChunk > 0)
            {
                Array.Copy(_buffer, 0, newBuffer, firstChunk, secondChunk);
            }
        }

        _buffer = newBuffer;
        _head = 0;
        Capacity = newCapacity;
    }

    /// <summary>
    /// Computes the next capacity when growing. Uses banker-style ceiling to avoid floating
    /// point surprises, and clamps to <paramref name="maxCapacity"/>.
    /// </summary>
    private static int ComputeGrowCapacity(int current, int max, double factor)
    {
        var raw = (long)Math.Ceiling(current * factor);
        if (raw < current + 1)
        {
            raw = (long)current + 1; // ensure strict increase even with factor=1.0
        }

        return (int)Math.Min(raw, max);
    }
}
