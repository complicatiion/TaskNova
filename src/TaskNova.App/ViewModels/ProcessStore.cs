using Avalonia.Threading;
using TaskNova.Core.Classification;
using TaskNova.Core.History;
using TaskNova.Core.Models;
using TaskNova.Platform.Windows;

namespace TaskNova.App.ViewModels;

/// <summary>
/// All process rows, keyed by (PID, creation time). Applies process snapshots, queues new processes for the background
/// details lookup and picks up its results. Pages build their lists from <see cref="Rows"/>.
/// </summary>
public sealed class ProcessStore : IDisposable
{
    private readonly Dictionary<ProcessKey, ProcessRow> _rows = new();
    private readonly HashSet<ProcessKey> _seen = new();
    private readonly ProcessEnricher _enricher;
    private readonly AppHistoryStore _history;
    private readonly DispatcherTimer _drainTimer;
    private readonly string _windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private readonly string _tempDirectory = Path.GetTempPath();
    private readonly string _userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private HashSet<int> _windowPids = [];

    public ProcessStore(ProcessEnricher enricher, AppHistoryStore history)
    {
        _enricher = enricher;
        _history = history;

        // Details arrive faster than the sample interval at startup: pick them up twice a second.
        _drainTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Drain());
        _drainTimer.Start();
    }

    public IReadOnlyCollection<ProcessRow> Rows => _rows.Values;

    /// <summary>Incremented when processes start or end (lists rebuild their structure).</summary>
    public int StructureVersion { get; private set; }

    /// <summary>Incremented when details arrive (grouping, names and icons may change).</summary>
    public int DetailsVersion { get; private set; }

    public bool HasData { get; private set; }

    public ulong TotalMemory { get; private set; }

    /// <summary>Raised after every applied process snapshot.</summary>
    public event Action? Updated;

    public ProcessRow? Find(ProcessKey key) => _rows.GetValueOrDefault(key);

    public ProcessRow? FindByPid(int pid) => _rows.Values.FirstOrDefault(r => r.Pid == pid);

    public void Apply(SystemSnapshot snapshot)
    {
        if (!snapshot.Demand.HasFlag(SampleDemand.Processes) || snapshot.Processes.Length == 0)
        {
            return;
        }

        TotalMemory = snapshot.Memory.TotalBytes;
        if (snapshot.WindowPids is { } windowPids)
        {
            _windowPids = windowPids.ToHashSet();
        }

        _seen.Clear();
        var changed = false;
        foreach (ref readonly var sample in snapshot.Processes.AsSpan())
        {
            _seen.Add(sample.Key);
            if (!_rows.TryGetValue(sample.Key, out var row))
            {
                row = new ProcessRow(sample.Key, sample.Name);
                row.Group = ProcessClassifier.Classify(row.Name, null, _windowPids.Contains(row.Pid), _windowsDirectory);
                _rows[sample.Key] = row;
                _enricher.Enqueue(sample.Key, sample.Name);
                changed = true;
            }

            row.Update(sample, TotalMemory);
        }

        if (_rows.Count != _seen.Count)
        {
            foreach (var key in _rows.Keys.Where(k => !_seen.Contains(k)).ToList())
            {
                _rows.Remove(key);
                changed = true;
            }
        }

        if (snapshot.WindowPids is not null)
        {
            foreach (var row in _rows.Values)
            {
                var hasWindow = _windowPids.Contains(row.Pid);
                if (row.HasWindow != hasWindow)
                {
                    row.HasWindow = hasWindow;
                    Regroup(row);
                    changed = true;
                }
            }
        }

        Drain();
        if (changed)
        {
            StructureVersion++;
        }

        HasData = true;
        Updated?.Invoke();
    }

    private void Drain()
    {
        var any = false;
        while (_enricher.TryTakeDetails(out var details))
        {
            if (_rows.TryGetValue(details.Key, out var row))
            {
                row.ApplyDetails(details);
                Regroup(row);
                row.Security = ProcessClassifier.Inspect(row.Name, row.Path, row.Path is null || File.Exists(row.Path), _windowsDirectory,
                    _tempDirectory, _userProfile);
                if (row.Path is not null || row.Description is not null)
                {
                    _history.Describe(row.Name, row.Path, row.Description);
                }

                any = true;
            }
        }

        while (_enricher.TryTakeSignature(out var signature))
        {
            if (_rows.TryGetValue(signature.Key, out var row))
            {
                row.Signed = signature.Signed;
                row.Signer = signature.Signer;
                row.Security = signature.Signed ? row.Security & ~SecurityFlags.Unsigned : row.Security | SecurityFlags.Unsigned;
            }
        }

        if (any)
        {
            DetailsVersion++;
        }
    }

    private void Regroup(ProcessRow row)
    {
        row.Group = ProcessClassifier.Classify(row.Name, row.Path, row.HasWindow, _windowsDirectory);
    }

    /// <summary>Window hidden: the details timer stops (details are still picked up with every process snapshot).</summary>
    public void SetActive(bool active)
    {
        if (active)
        {
            _drainTimer.Start();
        }
        else
        {
            _drainTimer.Stop();
        }
    }

    /// <summary>(key, parent) pairs for "end process tree".</summary>
    public IReadOnlyList<(ProcessKey Key, int ParentPid)> Tree() => _rows.Values.Select(r => (r.Key, r.ParentPid)).ToList();

    public void Dispose()
    {
        _drainTimer.Stop();
    }
}
