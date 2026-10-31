using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskNova.App.Localization;
using TaskNova.App.Services;
using TaskNova.Core.Formatting;
using TaskNova.Core.Models;
using TaskNova.Platform.Windows;

namespace TaskNova.App.ViewModels;

/// <summary>
/// One process, shared by the Processes, Details and Users pages and the overview. Values are updated in place every
/// tick; PropertyChanged fires only when the displayed value actually changes (rounded), so invisible rows cost nothing
/// and visible rows redraw only what changed.
/// </summary>
public sealed class ProcessRow : ObservableObject
{
    private double _cpu;
    private double _cpuShown = -1;
    private ulong _memory;
    private ulong _memoryShown = ulong.MaxValue;
    private double _io;
    private double _ioShown = -1;
    private double _gpu;
    private double _gpuShown = -1;
    private int _cpuLevel = -1;
    private int _memoryLevel = -1;
    private int _ioLevel = -1;
    private int _gpuLevel = -1;
    private int _heatVersion = -1;

    public ProcessRow(ProcessKey key, string name)
    {
        Key = key;
        Name = name;
    }

    public ProcessKey Key { get; }

    public int Pid => Key.Pid;

    public string Name { get; }

    public int ParentPid { get; private set; }

    public int SessionId { get; private set; }

    // ------------------------------------------------------------------ details (once, from the enricher)

    public string? Path { get; private set; }

    public string? CommandLine { get; private set; }

    public string? UserName { get; private set; }

    public string? Description { get; private set; }

    public string? Company { get; private set; }

    public string? Platform { get; private set; }

    public Bitmap? Icon { get; private set; }

    public bool HasIcon => Icon is not null;

    public bool DetailsLoaded { get; private set; }

    /// <summary>Processes page: description like the Windows Task Manager ("Google Chrome"), otherwise the image name.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Description) ? Name : Description!;

    public string UserText => UserName ?? (DetailsLoaded ? "–" : "");

    // ------------------------------------------------------------------ group, security

    public ProcessGroup Group { get; set; } = ProcessGroup.Background;

    public bool HasWindow { get; set; }

    public bool IsCritical => Core.Classification.ProcessClassifier.IsCritical(Name);

    private SecurityFlags _security;

    public SecurityFlags Security
    {
        get => _security;
        set
        {
            if (SetProperty(ref _security, value))
            {
                OnPropertyChanged(nameof(HasWarning));
                OnPropertyChanged(nameof(WarningTip));
            }
        }
    }

    public string? Signer { get; set; }

    public bool? Signed { get; set; }

    public bool HasWarning => Security != SecurityFlags.None;

    public string WarningTip
    {
        get
        {
            var parts = new List<string>();
            if (Security.HasFlag(SecurityFlags.Unsigned))
            {
                parts.Add(Loc.T("SecUnsigned"));
            }

            if (Security.HasFlag(SecurityFlags.UserWritableLocation))
            {
                parts.Add(Loc.T("SecUserLocation"));
            }

            if (Security.HasFlag(SecurityFlags.FileMissing))
            {
                parts.Add(Loc.T("SecFileMissing"));
            }

            if (Security.HasFlag(SecurityFlags.NameMismatch))
            {
                parts.Add(Loc.T("SecNameMismatch"));
            }

            return string.Join("\n", parts);
        }
    }

    // ------------------------------------------------------------------ live values

    public double Cpu => _cpu;

    public string CpuText => Fmt.CpuPercent(_cpu);

    public IBrush CpuHeat => HeatBrushes.For(_cpuLevel);

    public ulong Memory => _memory;

    public string MemoryText => Fmt.MemoryMb(_memory);

    public IBrush MemoryHeat => HeatBrushes.For(_memoryLevel);

    public double Io => _io;

    public string IoText => _io < 0.5 ? "0 MB/s" : (_io / (1024 * 1024)).ToString(_io >= 10 * 1024 * 1024 ? "0" : "0.0", Fmt.Culture) + " MB/s";

    public IBrush IoHeat => HeatBrushes.For(_ioLevel);

    public double Gpu => _gpu;

    public string GpuText => Fmt.CpuPercent(_gpu);

    public IBrush GpuHeat => HeatBrushes.For(_gpuLevel);

    public bool IsSuspended { get; private set; }

    public string StatusText => IsSuspended ? Loc.T("StatusSuspended") : Loc.T("StatusRunning");

    public int Threads { get; private set; }

    public int Handles { get; private set; }

    public int BasePriority { get; private set; }

    public PriorityLevel Priority => ProcessActions.FromBasePriority(BasePriority);

    public string PriorityText => Loc.T("Priority" + Priority);

    public long CpuTime { get; private set; }

    public string CpuTimeText => Fmt.CpuTime(CpuTime);

    public ulong WorkingSet { get; private set; }

    public string WorkingSetText => Fmt.MemoryMb(WorkingSet);

    public ulong PeakWorkingSet { get; private set; }

    public string PeakWorkingSetText => Fmt.MemoryMb(PeakWorkingSet);

    public ulong PrivateBytes { get; private set; }

    public string PrivateBytesText => Fmt.MemoryMb(PrivateBytes);

    public ulong IoRead { get; private set; }

    public string IoReadText => Fmt.Bytes(IoRead);

    public ulong IoWrite { get; private set; }

    public string IoWriteText => Fmt.Bytes(IoWrite);

    public uint PageFaults { get; private set; }

    public string PageFaultsText => Fmt.Number(PageFaults);

    // ------------------------------------------------------------------ tree / list layout

    private int _depth;

    public int Depth
    {
        get => _depth;
        set
        {
            if (SetProperty(ref _depth, value))
            {
                OnPropertyChanged(nameof(Indent));
            }
        }
    }

    public Thickness Indent => new(4 + Depth * 18, 0, 0, 0);

    private bool _hasChildren;

    public bool HasChildren
    {
        get => _hasChildren;
        set => SetProperty(ref _hasChildren, value);
    }

    private bool _isExpanded = true;

    /// <summary>Tree view: children shown. Changing it asks the page to rebuild the list.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                ExpandedChanged?.Invoke(this);
            }
        }
    }

    public static event Action<ProcessRow>? ExpandedChanged;

    // Cached event args: the update runs for every visible row on every tick – no allocations on that path.
    private static readonly PropertyChangedEventArgs CpuTextArgs = new(nameof(CpuText));
    private static readonly PropertyChangedEventArgs MemoryTextArgs = new(nameof(MemoryText));
    private static readonly PropertyChangedEventArgs IoTextArgs = new(nameof(IoText));
    private static readonly PropertyChangedEventArgs GpuTextArgs = new(nameof(GpuText));
    private static readonly PropertyChangedEventArgs CpuHeatArgs = new(nameof(CpuHeat));
    private static readonly PropertyChangedEventArgs MemoryHeatArgs = new(nameof(MemoryHeat));
    private static readonly PropertyChangedEventArgs IoHeatArgs = new(nameof(IoHeat));
    private static readonly PropertyChangedEventArgs GpuHeatArgs = new(nameof(GpuHeat));
    private static readonly PropertyChangedEventArgs ThreadsArgs = new(nameof(Threads));
    private static readonly PropertyChangedEventArgs HandlesArgs = new(nameof(Handles));
    private static readonly PropertyChangedEventArgs CpuTimeTextArgs = new(nameof(CpuTimeText));
    private static readonly PropertyChangedEventArgs WorkingSetTextArgs = new(nameof(WorkingSetText));
    private static readonly PropertyChangedEventArgs PeakWorkingSetTextArgs = new(nameof(PeakWorkingSetText));
    private static readonly PropertyChangedEventArgs PrivateBytesTextArgs = new(nameof(PrivateBytesText));
    private static readonly PropertyChangedEventArgs IoReadTextArgs = new(nameof(IoReadText));
    private static readonly PropertyChangedEventArgs IoWriteTextArgs = new(nameof(IoWriteText));
    private static readonly PropertyChangedEventArgs PageFaultsTextArgs = new(nameof(PageFaultsText));

    /// <summary>Applies one sample; raises change notifications only for values whose shown text changed.</summary>
    public void Update(in ProcessSample sample, ulong totalMemory)
    {
        ParentPid = sample.ParentPid;
        SessionId = sample.SessionId;
        _cpu = sample.CpuPercent;
        _memory = sample.PrivateWorkingSet;
        _io = sample.IoBytesPerSec;
        _gpu = sample.GpuPercent;

        var heatChanged = _heatVersion != HeatBrushes.Version;
        _heatVersion = HeatBrushes.Version;

        var cpuShown = Math.Round(_cpu, 1);
        if (cpuShown != _cpuShown)
        {
            _cpuShown = cpuShown;
            OnPropertyChanged(CpuTextArgs);
        }

        // Memory column changes in 0.1 MB steps.
        var memoryShown = _memory / (100 * 1024);
        if (memoryShown != _memoryShown)
        {
            _memoryShown = memoryShown;
            OnPropertyChanged(MemoryTextArgs);
        }

        var ioShown = Math.Round(_io / (100 * 1024));
        if (ioShown != _ioShown)
        {
            _ioShown = ioShown;
            OnPropertyChanged(IoTextArgs);
        }

        var gpuShown = Math.Round(_gpu, 1);
        if (gpuShown != _gpuShown)
        {
            _gpuShown = gpuShown;
            OnPropertyChanged(GpuTextArgs);
        }

        UpdateLevel(ref _cpuLevel, HeatBrushes.Level(_cpu), CpuHeatArgs, heatChanged);
        UpdateLevel(ref _memoryLevel, totalMemory == 0 ? 0 : HeatBrushes.Level(_memory * 500d / totalMemory), MemoryHeatArgs, heatChanged);
        UpdateLevel(ref _ioLevel, HeatBrushes.Level(_io / (1024 * 1024) * 10), IoHeatArgs, heatChanged);
        UpdateLevel(ref _gpuLevel, HeatBrushes.Level(_gpu), GpuHeatArgs, heatChanged);

        if (IsSuspended != sample.IsSuspended)
        {
            IsSuspended = sample.IsSuspended;
            OnPropertyChanged(nameof(IsSuspended));
            OnPropertyChanged(nameof(StatusText));
        }

        if (Threads != sample.Threads)
        {
            Threads = sample.Threads;
            OnPropertyChanged(ThreadsArgs);
        }

        if (Handles != sample.Handles)
        {
            Handles = sample.Handles;
            OnPropertyChanged(HandlesArgs);
        }

        if (BasePriority != sample.BasePriority)
        {
            BasePriority = sample.BasePriority;
            OnPropertyChanged(nameof(PriorityText));
        }

        var cpuTimeChanged = sample.CpuTime100Ns / 10_000_000 != CpuTime / 10_000_000;
        CpuTime = sample.CpuTime100Ns;
        if (cpuTimeChanged)
        {
            OnPropertyChanged(CpuTimeTextArgs);
        }

        if (BytesShownChanged(sample.WorkingSet, WorkingSet))
        {
            OnPropertyChanged(WorkingSetTextArgs);
        }

        WorkingSet = sample.WorkingSet;
        if (BytesShownChanged(sample.PeakWorkingSet, PeakWorkingSet))
        {
            OnPropertyChanged(PeakWorkingSetTextArgs);
        }

        PeakWorkingSet = sample.PeakWorkingSet;
        if (BytesShownChanged(sample.PrivateBytes, PrivateBytes))
        {
            OnPropertyChanged(PrivateBytesTextArgs);
        }

        PrivateBytes = sample.PrivateBytes;
        if (BytesShownChanged(sample.IoReadBytes, IoRead))
        {
            OnPropertyChanged(IoReadTextArgs);
        }

        IoRead = sample.IoReadBytes;
        if (BytesShownChanged(sample.IoWriteBytes, IoWrite))
        {
            OnPropertyChanged(IoWriteTextArgs);
        }

        IoWrite = sample.IoWriteBytes;
        if (PageFaults != sample.PageFaults)
        {
            PageFaults = sample.PageFaults;
            OnPropertyChanged(PageFaultsTextArgs);
        }
    }

    private void UpdateLevel(ref int field, int level, PropertyChangedEventArgs args, bool force)
    {
        if (field != level || force)
        {
            field = level;
            OnPropertyChanged(args);
        }
    }

    /// <summary>The byte columns show 0.1 MB steps; a notification is needed only when that step changes.</summary>
    private static bool BytesShownChanged(ulong value, ulong current) => value / (100 * 1024) != current / (100 * 1024);

    /// <summary>Details from the background lookup.</summary>
    public void ApplyDetails(ProcessDetails details)
    {
        DetailsLoaded = true;
        Path = details.Path;
        CommandLine = details.CommandLine;
        UserName = details.UserName;
        Description = details.Description;
        Company = details.Company;
        Platform = details.Platform;
        Icon = IconCache.FromPixels(details.Path, details.IconPixels, details.IconSize);
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(CommandLine));
        OnPropertyChanged(nameof(UserName));
        OnPropertyChanged(nameof(UserText));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Platform));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasIcon));
    }

    /// <summary>Language switch: texts with translated parts.</summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PriorityText));
        OnPropertyChanged(nameof(WarningTip));
        OnPropertyChanged(nameof(UserText));
    }
}

/// <summary>Group header of the Processes page (Apps / Background processes / Windows processes).</summary>
public sealed class GroupRow : ObservableObject
{
    public GroupRow(ProcessGroup group)
    {
        Group = group;
    }

    public ProcessGroup Group { get; }

    public string Title => Loc.T("Group" + Group);

    private int _count;

    public int Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(Header));
            }
        }
    }

    public string Header => $"{Title} ({Count})";

    private bool _isExpanded = true;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                ExpandedChanged?.Invoke(this);
            }
        }
    }

    public event Action<GroupRow>? ExpandedChanged;

    public void RefreshText() => OnPropertyChanged(nameof(Header));
}
