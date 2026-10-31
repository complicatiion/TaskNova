using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNova.App.Localization;
using TaskNova.App.Services;
using TaskNova.Core.Formatting;
using TaskNova.Core.Models;

namespace TaskNova.App.ViewModels;

/// <summary>Entry of the top-consumer lists on the overview.</summary>
public sealed record TopEntry(ProcessRow Row, double Percent, string Value);

/// <summary>Overview: CPU and memory gauges, disk/network/GPU tiles with live charts, top processes and system facts.</summary>
public sealed partial class OverviewViewModel(MainViewModel main) : PageViewModel(main)
{
    public override string TitleKey => "NavOverview";

    public override string IconKey => "IconViewDashboard";

    public override string Id => "overview";

    public override bool SupportsSearch => false;

    public override SampleDemand Demand => SampleDemand.Processes | SampleDemand.Gpu;

    public MetricsHistory Metrics => Main.Metrics;

    public bool ChartFill => Settings.ChartFill;

    // ------------------------------------------------------------------ CPU

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private string _cpuText = "–";

    [ObservableProperty]
    private string _cpuSub = "";

    [ObservableProperty]
    private string _processesText = "–";

    [ObservableProperty]
    private string _threadsText = "–";

    [ObservableProperty]
    private string _handlesText = "–";

    [ObservableProperty]
    private string _uptimeText = "–";

    // ------------------------------------------------------------------ memory

    [ObservableProperty]
    private double _memoryPercent;

    [ObservableProperty]
    private string _memoryText = "–";

    [ObservableProperty]
    private string _memoryUsedText = "–";

    [ObservableProperty]
    private string _memoryAvailableText = "–";

    [ObservableProperty]
    private string _commitText = "–";

    [ObservableProperty]
    private string _cacheText = "–";

    [ObservableProperty]
    private double _memoryUsed;

    [ObservableProperty]
    private double _memoryCache;

    [ObservableProperty]
    private double _memoryTotal = 1;

    // ------------------------------------------------------------------ tiles

    [ObservableProperty]
    private string _diskText = "–";

    [ObservableProperty]
    private string _diskSub = "";

    [ObservableProperty]
    private string _networkText = "–";

    [ObservableProperty]
    private string _networkSub = "";

    [ObservableProperty]
    private string _gpuText = "–";

    [ObservableProperty]
    private string _gpuSub = "";

    [ObservableProperty]
    private bool _hasGpu;

    public ObservableCollection<TopEntry> TopCpu { get; } = [];

    /// <summary>Entries of the top lists: 5 in a normal window, more when the view gives them the height (set by the view).</summary>
    public int TopCount { get; set; } = 5;

    public ObservableCollection<TopEntry> TopMemory { get; } = [];

    // ------------------------------------------------------------------ system facts

    public string ComputerName => Main.SystemInfo.ComputerName;

    public string OsText => $"{Main.SystemInfo.OsName} · {Main.SystemInfo.OsVersion}";

    public string CpuModel => string.IsNullOrEmpty(Main.SystemInfo.CpuName) ? "–" : Main.SystemInfo.CpuName;

    public string CoresText => Loc.F("CoresText", Main.SystemInfo.CpuSockets, Main.SystemInfo.CpuCores, Main.SystemInfo.LogicalProcessors);

    public string RamText => Fmt.Bytes(Main.SystemInfo.TotalMemoryBytes);

    public string GpuNames => Main.SystemInfo.GpuNames.Count == 0 ? "–" : string.Join(", ", Main.SystemInfo.GpuNames);

    public string UserText => Main.SystemInfo.UserName;

    public string ElevationText => Loc.T(Main.SystemInfo.IsElevated ? "PillAdmin" : "PillStandard");

    public string MachineKind => Loc.T(Main.SystemInfo.IsServer ? "KindServer" : "KindWorkstation") +
                                 (Main.SystemInfo.VirtualMachine ? " · " + Loc.T("KindVirtual") : "");

    [ObservableProperty]
    private string _sessionsText = "–";

    public override void OnSettingsChanged() => OnPropertyChanged(nameof(ChartFill));

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(CoresText));
        OnPropertyChanged(nameof(ElevationText));
        OnPropertyChanged(nameof(MachineKind));
    }

    public override void OnSnapshot(SystemSnapshot s)
    {
        CpuPercent = s.Cpu.TotalPercent;
        CpuText = Fmt.Percent(s.Cpu.TotalPercent);
        CpuSub = Loc.F("KernelShare", Fmt.Percent(s.Cpu.KernelPercent)) + (s.Cpu.CurrentMhz > 0 ? " · " + Fmt.Mhz(s.Cpu.CurrentMhz) : "");
        ProcessesText = Fmt.Number(s.ProcessCount);
        ThreadsText = Fmt.Number(s.ThreadCount);
        HandlesText = Fmt.Number(s.HandleCount);
        UptimeText = Fmt.Uptime(s.Uptime);

        var memory = s.Memory;
        MemoryPercent = memory.UsedPercent;
        MemoryText = Fmt.Percent(memory.UsedPercent);
        MemoryUsedText = $"{Fmt.Bytes(memory.UsedBytes)} / {Fmt.Bytes(memory.TotalBytes)}";
        MemoryAvailableText = Fmt.Bytes(memory.AvailableBytes);
        CommitText = $"{Fmt.Bytes(memory.CommitBytes)} / {Fmt.Bytes(memory.CommitLimitBytes)}";
        CacheText = Fmt.Bytes(memory.CacheBytes);
        MemoryTotal = Math.Max(1, memory.TotalBytes);
        MemoryUsed = memory.UsedBytes;
        MemoryCache = Math.Min(memory.CacheBytes, memory.AvailableBytes);

        if (s.Disks.Count > 0)
        {
            var busiest = s.Disks.MaxBy(d => d.ActivePercent)!;
            DiskText = Fmt.Percent(busiest.ActivePercent);
            DiskSub = Loc.F("ReadWrite", Fmt.BytesPerSecond(s.Disks.Sum(d => d.ReadBytesPerSec)), Fmt.BytesPerSecond(s.Disks.Sum(d => d.WriteBytesPerSec)));
        }

        if (s.Demand.HasFlag(SampleDemand.Network))
        {
            var send = s.Networks.Sum(n => n.SendBytesPerSec);
            var receive = s.Networks.Sum(n => n.ReceiveBytesPerSec);
            NetworkText = Fmt.BitsPerSecond(send + receive);
            NetworkSub = Loc.F("SendReceive", Fmt.BitsPerSecond(send), Fmt.BitsPerSecond(receive));
        }

        HasGpu = s.Gpus.Count > 0;
        if (HasGpu)
        {
            var gpu = s.Gpus[0];
            GpuText = Fmt.Percent(s.Gpus.Max(g => g.UtilizationPercent));
            var total = Main.Sampling.GpuDedicatedTotal(gpu.Id);
            GpuSub = total > 0 ? $"{Fmt.Bytes(gpu.DedicatedUsedBytes)} / {Fmt.Bytes(total)}" : Fmt.Bytes(gpu.DedicatedUsedBytes);
        }

        var rows = Main.Processes.Rows.Where(r => r.Pid != 0).ToList();
        if (rows.Count > 0)
        {
            var total = Math.Max(1d, memory.TotalBytes);
            Replace(TopCpu, rows.OrderByDescending(r => r.Cpu).Take(TopCount).Select(r => new TopEntry(r, r.Cpu, r.CpuText)));
            Replace(TopMemory, rows.OrderByDescending(r => r.Memory).Take(TopCount).Select(r => new TopEntry(r, r.Memory * 100d / total, r.MemoryText)));
            SessionsText = Fmt.Number(rows.Where(r => r.SessionId > 0).Select(r => r.SessionId).Distinct().Count());
        }
    }

    private static void Replace(ObservableCollection<TopEntry> target, IEnumerable<TopEntry> items)
    {
        var list = items.ToList();
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
    }

    [RelayCommand]
    private void OpenProcess(TopEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        Main.SearchOn(Main.ProcessesPage, "");
        Main.ProcessesPage.Reveal(entry.Row);
    }

    /// <summary>Whole "Top CPU" / "Top memory" card: the Processes page sorted by that column.</summary>
    [RelayCommand]
    private void OpenTop(string? column)
    {
        Main.SearchOn(Main.ProcessesPage, "");
        Main.ProcessesPage.SortBy(column == "memory" ? "memory" : "cpu");
    }

    /// <summary>System card: Windows "About" settings page (msinfo32 where the Settings app is missing, e.g. Server Core).</summary>
    [RelayCommand]
    private void OpenSystemInfo(string? target)
    {
        switch (target)
        {
            case "msinfo":
                AppEnvironment.Open("msinfo32.exe");
                break;
            case "control":
                AppEnvironment.Open("control.exe");
                break;
            case "properties":
                AppEnvironment.Open("SystemPropertiesAdvanced.exe");
                break;
            default:
                var settingsApp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ImmersiveControlPanel", "SystemSettings.exe");
                AppEnvironment.Open(File.Exists(settingsApp) ? "ms-settings:about" : "msinfo32.exe");
                break;
        }
    }

    [RelayCommand]
    private void OpenPerformance(string? device)
    {
        Main.NavigateTo(Main.Performance);
        Main.Performance.Select(device);
    }
}
