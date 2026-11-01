using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNova.App.Localization;
using TaskNova.App.Services;
using TaskNova.Core.Formatting;
using TaskNova.Core.Models;
using TaskNova.Core.Reports;
using TaskNova.Platform.Windows;

namespace TaskNova.App.ViewModels;

/// <summary>Shared actions of the tool pages on a process id: end it or show it on the Processes page.</summary>
public abstract class ToolPageViewModel(MainViewModel main) : PageViewModel(main)
{
    protected async Task EndProcessAsync(int pid)
    {
        if (Main.Processes.FindByPid(pid) is { } row)
        {
            await ProcessCommands.EndAsync(Main, row, tree: false);
        }
        else
        {
            Main.ShowToast(Loc.F("ToastProcessGone", pid), error: true);
        }
    }

    protected void ShowProcess(int pid)
    {
        if (Main.Processes.FindByPid(pid) is { } row)
        {
            Main.SearchOn(Main.ProcessesPage, "");
            Main.ProcessesPage.Reveal(row);
        }
        else
        {
            Main.SearchOn(Main.Details, $"pid:{pid}");
        }
    }

    protected (string Name, Bitmap? Icon, string? User) Describe(int pid, string fallback)
    {
        var row = Main.Processes.FindByPid(pid);
        return row is null ? (fallback, null, null) : (row.DisplayName, row.Icon, row.UserName);
    }
}

// ================================================================================================== file locks

public sealed record LockerRow(int Pid, string Name, Bitmap? Icon, string? User, string Kind, string? Service, int Session) : Controls.ITypeAheadItem
{
    string Controls.ITypeAheadItem.TypeAheadText => Name;

    public string ServiceText => Service ?? "–";

    public string UserText => User ?? "–";
}

/// <summary>"Which process is locking this file?" – Restart Manager, the API Explorer and installers use.</summary>
public sealed partial class FileLocksViewModel(MainViewModel main) : ToolPageViewModel(main)
{
    public override string TitleKey => "NavFileLocks";

    public override string IconKey => "IconFileLock";

    public override string Id => "filelocks";

    public override bool SupportsSearch => false;

    // The process list provides names and icons of the lockers.
    public override SampleDemand Demand => SampleDemand.Processes;

    public ObservableCollection<LockerRow> Items { get; } = [];

    private List<LockerRow> _found = [];

    private ListSort? _sortState;

    private ListSort ListSortState => _sortState ??= new ListSort("filelocks", Settings, Main.SaveSettingsSoon, "name", false, "pid", "session");

    public string SortColumn => ListSortState.Column;

    public bool SortDescending => ListSortState.Descending;

    [RelayCommand]
    private void Sort(string column)
    {
        ListSortState.Toggle(column);
        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortDescending));
        ShowSorted();
    }

    private void ShowSorted()
    {
        var selected = Selected;
        var rows = _found.ToList();
        rows.Sort(ListSortState.Comparer(new Dictionary<string, Comparison<LockerRow>>
        {
            ["name"] = (a, b) => ListSort.Text(a.Name, b.Name),
            ["pid"] = (a, b) => a.Pid.CompareTo(b.Pid),
            ["type"] = (a, b) => ListSort.Text(a.Kind, b.Kind),
            ["user"] = (a, b) => ListSort.Text(a.User, b.User),
            ["session"] = (a, b) => a.Session.CompareTo(b.Session)
        }, (a, b) => a.Pid.CompareTo(b.Pid)));
        Items.Clear();
        foreach (var row in rows)
        {
            Items.Add(row);
        }

        Selected = selected is null ? null : Items.FirstOrDefault(r => r.Pid == selected.Pid);
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    private string _path = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private LockerRow? _selected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _summary = "";

    public bool HasSelection => Selected is not null;

    [RelayCommand]
    private async Task BrowseFile()
    {
        if (await Host.PickPathAsync(Loc.T("DlgPickFile"), folder: false) is { } file)
        {
            Path = file;
            await Find();
        }
    }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        if (await Host.PickPathAsync(Loc.T("DlgPickFolder"), folder: true) is { } folder)
        {
            Path = folder;
            await Find();
        }
    }

    private bool CanFind() => !string.IsNullOrWhiteSpace(Path);

    [RelayCommand(CanExecute = nameof(CanFind))]
    private async Task Find()
    {
        var path = Path.Trim().Trim('"');
        if (path.Length == 0 || IsBusy)
        {
            return;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Items.Clear();
            Summary = Loc.T("LocksNotFound");
            return;
        }

        IsBusy = true;
        try
        {
            var (lockers, error) = await Task.Run(() => SystemTools.FindLockers(path));
            var found = new List<LockerRow>();
            foreach (var locker in lockers.DistinctBy(l => l.Pid))
            {
                var (name, icon, user) = Describe(locker.Pid, locker.AppName);
                var kind = Loc.T(locker.ServiceName is not null ? "LockKindService" : locker.AppType switch
                {
                    1 => "LockKindWindow",
                    2 => "LockKindApp",
                    3 => "LockKindService",
                    4 => "LockKindExplorer",
                    5 => "LockKindConsole",
                    1000 => "LockKindCritical",
                    _ => "LockKindOther"
                });
                found.Add(new LockerRow(locker.Pid, name, icon, user, kind, locker.ServiceName, locker.SessionId));
            }

            _found = found;
            ShowSorted();

            Summary = error is not null ? Loc.F("ToastActionFailed", error)
                : Items.Count == 0 ? Loc.T("LocksNone")
                : Loc.F("LocksSummary", Items.Count);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task EndProcess()
    {
        if (Selected is { } row)
        {
            await EndProcessAsync(row.Pid);
            await Task.Delay(400);
            await Find();
        }
    }

    [RelayCommand]
    private void ShowInProcesses()
    {
        if (Selected is { } row)
        {
            ShowProcess(row.Pid);
        }
    }
}

// ================================================================================================== ports

public sealed record PortRow(ConnectionInfo Info, string Name, Bitmap? Icon) : Controls.ITypeAheadItem
{
    string Controls.ITypeAheadItem.TypeAheadText => Name;

    public string Protocol => Info.Protocol switch
    {
        ConnectionProtocol.Tcp => "TCP",
        ConnectionProtocol.Tcp6 => "TCPv6",
        ConnectionProtocol.Udp => "UDP",
        _ => "UDPv6"
    };

    public int Port => Info.LocalPort;

    public string Local => Info.LocalAddress;

    public string Remote => string.IsNullOrEmpty(Info.RemoteAddress) ? "–" : $"{Info.RemoteAddress}:{Info.RemotePort}";

    public string State => string.IsNullOrEmpty(Info.State) ? "–" : Info.State;

    public int Pid => Info.ProcessId;
}

/// <summary>Who uses a port: listening sockets by default, all connections on demand; the header search filters by port or name.</summary>
public sealed partial class PortsViewModel(MainViewModel main) : ToolPageViewModel(main)
{
    private IReadOnlyList<ConnectionInfo> _all = [];

    public override string TitleKey => "NavPorts";

    public override string IconKey => "IconLan";

    public override string Id => "ports";

    public override string SearchHintKey => "SearchPorts";

    public override SampleDemand Demand => SampleDemand.Processes;

    public ObservableCollection<PortRow> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private PortRow? _selected;

    [ObservableProperty]
    private bool _onlyListening = true;

    private ListSort? _sortState;

    private ListSort ListSortState => _sortState ??= new ListSort("ports", Settings, Main.SaveSettingsSoon, "port", false, "pid");

    public string SortColumn => ListSortState.Column;

    public bool SortDescending => ListSortState.Descending;

    [RelayCommand]
    private void Sort(string column)
    {
        ListSortState.Toggle(column);
        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortDescending));
        Apply();
    }


    private readonly StatPill _pillEntries = new("PillEntries");
    private readonly StatPill _pillProcesses = new("PillProcesses");

    /// <summary>Key figures of the toolbar, each in its own pill.</summary>
    public IReadOnlyList<StatPill> Stats => [_pillEntries, _pillProcesses];


    [ObservableProperty]
    private string _summary = "";

    public bool HasSelection => Selected is not null;

    partial void OnOnlyListeningChanged(bool value) => Apply();

    protected override void OnActivated() => Refresh();

    public override void OnSearchChanged() => Apply();

    public override async void Refresh()
    {
        _all = await Task.Run(ConnectionReader.ReadAll);
        Apply();
    }

    private void Apply()
    {
        var query = Main.SearchText.Trim();
        var exactPort = int.TryParse(query, out var port) ? port : -1;
        var rows = _all
            .Where(c => !OnlyListening || c.Protocol is ConnectionProtocol.Udp or ConnectionProtocol.Udp6 || c.State.Equals("Listen", StringComparison.OrdinalIgnoreCase))
            .Select(c =>
            {
                var (name, icon, _) = Describe(c.ProcessId, c.ProcessId == 4 ? "System" : $"PID {c.ProcessId}");
                return new PortRow(c, name, icon);
            })
            .Where(r => exactPort >= 0
                ? r.Info.LocalPort == exactPort || r.Info.RemotePort == exactPort
                : Search.IsEmpty || Search.Matches(r.Pid, -1, r.Name, r.Port.ToString(Fmt.Culture), r.Local, r.Remote, r.Protocol))
            .ToList();
        rows.Sort(ListSortState.Comparer(new Dictionary<string, Comparison<PortRow>>
        {
            ["port"] = (a, b) => a.Port.CompareTo(b.Port),
            ["protocol"] = (a, b) => string.CompareOrdinal(a.Protocol, b.Protocol),
            ["process"] = (a, b) => ListSort.Text(a.Name, b.Name),
            ["local"] = (a, b) => string.CompareOrdinal(a.Local, b.Local),
            ["remote"] = (a, b) => string.CompareOrdinal(a.Remote, b.Remote),
            ["state"] = (a, b) => string.CompareOrdinal(a.State, b.State),
            ["pid"] = (a, b) => a.Pid.CompareTo(b.Pid)
        }, (a, b) => a.Port != b.Port ? a.Port.CompareTo(b.Port) : string.CompareOrdinal(a.Protocol, b.Protocol)));

        var selectedKey = Selected?.Info.Key;
        Items.Clear();
        foreach (var row in rows)
        {
            Items.Add(row);
        }

        Selected = selectedKey is null ? null : Items.FirstOrDefault(r => r.Info.Key == selectedKey);
        Summary = Loc.F("PortsSummary", rows.Count, rows.Select(r => r.Pid).Distinct().Count());
        _pillEntries.Set(rows.Count);
        _pillProcesses.Set(rows.Select(r => r.Pid).Distinct().Count());
    }

    [RelayCommand]
    private async Task EndProcess()
    {
        if (Selected is { } row)
        {
            await EndProcessAsync(row.Pid);
            await Task.Delay(400);
            Refresh();
        }
    }

    [RelayCommand]
    private void ShowInProcesses()
    {
        if (Selected is { } row)
        {
            ShowProcess(row.Pid);
        }
    }

    [RelayCommand]
    private async Task CopyRow()
    {
        if (Selected is { } row)
        {
            await Host.CopyAsync($"{row.Protocol}\t{row.Local}:{row.Port}\t{row.Remote}\t{row.State}\t{row.Name}\t{row.Pid}");
            Main.ShowToast(Loc.T("ToastCopied"));
        }
    }
}

// ================================================================================================== power plans

public sealed partial class PowerPlanRow(PowerPlan plan) : ObservableObject
{
    public PowerPlan Plan { get; } = plan;

    public string Name => Plan.Name;

    public string Id => Plan.Id.ToString();

    [ObservableProperty]
    private bool _isActive = plan.Active;
}

/// <summary>Power plans: see and switch the active plan (like powercfg /setactive).</summary>
public sealed partial class PowerPlansViewModel(MainViewModel main) : ToolPageViewModel(main)
{
    public override string TitleKey => "NavPowerPlans";

    public override string IconKey => "IconPowerPlug";

    public override string Id => "power";

    public override bool SupportsSearch => false;

    public ObservableCollection<PowerPlanRow> Items { get; } = [];

    [ObservableProperty]
    private string _activeName = "–";

    [ObservableProperty]
    private bool _isEmpty;

    protected override void OnActivated() => Refresh();

    public override void Refresh()
    {
        Items.Clear();
        foreach (var plan in SystemTools.GetPowerPlans())
        {
            Items.Add(new PowerPlanRow(plan));
        }

        ActiveName = Items.FirstOrDefault(p => p.IsActive)?.Name ?? "–";
        IsEmpty = Items.Count == 0;
    }

    [RelayCommand]
    private void ActivatePlan(PowerPlanRow? row)
    {
        if (row is null || row.IsActive)
        {
            return;
        }

        var result = SystemTools.SetActivePowerPlan(row.Plan.Id);
        Main.Report(result, Loc.F("ToastPowerPlan", row.Name));
        Refresh();
    }

    [RelayCommand]
    private void OpenPowerOptions() => AppEnvironment.Open("powercfg.cpl");
}

// ================================================================================================== system report

/// <summary>Exports a self-contained HTML report (system, load, top processes, sessions, services) – created locally.</summary>
public sealed partial class SystemReportViewModel(MainViewModel main) : ToolPageViewModel(main)
{
    public override string TitleKey => "NavReport";

    public override string IconKey => "IconReport";

    public override string Id => "report";

    public override bool SupportsSearch => false;

    public override SampleDemand Demand => SampleDemand.Processes;

    [ObservableProperty]
    private bool _includeServices = true;

    [ObservableProperty]
    private bool _includeSessions = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _lastFile;

    [RelayCommand]
    private async Task Export()
    {
        if (IsBusy)
        {
            return;
        }

        var name = $"TaskNova_{Main.SystemInfo.ComputerName}_{DateTime.Now:yyyyMMdd_HHmm}.html";
        var file = await Host.SaveFileAsync(Loc.T("DlgReportTitle"), name, Loc.T("FileTypeHtml"), "html");
        if (file is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var processes = Main.Processes.Rows.Where(r => r.Pid != 0)
                .Select(r => new ReportProcess(r.DisplayName, r.Pid, r.UserName, r.Cpu, r.Memory, r.Threads, r.Handles, r.Path))
                .ToList();
            var snapshot = Main.Last;
            var info = Main.SystemInfo;
            var withServices = IncludeServices;
            var withSessions = IncludeSessions;
            await Task.Run(() =>
            {
                IReadOnlyList<ServiceInfo> services = withServices ? ServiceManager.ReadAll(withConfiguration: false) : [];
                IReadOnlyList<SessionInfo> sessions = withSessions ? SessionManager.ReadAll() : [];
                var html = SystemReport.Build(info, snapshot, processes, services, sessions, AppEnvironment.Version, DateTime.Now);
                File.WriteAllText(file, html);
            });
            LastFile = file;
            Main.ShowToast(Loc.F("ToastReportSaved", Path.GetFileName(file)));
            AppEnvironment.Open(file);
        }
        catch (Exception ex)
        {
            Main.ShowToast(Loc.F("ToastActionFailed", ex.Message), error: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenLast()
    {
        if (LastFile is { } file && File.Exists(file))
        {
            ProcessActions.OpenFileLocation(file);
        }
    }
}
