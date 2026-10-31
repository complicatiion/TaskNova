using TaskNova.Core.Metrics;
using TaskNova.Core.Models;

namespace TaskNova.App.Services;

/// <summary>
/// Chart history of the system values (UI thread only). Charts draw straight from these ring buffers and only redraw when
/// a buffer's version changed and the chart is visible.
/// </summary>
public sealed class MetricsHistory
{
    private int _capacity = 60;

    public RingBuffer Cpu { get; private set; } = new(60);

    public RingBuffer CpuKernel { get; private set; } = new(60);

    public RingBuffer Memory { get; private set; } = new(60);

    public List<RingBuffer> Cores { get; } = [];

    public Dictionary<string, (RingBuffer Active, RingBuffer Read, RingBuffer Write)> Disks { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, (RingBuffer Send, RingBuffer Receive)> Networks { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, RingBuffer> Gpus { get; } = new(StringComparer.Ordinal);

    /// <summary>Total disk activity (busiest disk) and total network throughput for the overview tiles.</summary>
    public RingBuffer DiskTotal { get; private set; } = new(60);

    public RingBuffer NetworkTotal { get; private set; } = new(60);

    public RingBuffer GpuTotal { get; private set; } = new(60);

    public int Capacity => _capacity;

    /// <summary>Raised after every snapshot was added (charts redraw).</summary>
    public event Action? Updated;

    /// <summary>History length = seconds / interval, at least 30 and at most 600 points.</summary>
    public void Configure(int historySeconds, int intervalMs)
    {
        var interval = intervalMs <= 0 ? 1000 : intervalMs;
        var capacity = Math.Clamp(historySeconds * 1000 / interval, 30, 600);
        if (capacity == _capacity)
        {
            return;
        }

        _capacity = capacity;
        foreach (var buffer in All())
        {
            buffer.Resize(capacity);
        }
    }

    private IEnumerable<RingBuffer> All()
    {
        yield return Cpu;
        yield return CpuKernel;
        yield return Memory;
        yield return DiskTotal;
        yield return NetworkTotal;
        yield return GpuTotal;
        foreach (var core in Cores)
        {
            yield return core;
        }

        foreach (var (active, read, write) in Disks.Values)
        {
            yield return active;
            yield return read;
            yield return write;
        }

        foreach (var (send, receive) in Networks.Values)
        {
            yield return send;
            yield return receive;
        }

        foreach (var gpu in Gpus.Values)
        {
            yield return gpu;
        }
    }

    public void Add(SystemSnapshot snapshot)
    {
        if (snapshot.IntervalSeconds <= 0)
        {
            return; // first tick: no deltas yet
        }

        Cpu.Add(snapshot.Cpu.TotalPercent);
        CpuKernel.Add(snapshot.Cpu.KernelPercent);
        Memory.Add(snapshot.Memory.UsedPercent);

        var cores = snapshot.Cpu.Cores;
        if (cores.Length > 0)
        {
            while (Cores.Count < cores.Length)
            {
                Cores.Add(new RingBuffer(_capacity));
            }

            for (var i = 0; i < cores.Length; i++)
            {
                Cores[i].Add(cores[i]);
            }
        }

        if (snapshot.Demand.HasFlag(SampleDemand.Disks))
        {
            var busiest = 0d;
            foreach (var disk in snapshot.Disks)
            {
                if (!Disks.TryGetValue(disk.Id, out var buffers))
                {
                    buffers = (new RingBuffer(_capacity), new RingBuffer(_capacity), new RingBuffer(_capacity));
                    Disks[disk.Id] = buffers;
                }

                buffers.Active.Add(disk.ActivePercent);
                buffers.Read.Add(disk.ReadBytesPerSec);
                buffers.Write.Add(disk.WriteBytesPerSec);
                busiest = Math.Max(busiest, disk.ActivePercent);
            }

            DiskTotal.Add(busiest);
        }

        if (snapshot.Demand.HasFlag(SampleDemand.Network))
        {
            var total = 0d;
            foreach (var network in snapshot.Networks)
            {
                if (!Networks.TryGetValue(network.Id, out var buffers))
                {
                    buffers = (new RingBuffer(_capacity), new RingBuffer(_capacity));
                    Networks[network.Id] = buffers;
                }

                buffers.Send.Add(network.SendBytesPerSec);
                buffers.Receive.Add(network.ReceiveBytesPerSec);
                total += network.SendBytesPerSec + network.ReceiveBytesPerSec;
            }

            NetworkTotal.Add(total);
        }

        if (snapshot.Demand.HasFlag(SampleDemand.Gpu) && snapshot.Gpus.Count > 0)
        {
            var max = 0d;
            foreach (var gpu in snapshot.Gpus)
            {
                if (!Gpus.TryGetValue(gpu.Id, out var buffer))
                {
                    buffer = new RingBuffer(_capacity);
                    Gpus[gpu.Id] = buffer;
                }

                buffer.Add(gpu.UtilizationPercent);
                max = Math.Max(max, gpu.UtilizationPercent);
            }

            GpuTotal.Add(max);
        }

        Updated?.Invoke();
    }
}
