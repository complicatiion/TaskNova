namespace TaskNova.Core.Metrics;

/// <summary>
/// Fixed-size history of values for the live charts. Adding never allocates; the oldest value is overwritten.
/// Not thread-safe: written and read on the UI thread only.
/// </summary>
public sealed class RingBuffer
{
    private double[] _values;
    private int _start;

    public RingBuffer(int capacity)
    {
        if (capacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _values = new double[capacity];
    }

    public int Capacity => _values.Length;

    public int Count { get; private set; }

    /// <summary>Incremented on every change: charts redraw only when it differs from what they drew.</summary>
    public long Version { get; private set; }

    /// <summary>Newest value (0 when empty).</summary>
    public double Last => Count == 0 ? 0 : this[Count - 1];

    /// <summary>Value by age order: 0 = oldest, Count-1 = newest.</summary>
    public double this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _values[(_start + index) % _values.Length];
        }
    }

    public void Add(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            value = 0;
        }

        if (Count < _values.Length)
        {
            _values[(_start + Count) % _values.Length] = value;
            Count++;
        }
        else
        {
            _values[_start] = value;
            _start = (_start + 1) % _values.Length;
        }

        Version++;
    }

    public double Max()
    {
        var max = 0d;
        for (var i = 0; i < Count; i++)
        {
            var v = this[i];
            if (v > max)
            {
                max = v;
            }
        }

        return max;
    }

    /// <summary>Changes the capacity and keeps the newest values.</summary>
    public void Resize(int capacity)
    {
        if (capacity < 2 || capacity == _values.Length)
        {
            return;
        }

        var keep = Math.Min(Count, capacity);
        var values = new double[capacity];
        for (var i = 0; i < keep; i++)
        {
            values[i] = this[Count - keep + i];
        }

        _values = values;
        _start = 0;
        Count = keep;
        Version++;
    }

    public void Clear()
    {
        _start = 0;
        Count = 0;
        Version++;
    }
}
