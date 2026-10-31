using System.Diagnostics;
using System.Runtime.InteropServices;
using TaskNova.Core.History;
using TaskNova.Core.Metrics;
using TaskNova.Core.Models;
using TaskNova.Platform.Windows.Native;
using TaskNova.Platform.Windows.Sampling;

namespace TaskNova.Platform.Windows;

/// <summary>
/// Collects one <see cref="SystemSnapshot"/> per tick on the caller's (background) thread. Everything a tick needs comes
/// from a handful of native calls:
/// <list type="bullet">
/// <item>GetSystemTimes / GlobalMemoryStatusEx / GetPerformanceInfo – system totals</item>
/// <item>NtQuerySystemInformation(SystemProcessInformation) – all processes and threads in one call</item>
/// <item>NtQuerySystemInformation(SystemProcessorPerformanceInformation) – per-core times</item>
/// <item>PDH – disks, GPU engines and the optional processor utility</item>
/// </list>
/// The process buffer is unmanaged memory reused across ticks (no Large Object Heap churn). Not thread-safe:
/// one sampler per sampling thread.
/// </summary>
public sealed unsafe class SystemSampler : IDisposable
{
    private sealed class ProcessState
    {
        public long CreateTime;
        public required string Name;
        public long CpuTime;
        public ulong IoTotal;
        public int Generation;
    }

    private readonly AppHistoryStore? _history;
    private readonly Dictionary<int, ProcessState> _processes = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly NetworkCollector _network = new();
    private readonly int _baseMhz;

    private byte* _buffer;
    private nuint _bufferSize;
    private ProcessSample[] _scratch = new ProcessSample[512];
    private int _generation;

    private bool _haveSystemTimes;
    private long _idle;
    private long _kernel;
    private long _user;
    private double _lastTickSeconds = -1;
    private double _lastProcessSeconds = -1;
    private SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] _cores = [];
    private SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] _coresNext = [];
    private bool _haveCores;
    private double _lastGpuSeconds = -10;
    private IReadOnlyList<GpuSample> _lastGpus = [];
    private Dictionary<int, double> _lastGpuPerProcess = new();

    private DiskCollector? _disks;
    private GpuCollector? _gpu;
    private CpuExtraCollector? _cpuExtra;

    public SystemSampler(AppHistoryStore? history, int baseMhz)
    {
        _history = history;
        _baseMhz = baseMhz;
        LogicalProcessors = StaticInfoReader.LogicalProcessorCount();
    }

    public int LogicalProcessors { get; }

    /// <summary>Use "% Processor Utility" for CPU (set from the settings; takes effect on the next tick).</summary>
    public bool UseProcessorUtility { get; set; }

    /// <summary>Record the app history (only while process sampling runs).</summary>
    public bool RecordHistory { get; set; } = true;

    /// <summary>GPU sampling allowed (setting). The demand flag still decides per tick.</summary>
    public bool GpuEnabled { get; set; } = true;

    /// <summary>True after the first GPU sample found at least one adapter.</summary>
    public bool HasGpu { get; private set; }

    public SystemSnapshot Sample(SampleDemand demand)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var interval = _lastTickSeconds < 0 ? 0 : now - _lastTickSeconds;
        _lastTickSeconds = now;

        var cpu = SampleCpu(demand);
        var memory = SampleMemory(out var perf);

        ProcessSample[] processes = [];
        IReadOnlyList<ProcessKey> exited = [];
        if (demand.HasFlag(SampleDemand.Processes))
        {
            (processes, exited) = SampleProcesses(now);
        }

        IReadOnlyList<GpuSample> gpus = [];
        if (demand.HasFlag(SampleDemand.Gpu) && GpuEnabled)
        {
            // The GPU engine counter has hundreds of instances: at most every 2 s, values in between are repeated.
            if (now - _lastGpuSeconds >= 1.9)
            {
                _gpu ??= new GpuCollector();
                var (adapters, perProcess) = _gpu.Sample(demand.HasFlag(SampleDemand.Processes));
                _lastGpus = adapters;
                _lastGpuPerProcess = perProcess;
                _lastGpuSeconds = now;
                HasGpu = adapters.Count > 0;
            }

            gpus = _lastGpus;
            if (_lastGpuPerProcess.Count > 0)
            {
                for (var i = 0; i < processes.Length; i++)
                {
                    if (_lastGpuPerProcess.TryGetValue(processes[i].Key.Pid, out var gpu))
                    {
                        processes[i].GpuPercent = gpu;
                    }
                }
            }
        }

        IReadOnlyList<DiskSample> disks = [];
        if (demand.HasFlag(SampleDemand.Disks))
        {
            _disks ??= new DiskCollector();
            disks = _disks.Sample();
        }

        IReadOnlyList<NetworkSample> networks = [];
        if (demand.HasFlag(SampleDemand.Network))
        {
            networks = _network.Sample(interval);
        }

        return new SystemSnapshot
        {
            TimestampUtc = DateTime.UtcNow,
            IntervalSeconds = interval,
            Demand = demand,
            Cpu = cpu,
            Memory = memory,
            Disks = disks,
            Networks = networks,
            Gpus = gpus,
            Processes = processes,
            Exited = exited,
            WindowPids = demand.HasFlag(SampleDemand.Windows) ? WindowEnumerator.Collect() : null,
            ProcessCount = (int)perf.ProcessCount,
            ThreadCount = (int)perf.ThreadCount,
            HandleCount = (int)perf.HandleCount,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64)
        };
    }

    /// <summary>The sum of all GPU dedicated memory of an adapter (0 = unknown).</summary>
    public ulong GpuDedicatedTotal(string luid) => _gpu?.DedicatedTotal(luid) ?? 0;

    // ------------------------------------------------------------------------------------------------ CPU

    private CpuSample SampleCpu(SampleDemand demand)
    {
        double total = 0;
        double kernel = 0;
        if (Kernel32.GetSystemTimes(out var idle, out var kernelTime, out var user))
        {
            if (_haveSystemTimes)
            {
                (total, kernel) = CpuMath.SystemUsage(idle - _idle, kernelTime - _kernel, user - _user);
            }

            _idle = idle;
            _kernel = kernelTime;
            _user = user;
            _haveSystemTimes = true;
        }

        var cores = demand.HasFlag(SampleDemand.Cores) ? SampleCores() : [];

        // Optional collectors: processor utility (setting) and clock (performance page).
        var mhz = 0d;
        var isUtility = false;
        var wantExtra = demand.HasFlag(SampleDemand.Utility) || UseProcessorUtility;
        if (wantExtra)
        {
            if (_cpuExtra is null || _cpuExtra.WithUtility != UseProcessorUtility)
            {
                _cpuExtra?.Dispose();
                _cpuExtra = new CpuExtraCollector(UseProcessorUtility);
            }

            var (utilityTotal, utilityCores, clock) = _cpuExtra.Sample(_baseMhz);
            mhz = clock;
            if (UseProcessorUtility && !double.IsNaN(utilityTotal))
            {
                total = utilityTotal;
                isUtility = true;
                if (cores.Length > 0 && utilityCores.Length == cores.Length)
                {
                    cores = utilityCores;
                }
            }
        }
        else if (_cpuExtra is not null)
        {
            _cpuExtra.Dispose();
            _cpuExtra = null;
        }

        return new CpuSample { TotalPercent = total, KernelPercent = Math.Min(kernel, total), Cores = cores, CurrentMhz = mhz, IsUtility = isUtility };
    }

    /// <summary>Per logical processor of the current processor group (up to 64).</summary>
    private double[] SampleCores()
    {
        var count = Math.Min(LogicalProcessors, 64);
        if (_coresNext.Length != count)
        {
            _coresNext = new SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[count];
            _cores = new SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[count];
            _haveCores = false;
        }

        uint returned;
        fixed (SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION* p = _coresNext)
        {
            var size = (uint)(sizeof(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION) * count);
            if (Ntdll.NtQuerySystemInformation(Ntdll.SystemProcessorPerformanceInformation, p, size, out returned) < 0)
            {
                return [];
            }
        }

        var available = (int)(returned / (uint)sizeof(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION));
        var result = new double[available];
        if (_haveCores)
        {
            for (var i = 0; i < available; i++)
            {
                result[i] = CpuMath.CoreUsage(
                    _coresNext[i].IdleTime - _cores[i].IdleTime,
                    _coresNext[i].KernelTime - _cores[i].KernelTime,
                    _coresNext[i].UserTime - _cores[i].UserTime);
            }
        }

        (_cores, _coresNext) = (_coresNext, _cores);
        _haveCores = true;
        return result;
    }

    // ------------------------------------------------------------------------------------------------ Memory

    private static MemorySample SampleMemory(out PERFORMANCE_INFORMATION perf)
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)sizeof(MEMORYSTATUSEX) };
        Kernel32.GlobalMemoryStatusEx(ref status);
        perf = new PERFORMANCE_INFORMATION { cb = (uint)sizeof(PERFORMANCE_INFORMATION) };
        if (!Kernel32.GetPerformanceInfo(ref perf, perf.cb))
        {
            perf = default;
        }

        var page = (ulong)perf.PageSize;
        return new MemorySample
        {
            TotalBytes = status.ullTotalPhys,
            AvailableBytes = status.ullAvailPhys,
            CommitBytes = (ulong)perf.CommitTotal * page,
            CommitLimitBytes = (ulong)perf.CommitLimit * page,
            CacheBytes = (ulong)perf.SystemCache * page,
            PagedPoolBytes = (ulong)perf.KernelPaged * page,
            NonPagedPoolBytes = (ulong)perf.KernelNonpaged * page
        };
    }

    // ------------------------------------------------------------------------------------------------ Processes

    private bool QueryProcesses()
    {
        if (_buffer == null)
        {
            _bufferSize = 512 * 1024;
            _buffer = (byte*)NativeMemory.Alloc(_bufferSize);
        }

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var status = Ntdll.NtQuerySystemInformation(Ntdll.SystemProcessInformation, _buffer, (uint)_bufferSize, out var needed);
            if (status >= 0)
            {
                return true;
            }

            if (status != Ntdll.StatusInfoLengthMismatch && status != Ntdll.StatusBufferTooSmall)
            {
                return false;
            }

            // New processes may appear between the calls: grow with headroom.
            _bufferSize = Math.Max(_bufferSize * 3 / 2, (nuint)needed + 64 * 1024);
            _buffer = (byte*)NativeMemory.Realloc(_buffer, _bufferSize);
        }

        return false;
    }

    private (ProcessSample[] Processes, IReadOnlyList<ProcessKey> Exited) SampleProcesses(double now)
    {
        if (!QueryProcesses())
        {
            return ([], []);
        }

        var elapsed = _lastProcessSeconds < 0 ? 0 : now - _lastProcessSeconds;
        _lastProcessSeconds = now;
        var generation = ++_generation;
        var nowUtc = DateTime.UtcNow;
        var record = RecordHistory && _history is not null && elapsed > 0;
        var threadParsing = sizeof(SYSTEM_PROCESS_INFORMATION) == 256 && sizeof(SYSTEM_THREAD_INFORMATION) == 80;
        var count = 0;

        var p = _buffer;
        while (true)
        {
            var info = (SYSTEM_PROCESS_INFORMATION*)p;
            var pid = (int)info->UniqueProcessId;
            var cpuTime = info->KernelTime + info->UserTime;
            var ioTotal = (ulong)(info->ReadTransferCount + info->WriteTransferCount + info->OtherTransferCount);

            if (!_processes.TryGetValue(pid, out var state) || state.CreateTime != info->CreateTime)
            {
                state = new ProcessState
                {
                    CreateTime = info->CreateTime,
                    Name = info->ImageName.ToStringOrNull() ?? (pid == 0 ? "System Idle Process" : "System"),
                    CpuTime = cpuTime,
                    IoTotal = ioTotal
                };
                _processes[pid] = state;
            }

            var cpuDelta = Math.Max(0, cpuTime - state.CpuTime);
            var ioDelta = ioTotal >= state.IoTotal ? ioTotal - state.IoTotal : 0;
            state.CpuTime = cpuTime;
            state.IoTotal = ioTotal;
            state.Generation = generation;

            if (count == _scratch.Length)
            {
                Array.Resize(ref _scratch, _scratch.Length * 2);
            }

            ref var sample = ref _scratch[count++];
            sample.Key = new ProcessKey(pid, info->CreateTime);
            sample.ParentPid = (int)info->InheritedFromUniqueProcessId;
            sample.Name = state.Name;
            sample.SessionId = (int)info->SessionId;
            sample.BasePriority = info->BasePriority;
            sample.Threads = (int)info->NumberOfThreads;
            sample.Handles = (int)info->HandleCount;
            sample.CpuPercent = CpuMath.ProcessUsage(cpuDelta, elapsed, LogicalProcessors);
            sample.CpuTime100Ns = cpuTime;
            sample.PrivateWorkingSet = (ulong)Math.Max(0, info->WorkingSetPrivateSize);
            sample.WorkingSet = info->WorkingSetSize;
            sample.PeakWorkingSet = info->PeakWorkingSetSize;
            sample.PrivateBytes = info->PrivatePageCount;
            sample.VirtualSize = info->VirtualSize;
            sample.PageFaults = info->PageFaultCount;
            sample.IoReadBytes = (ulong)info->ReadTransferCount;
            sample.IoWriteBytes = (ulong)info->WriteTransferCount;
            sample.IoOtherBytes = (ulong)info->OtherTransferCount;
            sample.IoBytesPerSec = elapsed > 0 ? ioDelta / elapsed : 0;
            sample.GpuPercent = 0;
            sample.IsSuspended = threadParsing && pid > 4 && IsSuspended(info);

            if (record && pid != 0)
            {
                _history!.Add(state.Name, cpuDelta, ioDelta, elapsed, nowUtc);
            }

            if (info->NextEntryOffset == 0)
            {
                break;
            }

            p += info->NextEntryOffset;
        }

        List<ProcessKey>? exited = null;
        foreach (var (pid, state) in _processes)
        {
            if (state.Generation != generation)
            {
                (exited ??= []).Add(new ProcessKey(pid, state.CreateTime));
            }
        }

        if (exited is not null)
        {
            foreach (var key in exited)
            {
                _processes.Remove(key.Pid);
            }
        }

        return (_scratch.AsSpan(0, count).ToArray(), (IReadOnlyList<ProcessKey>?)exited ?? []);
    }

    /// <summary>All threads waiting with KWAIT_REASON Suspended (5) in state Waiting (5).</summary>
    private static bool IsSuspended(SYSTEM_PROCESS_INFORMATION* info)
    {
        var threads = info->NumberOfThreads;
        if (threads == 0)
        {
            return false;
        }

        var thread = (SYSTEM_THREAD_INFORMATION*)(info + 1);
        for (var i = 0; i < threads; i++)
        {
            if (thread[i].ThreadState != 5 || thread[i].WaitReason != 5)
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        _disks?.Dispose();
        _gpu?.Dispose();
        _cpuExtra?.Dispose();
        if (_buffer != null)
        {
            NativeMemory.Free(_buffer);
            _buffer = null;
        }
    }
}
