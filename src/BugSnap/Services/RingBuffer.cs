namespace BugSnap.Services;

internal sealed class RingBuffer<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;
    private readonly object _lock = new();

    public RingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new T[capacity];
    }

    public int Count
    {
        get { lock (_lock) { return _count; } }
    }

    public void Add(T item)
    {
        lock (_lock)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++;
        }
    }

    public List<T> ToList()
    {
        lock (_lock)
        {
            var result = new List<T>(_count);
            if (_count == 0) return result;

            // If buffer not yet full, items are from index 0 to _count-1 in insertion order.
            // If full, oldest is at _head, newest is at (_head - 1 + Length) % Length.
            int start = _count < _buffer.Length ? 0 : _head;
            for (int i = 0; i < _count; i++)
                result.Add(_buffer[(start + i) % _buffer.Length]);

            return result;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _head = 0;
            _count = 0;
        }
    }
}
