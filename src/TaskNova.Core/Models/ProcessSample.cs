namespace TaskNova.Core.Models;

/// <summary>
/// Identity of a process: the id alone is reused by Windows, so the creation time (FILETIME, 100 ns) is part of the key.
/// </summary>
public readonly record struct ProcessKey(int Pid, long CreateTime)
{
    public override string ToString() => $"{Pid}@{CreateTime}";
}

/// <summary>One process in one tick. A value type: one array per tick instead of hundreds of objects.</summary>
public struct ProcessSample
{
    public ProcessKey Key;

    public int ParentPid;

    /// <summary>Image name as reported by the kernel (interned per process by the sampler).</summary>
    public string Name;

    public int SessionId;

    public int BasePriority;

    public int Threads;

    public int Handles;

    /// <summary>0–100 of all logical processors.</summary>
    public double CpuPercent;

    /// <summary>Total kernel + user time (100 ns units).</summary>
    public long CpuTime100Ns;

    /// <summary>Private working set ("Memory" in the Task Manager).</summary>
    public ulong PrivateWorkingSet;

    public ulong WorkingSet;

    public ulong PeakWorkingSet;

    /// <summary>Commit charge / private bytes.</summary>
    public ulong PrivateBytes;

    public ulong VirtualSize;

    public uint PageFaults;

    public ulong IoReadBytes;

    public ulong IoWriteBytes;

    public ulong IoOtherBytes;

    /// <summary>Read + write + other bytes per second since the previous sample.</summary>
    public double IoBytesPerSec;

    /// <summary>GPU engine utilization (max over engine types), only when GPU sampling is on.</summary>
    public double GpuPercent;

    /// <summary>All threads waiting with reason "Suspended" (also suspended Store apps).</summary>
    public bool IsSuspended;
}

/// <summary>Classification for the grouped process list.</summary>
public enum ProcessGroup
{
    App,
    Background,
    Windows
}

/// <summary>Hints shown with the security indicator.</summary>
[Flags]
public enum SecurityFlags
{
    None = 0,

    /// <summary>No valid Authenticode signature.</summary>
    Unsigned = 1,

    /// <summary>Runs from %TEMP%, Downloads or a user AppData folder.</summary>
    UserWritableLocation = 2,

    /// <summary>The executable no longer exists on disk.</summary>
    FileMissing = 4,

    /// <summary>A Windows process name (svchost.exe, lsass.exe …) outside the Windows folder.</summary>
    NameMismatch = 8
}

/// <summary>Slow, once-per-process information collected in the background.</summary>
public sealed class ProcessDetails
{
    public required ProcessKey Key { get; init; }

    public string? Path { get; init; }

    public string? CommandLine { get; init; }

    public string? UserName { get; init; }

    public string? Description { get; init; }

    public string? Company { get; init; }

    /// <summary>"64-bit", "32-bit" or "ARM64"; null when unknown.</summary>
    public string? Platform { get; init; }

    /// <summary>32×32 BGRA, premultiplied; null when no icon.</summary>
    public byte[]? IconPixels { get; init; }

    public int IconSize { get; init; }

    /// <summary>Accessible at all (false: protected process or no rights).</summary>
    public bool AccessDenied { get; init; }
}

/// <summary>Result of the (later, low-priority) signature check.</summary>
public sealed record SignatureInfo(ProcessKey Key, bool Signed, string? Signer);
