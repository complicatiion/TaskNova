using Avalonia.Threading;
using TaskNova.Core.History;
using TaskNova.Core.Models;
using TaskNova.Platform.Windows;

namespace TaskNova.App.Services;

/// <summary>
/// The only polling loop of TaskNova: one background thread samples the system at the configured interval and hands the
/// snapshot to the UI thread in one batch (<see cref="DispatcherPriority.Background"/>). When the UI has not applied the
/// previous snapshot yet, the new one replaces it – no backlog, the window stays responsive at 100 % system load.
/// What is sampled is decided per tick by <see cref="Demand"/> (visible page + window state).
/// </summary>
public sealed class SamplingService : IDisposable
{
    private readonly SystemSampler _sampler;
    private readonly AppHistoryStore _history;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private SystemSnapshot? _pending;
    private int _posted;
    private volatile bool _disposed;
    private volatile int _intervalMs = 1000;
    private volatile int _demand = (int)SampleDemand.System;
    private long _tick;
    private long _lastHistorySave = Environment.TickCount64;

    public SamplingService(AppHistoryStore history, int baseMhz)
    {
        _history = history;
        _sampler = new SystemSampler(history, baseMhz);
        _thread = new Thread(Run) { IsBackground = true, Name = "TaskNova.Sampler", Priority = ThreadPriority.AboveNormal };
    }

    /// <summary>Raised on the UI thread with the newest snapshot.</summary>
    public event Action<SystemSnapshot>? SnapshotReady;

    public int LogicalProcessors => _sampler.LogicalProcessors;

    public bool HasGpu => _sampler.HasGpu;

    public SampleDemand Demand
    {
        get => (SampleDemand)_demand;
        set
        {
            var old = (SampleDemand)_demand;
            _demand = (int)value;

            // A page that needs more data than before gets it right away instead of after the next interval.
            if ((value & ~old) != 0)
            {
                _wake.Set();
            }
        }
    }

    /// <summary>Update interval in ms; 0 = paused (a manual refresh still samples once).</summary>
    public int IntervalMs
    {
        get => _intervalMs;
        set
        {
            _intervalMs = value;
            _wake.Set();
        }
    }

    public bool IsPaused => _intervalMs <= 0;

    /// <summary>While the window is hidden: sample processes every 5th tick for the app history.</summary>
    public bool BackgroundHistory { get; set; } = true;

    public bool UseProcessorUtility
    {
        get => _sampler.UseProcessorUtility;
        set => _sampler.UseProcessorUtility = value;
    }

    public bool GpuEnabled
    {
        get => _sampler.GpuEnabled;
        set => _sampler.GpuEnabled = value;
    }

    public bool RecordHistory
    {
        get => _sampler.RecordHistory;
        set => _sampler.RecordHistory = value;
    }

    public ulong GpuDedicatedTotal(string luid) => _sampler.GpuDedicatedTotal(luid);

    public void Start() => _thread.Start();

    /// <summary>Samples once now (F5, resume, page switch).</summary>
    public void RefreshNow() => _wake.Set();

    private void Run()
    {
        while (!_disposed)
        {
            var demand = Demand;
            if (BackgroundHistory && RecordHistory && !demand.HasFlag(SampleDemand.Processes) && _tick % 5 == 0)
            {
                demand |= SampleDemand.Processes;
            }

            try
            {
                var snapshot = _sampler.Sample(demand);
                Publish(snapshot);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("TaskNova sampler: " + ex);
            }

            _tick++;
            if (Environment.TickCount64 - _lastHistorySave > 5 * 60_000)
            {
                _lastHistorySave = Environment.TickCount64;
                _history.Save();
            }

            var interval = _intervalMs;
            _wake.WaitOne(interval <= 0 ? Timeout.Infinite : interval);
        }
    }

    private void Publish(SystemSnapshot snapshot)
    {
        Volatile.Write(ref _pending, snapshot);
        if (Interlocked.Exchange(ref _posted, 1) == 1)
        {
            return; // The UI still has one queued – it will pick up this newer snapshot.
        }

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _posted, 0);
            var latest = Interlocked.Exchange(ref _pending, null);
            if (latest is not null && !_disposed)
            {
                SnapshotReady?.Invoke(latest);
            }
        }, DispatcherPriority.Background);
    }

    public void Dispose()
    {
        _disposed = true;
        _wake.Set();
        // Free native buffers only when the sampler thread has really stopped.
        if (!_thread.IsAlive || _thread.Join(3000))
        {
            _sampler.Dispose();
        }

        _history.Save();
    }
}
