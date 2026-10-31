using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNova.App.Localization;
using TaskNova.App.Services;
using TaskNova.Core.Formatting;
using TaskNova.Core.Models;

namespace TaskNova.App.ViewModels;

/// <summary>One line of a mini monitor detail list (process, disk or adapter).</summary>
public sealed record MiniLine(string Name, string Value, string? Sub = null, double Percent = 0, Avalonia.Media.Imaging.Bitmap? Icon = null)
{
    public bool HasSub => !string.IsNullOrEmpty(Sub);
}

/// <summary>
/// Mini monitor: CPU, RAM, disk and network with sparklines. A click on a metric expands its details
/// (CPU: kernel share, clock, top 3 · RAM: used/available/committed, top 3 · disks and adapters one by one).
/// </summary>
public sealed partial class MiniMonitorViewModel : ObservableObject, IDisposable
{
    public MiniMonitorViewModel(MainViewModel main)
    {
        Main = main;
        main.StateChanged += Update;
        Update();
    }

    public MainViewModel Main { get; }

    public MetricsHistory Metrics => Main.Metrics;

    public bool ChartFill => Main.ChartFill;

    /// <summary>Expanded metric: "cpu", "ram", "disk", "net" or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuExpanded), nameof(RamExpanded), nameof(DiskExpanded), nameof(NetExpanded))]
    private string? _expanded;

    public bool CpuExpanded => Expanded == "cpu";

    public bool RamExpanded => Expanded == "ram";

    public bool DiskExpanded => Expanded == "disk";

    public bool NetExpanded => Expanded == "net";

    [ObservableProperty]
    private string _cpuText = "–";

    [ObservableProperty]
    private string _ramText = "–";

    [ObservableProperty]
    private string _diskText = "–";

    [ObservableProperty]
    private string _networkText = "–";

    [ObservableProperty]
    private string _cpuDetail = "";

    [ObservableProperty]
    private string _ramDetail = "";

    [ObservableProperty]
    private string _commitDetail = "";

    public ObservableCollection<MiniLine> TopCpu { get; } = [];

    public ObservableCollection<MiniLine> TopRam { get; } = [];

    public ObservableCollection<MiniLine> Disks { get; } = [];

    public ObservableCollection<MiniLine> Adapters { get; } = [];

    /// <summary>Raised when the expanded part changes (the window re-anchors its position to the new height).</summary>
    public event Action? LayoutChanged;

    [RelayCommand]
    private void Toggle(string? metric)
    {
        Expanded = Expanded == metric ? null : metric;
        // Top lists need the process list from the sampler while CPU or RAM is expanded.
        Main.SetMiniMonitorDemand(CpuExpanded || RamExpanded ? SampleDemand.Processes : SampleDemand.None);
        Update();
        LayoutChanged?.Invoke();
    }

    private void Update()
    {
        CpuText = Main.CpuText;
        RamText = Main.MemoryText;
        DiskText = Main.DiskText;
        NetworkText = Main.NetworkText;
        if (Main.Last is not { } s)
        {
            return;
        }

        if (CpuExpanded)
        {
            CpuDetail = Loc.F("KernelShare", Fmt.Percent(s.Cpu.KernelPercent)) + (s.Cpu.CurrentMhz > 0 ? " · " + Fmt.Mhz(s.Cpu.CurrentMhz) : "");
            Replace(TopCpu, Main.Processes.Rows.Where(r => r.Pid != 0).OrderByDescending(r => r.Cpu).Take(3)
                .Select(r => new MiniLine(r.DisplayName, r.CpuText, Percent: r.Cpu, Icon: r.Icon)));
        }
        else if (RamExpanded)
        {
            var m = s.Memory;
            RamDetail = Loc.F("MiniUsedAvailable", Fmt.Bytes(m.UsedBytes), Fmt.Bytes(m.TotalBytes), Fmt.Bytes(m.AvailableBytes));
            CommitDetail = Loc.F("MiniCommit", Fmt.Bytes(m.CommitBytes), Fmt.Bytes(m.CommitLimitBytes));
            var total = Math.Max(1d, m.TotalBytes);
            Replace(TopRam, Main.Processes.Rows.Where(r => r.Pid != 0).OrderByDescending(r => r.Memory).Take(3)
                .Select(r => new MiniLine(r.DisplayName, r.MemoryText, Percent: r.Memory * 100d / total, Icon: r.Icon)));
        }
        else if (DiskExpanded && s.Demand.HasFlag(SampleDemand.Disks))
        {
            Replace(Disks, s.Disks.Select(d => new MiniLine(d.Name, Fmt.Percent(d.ActivePercent),
                Loc.F("ReadWrite", Fmt.BytesPerSecond(d.ReadBytesPerSec), Fmt.BytesPerSecond(d.WriteBytesPerSec)), d.ActivePercent)));
        }
        else if (NetExpanded && s.Demand.HasFlag(SampleDemand.Network))
        {
            Replace(Adapters, s.Networks.Select(n => new MiniLine(n.Name, Fmt.BitsPerSecond(n.SendBytesPerSec + n.ReceiveBytesPerSec),
                Loc.F("SendReceive", Fmt.BitsPerSecond(n.SendBytesPerSec), Fmt.BitsPerSecond(n.ReceiveBytesPerSec)))));
        }
    }

    private void Replace(ObservableCollection<MiniLine> target, IEnumerable<MiniLine> items)
    {
        var list = items.ToList();
        var countChanged = list.Count != target.Count;
        for (var i = 0; i < list.Count; i++)
        {
            if (i < target.Count)
            {
                if (target[i] != list[i])
                {
                    target[i] = list[i];
                }
            }
            else
            {
                target.Add(list[i]);
            }
        }

        while (target.Count > list.Count)
        {
            target.RemoveAt(target.Count - 1);
        }

        if (countChanged)
        {
            LayoutChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        Main.StateChanged -= Update;
        Main.SetMiniMonitorDemand(SampleDemand.None);
    }
}
