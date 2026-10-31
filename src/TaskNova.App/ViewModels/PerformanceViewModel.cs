using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskNova.App.Localization;
using TaskNova.Core.Formatting;
using TaskNova.Core.Metrics;
using TaskNova.Core.Models;

namespace TaskNova.App.ViewModels;

/// <summary>Label/value pair under the big chart.</summary>
public sealed partial class StatItem(string labelKey) : ObservableObject
{
    public string LabelKey { get; } = labelKey;

    public string Label => Loc.T(LabelKey);

    [ObservableProperty]
    private string _value = "–";

    public void RefreshText() => OnPropertyChanged(nameof(Label));
}

/// <summary>One device on the performance page (CPU, memory, disk, network adapter, GPU).</summary>
public sealed partial class DeviceItem : ObservableObject
{
    public DeviceItem(string id, string kind, string iconKey, RingBuffer series, RingBuffer? series2, double maximum, params string[] statKeys)
    {
        Id = id;
        Kind = kind;
        Icon = Res.Icon(iconKey);
        Series = series;
        Series2 = series2;
        Maximum = maximum;
        Stats = new ObservableCollection<StatItem>(statKeys.Select(k => new StatItem(k)));
    }

    public string Id { get; }

    public string Kind { get; }

    public Geometry? Icon { get; }

    public RingBuffer Series { get; }

    public RingBuffer? Series2 { get; }

    public bool HasSeries2 => Series2 is not null;

    /// <summary>100 for percentages, NaN = auto scale (throughput).</summary>
    public double Maximum { get; }

    public ObservableCollection<StatItem> Stats { get; }

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _subtitle = "";

    [ObservableProperty]
    private string _model = "";

    [ObservableProperty]
    private string _chartCaption = "";

    [ObservableProperty]
    private string _legend1 = "";

    [ObservableProperty]
    private string _legend2 = "";

    public void SetStat(int index, string value)
    {
        if (index < Stats.Count)
        {
            Stats[index].Value = value;
        }
    }
}

/// <summary>Performance page: device list with mini charts on the left, big chart and figures of the selected device.</summary>
public sealed partial class PerformanceViewModel(MainViewModel main) : PageViewModel(main)
{
    private DeviceItem? _cpu;
    private DeviceItem? _memory;
    private string? _pendingSelection;

    public override string TitleKey => "NavPerformance";

    public override string IconKey => "IconPulse";

    public override string Id => "performance";

    public override bool SupportsSearch => false;

    public override SampleDemand Demand => SampleDemand.Cores | SampleDemand.Disks | SampleDemand.Network | SampleDemand.Gpu | SampleDemand.Utility;

    public ObservableCollection<DeviceItem> Devices { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCpu), nameof(ShowCoreGrid), nameof(ShowBigChart))]
    private DeviceItem? _selected;

    public bool IsCpu => Selected?.Kind == "cpu";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCoreGrid), nameof(ShowBigChart), nameof(ShowOverall), nameof(ShowCoresChip))]
    private bool _showCores;

    public bool ShowOverall
    {
        get => !ShowCores;
        set
        {
            if (value)
            {
                ShowCores = false;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(ShowOverall)));
        }
    }

    public bool ShowCoresChip
    {
        get => ShowCores;
        set
        {
            if (value)
            {
                ShowCores = true;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(ShowCoresChip)));
        }
    }

    public bool ShowCoreGrid => IsCpu && ShowCores;

    public bool ShowBigChart => !ShowCoreGrid;

    public IReadOnlyList<RingBuffer> Cores => Main.Metrics.Cores;

    public bool ChartFill => Settings.ChartFill;

    public string HistoryCaption => Loc.F("ChartSeconds", Settings.HistorySeconds);

    public override void OnSettingsChanged()
    {
        OnPropertyChanged(nameof(ChartFill));
        OnPropertyChanged(nameof(HistoryCaption));
    }

    public void Select(string? id)
    {
        var device = Devices.FirstOrDefault(d => d.Id == id || d.Kind == id);
        if (device is null)
        {
            _pendingSelection = id;
        }
        else
        {
            Selected = device;
        }
    }

    protected override void OnActivated()
    {
        if (Main.Last is { } last)
        {
            OnSnapshot(last);
        }
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(HistoryCaption));
        foreach (var device in Devices)
        {
            foreach (var stat in device.Stats)
            {
                stat.RefreshText();
            }
        }

        if (Main.Last is { } last)
        {
            OnSnapshot(last);
        }
    }

    public override void OnSnapshot(SystemSnapshot s)
    {
        var metrics = Main.Metrics;
        var info = Main.SystemInfo;

        _cpu ??= Add(new DeviceItem("cpu", "cpu", "IconChip", metrics.Cpu, metrics.CpuKernel, 100,
            "StatUtilization", "StatSpeed", "StatBaseSpeed", "StatProcesses", "StatThreads", "StatHandles", "StatUptime", "StatSockets",
            "StatCores", "StatLogical", "StatVirtualMachine"));
        _cpu.Title = Loc.T("DevCpu");
        _cpu.Subtitle = $"{Fmt.Percent(s.Cpu.TotalPercent)}{(s.Cpu.CurrentMhz > 0 ? "  " + Fmt.Mhz(s.Cpu.CurrentMhz) : "")}";
        _cpu.Model = info.CpuName;
        _cpu.ChartCaption = Loc.T(s.Cpu.IsUtility ? "ChartUtility" : "ChartCpu");
        _cpu.Legend1 = Loc.T("LegendTotal");
        _cpu.Legend2 = Loc.T("LegendKernel");
        _cpu.SetStat(0, Fmt.Percent(s.Cpu.TotalPercent));
        _cpu.SetStat(1, Fmt.Mhz(s.Cpu.CurrentMhz));
        _cpu.SetStat(2, Fmt.Mhz(info.BaseMhz));
        _cpu.SetStat(3, Fmt.Number(s.ProcessCount));
        _cpu.SetStat(4, Fmt.Number(s.ThreadCount));
        _cpu.SetStat(5, Fmt.Number(s.HandleCount));
        _cpu.SetStat(6, Fmt.Uptime(s.Uptime));
        _cpu.SetStat(7, Fmt.Number(info.CpuSockets));
        _cpu.SetStat(8, Fmt.Number(info.CpuCores));
        _cpu.SetStat(9, Fmt.Number(info.LogicalProcessors));
        _cpu.SetStat(10, Loc.T(info.VirtualMachine ? "Yes" : "No"));

        _memory ??= Add(new DeviceItem("memory", "memory", "IconMemory", metrics.Memory, null, 100,
            "StatInUse", "StatAvailable", "StatCommitted", "StatCached", "StatPagedPool", "StatNonPagedPool", "StatTotal"));
        var m = s.Memory;
        _memory.Title = Loc.T("DevMemory");
        _memory.Subtitle = $"{Fmt.Bytes(m.UsedBytes)} / {Fmt.Bytes(m.TotalBytes)} ({Fmt.Percent(m.UsedPercent)})";
        _memory.Model = Fmt.Bytes(m.TotalBytes);
        _memory.ChartCaption = Loc.T("ChartMemory");
        _memory.Legend1 = Loc.T("LegendInUse");
        _memory.SetStat(0, Fmt.Bytes(m.UsedBytes));
        _memory.SetStat(1, Fmt.Bytes(m.AvailableBytes));
        _memory.SetStat(2, $"{Fmt.Bytes(m.CommitBytes)} / {Fmt.Bytes(m.CommitLimitBytes)}");
        _memory.SetStat(3, Fmt.Bytes(m.CacheBytes));
        _memory.SetStat(4, Fmt.Bytes(m.PagedPoolBytes));
        _memory.SetStat(5, Fmt.Bytes(m.NonPagedPoolBytes));
        _memory.SetStat(6, Fmt.Bytes(m.TotalBytes));

        var present = new HashSet<string> { "cpu", "memory" };
        foreach (var disk in s.Disks)
        {
            var id = "disk:" + disk.Id;
            present.Add(id);
            if (!metrics.Disks.TryGetValue(disk.Id, out var buffers))
            {
                continue;
            }

            var item = Devices.FirstOrDefault(d => d.Id == id) ?? Add(new DeviceItem(id, "disk", "IconHarddisk", buffers.Active, null, 100,
                "StatActiveTime", "StatRead", "StatWrite", "StatResponse"));
            item.Title = Loc.F("DevDisk", disk.Name);
            item.Subtitle = Fmt.Percent(disk.ActivePercent);
            item.Model = disk.Name;
            item.ChartCaption = Loc.T("ChartActiveTime");
            item.Legend1 = Loc.T("LegendActive");
            item.SetStat(0, Fmt.Percent(disk.ActivePercent));
            item.SetStat(1, Fmt.BytesPerSecond(disk.ReadBytesPerSec));
            item.SetStat(2, Fmt.BytesPerSecond(disk.WriteBytesPerSec));
            item.SetStat(3, disk.AvgResponseMs.ToString("0.0", Fmt.Culture) + " ms");
        }

        foreach (var network in s.Networks)
        {
            var id = "net:" + network.Id;
            present.Add(id);
            if (!metrics.Networks.TryGetValue(network.Id, out var buffers))
            {
                continue;
            }

            var item = Devices.FirstOrDefault(d => d.Id == id) ?? Add(new DeviceItem(id, "network",
                network.Kind == "wifi" ? "IconWifi" : "IconEthernet", buffers.Receive, buffers.Send, double.NaN,
                "StatReceive", "StatSend", "StatLinkSpeed", "StatAdapter", "StatIPv4"));
            item.Title = Loc.T(network.Kind switch { "wifi" => "DevWifi", "ethernet" => "DevEthernet", _ => "DevNetwork" }) + " · " + network.Name;
            item.Subtitle = Loc.F("SendReceiveShort", Fmt.BitsPerSecond(network.SendBytesPerSec), Fmt.BitsPerSecond(network.ReceiveBytesPerSec));
            item.Model = network.Description;
            item.ChartCaption = Loc.T("ChartThroughput");
            item.Legend1 = Loc.T("LegendReceive");
            item.Legend2 = Loc.T("LegendSend");
            item.SetStat(0, Fmt.BitsPerSecond(network.ReceiveBytesPerSec));
            item.SetStat(1, Fmt.BitsPerSecond(network.SendBytesPerSec));
            item.SetStat(2, Fmt.LinkSpeed(network.LinkSpeedBitsPerSec));
            item.SetStat(3, network.Description);
            item.SetStat(4, network.IPv4 ?? "–");
        }

        var gpuIndex = 0;
        foreach (var gpu in s.Gpus)
        {
            var id = "gpu:" + gpu.Id;
            present.Add(id);
            if (!metrics.Gpus.TryGetValue(gpu.Id, out var buffer))
            {
                continue;
            }

            var item = Devices.FirstOrDefault(d => d.Id == id) ?? Add(new DeviceItem(id, "gpu", "IconExpansionCard", buffer, null, 100,
                "StatUtilization", "StatDedicated", "StatShared", "StatEngines"));
            var total = Main.Sampling.GpuDedicatedTotal(gpu.Id);
            item.Title = $"{Loc.T("DevGpu")} {gpuIndex}";
            item.Subtitle = Fmt.Percent(gpu.UtilizationPercent);
            item.Model = gpu.Name;
            item.ChartCaption = Loc.T("ChartGpu");
            item.Legend1 = Loc.T("LegendBusiestEngine");
            item.SetStat(0, Fmt.Percent(gpu.UtilizationPercent));
            item.SetStat(1, total > 0 ? $"{Fmt.Bytes(gpu.DedicatedUsedBytes)} / {Fmt.Bytes(total)}" : Fmt.Bytes(gpu.DedicatedUsedBytes));
            item.SetStat(2, Fmt.Bytes(gpu.SharedUsedBytes));
            item.SetStat(3, string.Join("  ", gpu.Engines.OrderByDescending(e => e.Value).Take(4).Select(e => $"{e.Key} {Fmt.Percent(e.Value)}")));
            gpuIndex++;
        }

        // Devices that disappeared (adapter down). GPUs keep their item between the 2-second GPU samples.
        if (s.Demand.HasFlag(SampleDemand.Disks) && s.Demand.HasFlag(SampleDemand.Network))
        {
            foreach (var gone in Devices.Where(d => !present.Contains(d.Id) && d.Kind != "gpu").ToList())
            {
                Devices.Remove(gone);
            }
        }

        if (_pendingSelection is not null && Devices.FirstOrDefault(d => d.Id == _pendingSelection || d.Kind == _pendingSelection) is { } pending)
        {
            Selected = pending;
            _pendingSelection = null;
        }

        Selected ??= _cpu;
    }

    private DeviceItem Add(DeviceItem item)
    {
        // Order: CPU, memory, disks, network, GPU.
        var rank = Rank(item.Kind);
        var index = 0;
        while (index < Devices.Count && Rank(Devices[index].Kind) <= rank)
        {
            index++;
        }

        Devices.Insert(index, item);
        return item;
    }

    private static int Rank(string kind) => kind switch
    {
        "cpu" => 0,
        "memory" => 1,
        "disk" => 2,
        "network" => 3,
        _ => 4
    };
}
