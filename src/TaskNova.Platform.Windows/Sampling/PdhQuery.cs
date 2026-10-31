using TaskNova.Platform.Windows.Native;

namespace TaskNova.Platform.Windows.Sampling;

/// <summary>
/// Small wrapper around one PDH query with English counter paths (independent of the Windows display language).
/// Wildcard counters return all instances in one call. Rate counters need two collections – the first one is empty.
/// </summary>
internal sealed unsafe class PdhQuery : IDisposable
{
    private readonly nint _query;
    private readonly Dictionary<string, nint> _counters = new(StringComparer.Ordinal);
    private byte[] _buffer = new byte[16 * 1024];

    private PdhQuery(nint query)
    {
        _query = query;
    }

    /// <summary>Null when PDH is not available or none of the counters exists on this system.</summary>
    public static PdhQuery? Create(params string[] paths)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            if (Pdh.PdhOpenQuery(null, 0, out var handle) != 0)
            {
                return null;
            }

            var query = new PdhQuery(handle);
            foreach (var path in paths)
            {
                if (Pdh.PdhAddEnglishCounter(handle, path, 0, out var counter) == 0)
                {
                    query._counters[path] = counter;
                }
            }

            if (query._counters.Count == 0)
            {
                query.Dispose();
                return null;
            }

            Pdh.PdhCollectQueryData(handle);
            return query;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    public bool Has(string path) => _counters.ContainsKey(path);

    public bool Collect() => Pdh.PdhCollectQueryData(_query) == 0;

    /// <summary>Single value; NaN when not valid (yet).</summary>
    public double Value(string path, bool noCap = true)
    {
        if (!_counters.TryGetValue(path, out var counter))
        {
            return double.NaN;
        }

        var format = Pdh.FmtDouble | (noCap ? Pdh.FmtNoCap100 : 0);
        return Pdh.PdhGetFormattedCounterValue(counter, format, out _, out var value) == 0
               && value.CStatus is Pdh.CStatusValidData or Pdh.CStatusNewData
            ? value.DoubleValue
            : double.NaN;
    }

    /// <summary>All instances of a wildcard counter as (instance, value); invalid instances are skipped.</summary>
    public void Values(string path, List<(string Instance, double Value)> result)
    {
        result.Clear();
        if (!_counters.TryGetValue(path, out var counter))
        {
            return;
        }

        const uint format = Pdh.FmtDouble | Pdh.FmtNoCap100;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var size = (uint)_buffer.Length;
            uint count;
            uint status;
            fixed (byte* p = _buffer)
            {
                status = Pdh.PdhGetFormattedCounterArray(counter, format, ref size, out count, p);
                if (status == 0)
                {
                    var items = (PDH_FMT_COUNTERVALUE_ITEM_W*)p;
                    for (var i = 0; i < count; i++)
                    {
                        var item = items[i];
                        if (item.FmtValue.CStatus is Pdh.CStatusValidData or Pdh.CStatusNewData && item.szName != null)
                        {
                            result.Add((new string(item.szName), item.FmtValue.DoubleValue));
                        }
                    }

                    return;
                }
            }

            if (status != Pdh.MoreData)
            {
                return;
            }

            _buffer = new byte[Math.Max(size + 1024, _buffer.Length * 2)];
        }
    }

    /// <summary>Large integer instances (memory counters in bytes).</summary>
    public void LargeValues(string path, List<(string Instance, double Value)> result)
    {
        result.Clear();
        if (!_counters.TryGetValue(path, out var counter))
        {
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var size = (uint)_buffer.Length;
            uint count;
            uint status;
            fixed (byte* p = _buffer)
            {
                status = Pdh.PdhGetFormattedCounterArray(counter, Pdh.FmtLarge, ref size, out count, p);
                if (status == 0)
                {
                    var items = (PDH_FMT_COUNTERVALUE_ITEM_LARGE_W*)p;
                    for (var i = 0; i < count; i++)
                    {
                        var item = items[i];
                        if (item.FmtValue.CStatus is Pdh.CStatusValidData or Pdh.CStatusNewData && item.szName != null)
                        {
                            result.Add((new string(item.szName), item.FmtValue.LargeValue));
                        }
                    }

                    return;
                }
            }

            if (status != Pdh.MoreData)
            {
                return;
            }

            _buffer = new byte[Math.Max(size + 1024, _buffer.Length * 2)];
        }
    }

    public void Dispose()
    {
        if (_query != 0)
        {
            Pdh.PdhCloseQuery(_query);
        }
    }
}
