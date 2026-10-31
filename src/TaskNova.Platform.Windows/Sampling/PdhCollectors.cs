using System.Globalization;
using TaskNova.Core.Models;

namespace TaskNova.Platform.Windows.Sampling;

/// <summary>Physical disks: active time, read/write throughput and response time (PDH "PhysicalDisk").</summary>
internal sealed class DiskCollector : IDisposable
{
    private const string Idle = @"\PhysicalDisk(*)\% Idle Time";
    private const string Read = @"\PhysicalDisk(*)\Disk Read Bytes/sec";
    private const string Write = @"\PhysicalDisk(*)\Disk Write Bytes/sec";
    private const string Response = @"\PhysicalDisk(*)\Avg. Disk sec/Transfer";

    private readonly PdhQuery? _query = PdhQuery.Create(Idle, Read, Write, Response);
    private readonly List<(string, double)> _idle = [];
    private readonly List<(string, double)> _read = [];
    private readonly List<(string, double)> _write = [];
    private readonly List<(string, double)> _response = [];

    public IReadOnlyList<DiskSample> Sample()
    {
        if (_query is null || !_query.Collect())
        {
            return [];
        }

        _query.Values(Idle, _idle);
        _query.Values(Read, _read);
        _query.Values(Write, _write);
        _query.Values(Response, _response);

        var result = new List<DiskSample>();
        foreach (var (instance, idle) in _idle)
        {
            if (instance == "_Total")
            {
                continue;
            }

            // "0 C:" → id "0", name "Disk 0 (C:)"
            var space = instance.IndexOf(' ');
            var number = space > 0 ? instance[..space] : instance;
            var letters = space > 0 ? instance[(space + 1)..].Trim() : "";
            var name = letters.Length > 0 ? $"{number} ({letters})" : number;
            result.Add(new DiskSample(number, name, Math.Clamp(100 - idle, 0, 100), Find(_read, instance), Find(_write, instance),
                Find(_response, instance) * 1000));
        }

        result.Sort((a, b) => string.CompareOrdinal(a.Id.PadLeft(4), b.Id.PadLeft(4)));
        return result;
    }

    private static double Find(List<(string Instance, double Value)> list, string instance)
    {
        foreach (var (name, value) in list)
        {
            if (name == instance)
            {
                return Math.Max(0, value);
            }
        }

        return 0;
    }

    public void Dispose() => _query?.Dispose();
}

/// <summary>
/// GPU engines (PDH "GPU Engine", like the Windows Task Manager): adapter utilization = busiest engine type,
/// per-process GPU from the same instances ("pid_1234_luid_…_engtype_3D"). Dedicated/shared memory per adapter.
/// </summary>
internal sealed class GpuCollector : IDisposable
{
    private const string Engine = @"\GPU Engine(*)\Utilization Percentage";
    private const string Dedicated = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private const string Shared = @"\GPU Adapter Memory(*)\Shared Usage";

    private readonly PdhQuery? _query = PdhQuery.Create(Engine, Dedicated, Shared);
    private readonly IReadOnlyDictionary<string, DxgiAdapter> _adapters = DxgiAdapters.Read();
    private readonly List<(string, double)> _engines = [];
    private readonly List<(string, double)> _dedicated = [];
    private readonly List<(string, double)> _shared = [];
    private readonly HashSet<string> _seenActive = new(StringComparer.OrdinalIgnoreCase);

    public bool Available => _query is not null;

    public (IReadOnlyList<GpuSample> Adapters, Dictionary<int, double> PerProcess) Sample(bool perProcess)
    {
        var perPid = new Dictionary<int, double>();
        if (_query is null || !_query.Collect())
        {
            return ([], perPid);
        }

        _query.Values(Engine, _engines);
        _query.LargeValues(Dedicated, _dedicated);
        _query.LargeValues(Shared, _shared);

        // (luid, engtype) → sum, (pid, engtype) → sum
        var adapterEngines = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        var processEngines = perProcess ? new Dictionary<(int, string), double>() : null;
        foreach (var (instance, value) in _engines)
        {
            if (!TryParseEngine(instance, out var pid, out var luid, out var engineType))
            {
                continue;
            }

            if (!adapterEngines.TryGetValue(luid, out var engines))
            {
                engines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                adapterEngines[luid] = engines;
            }

            engines[engineType] = engines.GetValueOrDefault(engineType) + value;
            if (processEngines is not null && pid > 0)
            {
                processEngines[(pid, engineType)] = processEngines.GetValueOrDefault((pid, engineType)) + value;
            }
        }

        if (processEngines is not null)
        {
            foreach (var ((pid, _), value) in processEngines)
            {
                perPid[pid] = Math.Min(100, Math.Max(perPid.GetValueOrDefault(pid), value));
            }
        }

        var luids = new HashSet<string>(adapterEngines.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var (instance, _) in _dedicated)
        {
            if (TryParseAdapter(instance, out var luid))
            {
                luids.Add(luid);
            }
        }

        var result = new List<GpuSample>();
        foreach (var luid in luids)
        {
            var engines = adapterEngines.GetValueOrDefault(luid) ?? new Dictionary<string, double>();
            var utilization = engines.Count == 0 ? 0 : Math.Min(100, engines.Values.Max());
            var dedicated = SumAdapter(_dedicated, luid);
            var shared = SumAdapter(_shared, luid);
            _adapters.TryGetValue(luid, out var adapter);

            // Skip render-only/software adapters that never do anything (e.g. "Microsoft Basic Render Driver").
            if (adapter is null && dedicated == 0 && utilization <= 0 && !_seenActive.Contains(luid))
            {
                continue;
            }

            if (utilization > 0 || dedicated > 0)
            {
                _seenActive.Add(luid);
            }

            result.Add(new GpuSample(luid, adapter?.Name ?? "GPU", utilization, (ulong)dedicated, (ulong)shared,
                engines.ToDictionary(e => e.Key, e => Math.Min(100, e.Value), StringComparer.OrdinalIgnoreCase)));
        }

        result.Sort((a, b) => b.DedicatedUsedBytes.CompareTo(a.DedicatedUsedBytes));
        return (result, perPid);
    }

    /// <summary>Total dedicated memory of an adapter (for "1.2 / 8.0 GB").</summary>
    public ulong DedicatedTotal(string luid) => _adapters.TryGetValue(luid, out var adapter) ? adapter.DedicatedBytes : 0;

    private static double SumAdapter(List<(string Instance, double Value)> list, string luid)
    {
        var sum = 0d;
        foreach (var (instance, value) in list)
        {
            if (TryParseAdapter(instance, out var id) && string.Equals(id, luid, StringComparison.OrdinalIgnoreCase))
            {
                sum += Math.Max(0, value);
            }
        }

        return sum;
    }

    /// <summary>"pid_1234_luid_0x00000000_0x0000D1A5_phys_0_eng_0_engtype_3D".</summary>
    internal static bool TryParseEngine(string instance, out int pid, out string luid, out string engineType)
    {
        pid = 0;
        luid = "";
        engineType = "";
        var span = instance.AsSpan();
        if (span.StartsWith("pid_"))
        {
            var end = span[4..].IndexOf('_');
            if (end > 0)
            {
                int.TryParse(span.Slice(4, end), NumberStyles.Integer, CultureInfo.InvariantCulture, out pid);
            }
        }

        var luidStart = instance.IndexOf("luid_", StringComparison.Ordinal);
        var physStart = instance.IndexOf("_phys", StringComparison.Ordinal);
        var typeStart = instance.IndexOf("engtype_", StringComparison.Ordinal);
        if (luidStart < 0 || physStart <= luidStart || typeStart < 0)
        {
            return false;
        }

        luid = instance[(luidStart + 5)..physStart];
        engineType = instance[(typeStart + 8)..];
        return engineType.Length > 0;
    }

    /// <summary>"luid_0x00000000_0x0000D1A5_phys_0".</summary>
    internal static bool TryParseAdapter(string instance, out string luid)
    {
        luid = "";
        var luidStart = instance.IndexOf("luid_", StringComparison.Ordinal);
        var physStart = instance.IndexOf("_phys", StringComparison.Ordinal);
        if (luidStart < 0 || physStart <= luidStart)
        {
            return false;
        }

        luid = instance[(luidStart + 5)..physStart];
        return true;
    }

    public void Dispose() => _query?.Dispose();
}

/// <summary>Optional "% Processor Utility" (total and per core) and the current clock ("% Processor Performance").</summary>
internal sealed class CpuExtraCollector : IDisposable
{
    private const string UtilityAll = @"\Processor Information(*)\% Processor Utility";
    private const string Performance = @"\Processor Information(_Total)\% Processor Performance";

    private readonly PdhQuery? _query;
    private readonly List<(string, double)> _values = [];
    private readonly bool _utility;

    public CpuExtraCollector(bool utility)
    {
        _utility = utility;
        _query = utility ? PdhQuery.Create(UtilityAll, Performance) : PdhQuery.Create(Performance);
    }

    public bool WithUtility => _utility;

    /// <summary>Utility values (NaN/empty when not available) and the current clock in MHz.</summary>
    public (double Total, double[] Cores, double Mhz) Sample(int baseMhz)
    {
        if (_query is null || !_query.Collect())
        {
            return (double.NaN, [], 0);
        }

        var performance = _query.Value(Performance);
        var mhz = double.IsNaN(performance) || baseMhz <= 0 ? 0 : performance * baseMhz / 100d;
        if (!_utility || !_query.Has(UtilityAll))
        {
            return (double.NaN, [], mhz);
        }

        _query.Values(UtilityAll, _values);
        var total = double.NaN;
        var cores = new List<(int Group, int Index, double Value)>();
        foreach (var (instance, value) in _values)
        {
            if (instance == "_Total")
            {
                total = Math.Clamp(value, 0, 100);
                continue;
            }

            // "0,3" = group 0, processor 3; "0,_Total" = group total.
            var comma = instance.IndexOf(',');
            if (comma > 0 && int.TryParse(instance.AsSpan(0, comma), out var group) && int.TryParse(instance.AsSpan(comma + 1), out var index))
            {
                cores.Add((group, index, Math.Clamp(value, 0, 100)));
            }
        }

        cores.Sort((a, b) => a.Group != b.Group ? a.Group.CompareTo(b.Group) : a.Index.CompareTo(b.Index));
        return (total, cores.Select(c => c.Value).ToArray(), mhz);
    }

    public void Dispose() => _query?.Dispose();
}
