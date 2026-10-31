using System.Text.Json;
using TaskNova.Core.Storage;

namespace TaskNova.Core.History;

/// <summary>Accumulated usage of one program (by image name) since the history was started or cleared.</summary>
public sealed class AppHistoryEntry
{
    public string Name { get; set; } = "";

    public string? Path { get; set; }

    public string? Description { get; set; }

    /// <summary>Kernel + user time in 100 ns units.</summary>
    public long CpuTime100Ns { get; set; }

    public ulong IoBytes { get; set; }

    /// <summary>Seconds the program was seen running (sum of sample intervals).</summary>
    public double RunSeconds { get; set; }

    public DateTime FirstSeenUtc { get; set; }

    public DateTime LastSeenUtc { get; set; }
}

public sealed class AppHistoryFile
{
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    public List<AppHistoryEntry> Entries { get; set; } = [];
}

/// <summary>
/// TaskNova's own app history (the Windows SRUM database is locked and not used): CPU time and I/O per program,
/// recorded from the process deltas while TaskNova runs. Written by the sampler thread, read by the UI.
/// </summary>
public sealed class AppHistoryStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, AppHistoryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;
    private bool _dirty;

    public AppHistoryStore(string? path = null)
    {
        _path = path ?? AppPaths.AppHistoryFile;
    }

    public DateTime StartedUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>Adds the usage of one process for one sample interval.</summary>
    public void Add(string name, long cpuDelta100Ns, ulong ioDelta, double seconds, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(name) || (cpuDelta100Ns <= 0 && ioDelta == 0 && seconds <= 0))
        {
            return;
        }

        lock (_lock)
        {
            if (!_entries.TryGetValue(name, out var entry))
            {
                entry = new AppHistoryEntry { Name = name, FirstSeenUtc = nowUtc };
                _entries[name] = entry;
            }

            entry.CpuTime100Ns += Math.Max(0, cpuDelta100Ns);
            entry.IoBytes += ioDelta;
            entry.RunSeconds += Math.Max(0, seconds);
            entry.LastSeenUtc = nowUtc;
            _dirty = true;
        }
    }

    /// <summary>Path and description once the background lookup knows them.</summary>
    public void Describe(string name, string? path, string? description)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(name, out var entry))
            {
                entry.Path ??= path;
                entry.Description ??= description;
            }
        }
    }

    /// <summary>Copy for the UI.</summary>
    public IReadOnlyList<AppHistoryEntry> Snapshot()
    {
        lock (_lock)
        {
            return _entries.Values.Select(e => new AppHistoryEntry
            {
                Name = e.Name,
                Path = e.Path,
                Description = e.Description,
                CpuTime100Ns = e.CpuTime100Ns,
                IoBytes = e.IoBytes,
                RunSeconds = e.RunSeconds,
                FirstSeenUtc = e.FirstSeenUtc,
                LastSeenUtc = e.LastSeenUtc
            }).ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            StartedUtc = DateTime.UtcNow;
            _dirty = true;
        }

        Save();
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var file = JsonSerializer.Deserialize(File.ReadAllText(_path), StorageJsonContext.Default.AppHistoryFile);
            if (file is null)
            {
                return;
            }

            lock (_lock)
            {
                _entries.Clear();
                StartedUtc = file.StartedUtc;
                foreach (var entry in file.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
                {
                    _entries[entry.Name] = entry;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Broken file: start a new history.
        }
    }

    /// <summary>Writes only when something changed since the last save.</summary>
    public void Save()
    {
        AppHistoryFile file;
        lock (_lock)
        {
            if (!_dirty)
            {
                return;
            }

            file = new AppHistoryFile { StartedUtc = StartedUtc, Entries = _entries.Values.OrderByDescending(e => e.CpuTime100Ns).ToList() };
            _dirty = false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(file, StorageJsonContext.Default.AppHistoryFile));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_lock)
            {
                _dirty = true;
            }
        }
    }
}
