namespace TaskNova.Core.Models;

/// <summary>What the sampler has to collect on the next tick (union of all visible pages and background needs).</summary>
[Flags]
public enum SampleDemand
{
    None = 0,

    /// <summary>Total CPU, kernel share, memory, uptime – always cheap (GetSystemTimes, GlobalMemoryStatusEx).</summary>
    System = 1,

    /// <summary>Per-core CPU (NtQuerySystemInformation SystemProcessorPerformanceInformation).</summary>
    Cores = 2,

    /// <summary>All processes with times, memory and I/O (one NtQuerySystemInformation call).</summary>
    Processes = 4,

    /// <summary>Processes owning a visible top-level window (Apps group).</summary>
    Windows = 8,

    /// <summary>Physical disks (PDH).</summary>
    Disks = 16,

    /// <summary>Network adapters (GetIfEntry2 through System.Net).</summary>
    Network = 32,

    /// <summary>GPU engines and dedicated memory (PDH "GPU Engine" – most expensive, sampled at most every 2 s).</summary>
    Gpu = 64,

    /// <summary>Optional "% Processor Utility" and current clock (PDH), like the Windows Task Manager.</summary>
    Utility = 128,

    All = System | Cores | Processes | Windows | Disks | Network | Gpu | Utility
}

/// <summary>Result of one sampler tick. Created on the sampler thread, read-only afterwards.</summary>
public sealed class SystemSnapshot
{
    public required DateTime TimestampUtc { get; init; }

    /// <summary>Real time since the previous tick (CPU and rate deltas refer to it).</summary>
    public required double IntervalSeconds { get; init; }

    public required SampleDemand Demand { get; init; }

    public CpuSample Cpu { get; init; } = new();

    public MemorySample Memory { get; init; } = new();

    public IReadOnlyList<DiskSample> Disks { get; init; } = [];

    public IReadOnlyList<NetworkSample> Networks { get; init; } = [];

    public IReadOnlyList<GpuSample> Gpus { get; init; } = [];

    /// <summary>Only filled when <see cref="SampleDemand.Processes"/> was requested.</summary>
    public ProcessSample[] Processes { get; init; } = [];

    /// <summary>Processes that ended since the previous process sample.</summary>
    public IReadOnlyList<ProcessKey> Exited { get; init; } = [];

    /// <summary>Process ids with a visible top-level window (only with <see cref="SampleDemand.Windows"/>).</summary>
    public IReadOnlySet<int>? WindowPids { get; init; }

    public int ProcessCount { get; init; }

    public int ThreadCount { get; init; }

    public int HandleCount { get; init; }

    public TimeSpan Uptime { get; init; }
}

public sealed class CpuSample
{
    /// <summary>Processor time 0–100 (or Processor Utility when enabled and available).</summary>
    public double TotalPercent { get; init; }

    public double KernelPercent { get; init; }

    /// <summary>Per logical processor 0–100; empty without <see cref="SampleDemand.Cores"/>.</summary>
    public double[] Cores { get; init; } = [];

    /// <summary>Current clock in MHz (PDH "% Processor Performance" × base clock); 0 = unknown.</summary>
    public double CurrentMhz { get; init; }

    /// <summary>True when <see cref="TotalPercent"/> and <see cref="Cores"/> come from "% Processor Utility".</summary>
    public bool IsUtility { get; init; }
}

public sealed class MemorySample
{
    public ulong TotalBytes { get; init; }

    public ulong AvailableBytes { get; init; }

    public ulong UsedBytes => TotalBytes > AvailableBytes ? TotalBytes - AvailableBytes : 0;

    public double UsedPercent => TotalBytes == 0 ? 0 : UsedBytes * 100d / TotalBytes;

    public ulong CommitBytes { get; init; }

    public ulong CommitLimitBytes { get; init; }

    public ulong CacheBytes { get; init; }

    public ulong PagedPoolBytes { get; init; }

    public ulong NonPagedPoolBytes { get; init; }
}

public sealed record DiskSample(string Id, string Name, double ActivePercent, double ReadBytesPerSec, double WriteBytesPerSec, double AvgResponseMs);

public sealed record NetworkSample(string Id, string Name, string Description, string Kind, double SendBytesPerSec, double ReceiveBytesPerSec,
    long LinkSpeedBitsPerSec, string? IPv4);

public sealed record GpuSample(string Id, string Name, double UtilizationPercent, ulong DedicatedUsedBytes, ulong SharedUsedBytes,
    IReadOnlyDictionary<string, double> Engines);
