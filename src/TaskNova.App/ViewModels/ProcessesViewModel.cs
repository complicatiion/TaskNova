using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNova.App.Localization;
using TaskNova.Core.Formatting;
using TaskNova.Core.Models;
using TaskNova.Platform.Windows;

namespace TaskNova.App.ViewModels;

/// <summary>
/// Processes page: grouped like the Windows Task Manager (Apps, Background processes, Windows processes) or as a
/// parent → child tree. Live values, heat cells, sorting by column, search, and all process actions.
/// </summary>
public sealed partial class ProcessesViewModel : PageViewModel
{
    private readonly Dictionary<ProcessGroup, GroupRow> _groups = new();
    private int _builtStructure = -1;
    private int _builtDetails = -1;
    private string? _builtSort;

    public ProcessesViewModel(MainViewModel main) : base(main)
    {
        Columns = new ProcessColumns(main.Settings, main.SaveSettingsSoon);
        foreach (var group in new[] { ProcessGroup.App, ProcessGroup.Background, ProcessGroup.Windows })
        {
            var row = new GroupRow(group) { IsExpanded = !main.Settings.CollapsedGroups.Contains(group.ToString()) };
            row.ExpandedChanged += OnGroupExpanded;
            _groups[group] = row;
        }

        ProcessRow.ExpandedChanged += _ =>
        {
            if (IsActive && TreeMode)
            {
                Rebuild(force: true);
            }
        };
    }

    public override string TitleKey => "NavProcesses";

    public override string IconKey => "IconApplicationOutline";

    public override string Id => "processes";

    public override string SearchHintKey => "SearchProcesses";

    public override SampleDemand Demand => SampleDemand.Processes | SampleDemand.Windows | SampleDemand.Gpu;

    /// <summary>GroupRow and ProcessRow items in display order.</summary>
    public ObservableCollection<object> Items { get; } = [];

    [ObservableProperty]
    private object? _selectedItem;

    /// <summary>Column widths / visibility (drag the header edge, right-click the header).</summary>
    public ProcessColumns Columns { get; }

    public ProcessRow? Selected => SelectedItem as ProcessRow;

    public bool HasSelection => Selected is not null;

    partial void OnSelectedItemChanged(object? value)
    {
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SuspendText));
        OnPropertyChanged(nameof(SuspendIcon));
    }

    public string SuspendText => Loc.T(Selected?.IsSuspended == true ? "ActResume" : "ActSuspend");

    public Avalonia.Media.Geometry? SuspendIcon => Res.Icon(Selected?.IsSuspended == true ? "IconPlay" : "IconSnowflake");

    public bool TreeMode
    {
        get => Settings.ProcessTree;
        set
        {
            if (Settings.ProcessTree != value)
            {
                Settings.ProcessTree = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GroupMode));
                OnPropertyChanged(nameof(TreeChip));
                OnPropertyChanged(nameof(GroupChip));
                Main.SaveSettings();
                Rebuild(force: true);
            }
        }
    }

    public bool GroupMode
    {
        get => !TreeMode;
        set => TreeMode = !value;
    }

    /// <summary>Mode chips: clicking the active chip keeps it active.</summary>
    public bool GroupChip
    {
        get => GroupMode;
        set
        {
            if (value)
            {
                GroupMode = true;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(GroupChip)));
        }
    }

    public bool TreeChip
    {
        get => TreeMode;
        set
        {
            if (value)
            {
                TreeMode = true;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(TreeChip)));
        }
    }

    public bool ShowGpu => Settings.GpuMonitoring && Main.Sampling.HasGpu;

    [ObservableProperty]
    private string _summary = "";

    private readonly StatPill _pillProcesses = new("PillProcesses");
    private readonly StatPill _pillCpu = new("PillCpu");
    private readonly StatPill _pillMemory = new("PillMemory");

    /// <summary>Key figures of the toolbar, each in its own pill.</summary>
    public IReadOnlyList<StatPill> Stats => [_pillProcesses, _pillCpu, _pillMemory];


    // ------------------------------------------------------------------------------------------- sorting

    public string SortColumn => Settings.ProcessSort;

    public bool SortDescending => Settings.ProcessSortDescending;

    [RelayCommand]
    private void Sort(string column)
    {
        if (Settings.ProcessSort == column)
        {
            Settings.ProcessSortDescending = !Settings.ProcessSortDescending;
        }
        else
        {
            Settings.ProcessSort = column;
            // Names ascending, values descending by default (biggest consumers on top).
            Settings.ProcessSortDescending = column != "name";
        }

        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortDescending));
        Main.SaveSettings();
        Rebuild(force: true);
    }

    /// <summary>Sorts by a column descending (overview "Top CPU" / "Top memory" cards).</summary>
    public void SortBy(string column)
    {
        Settings.ProcessSort = column;
        Settings.ProcessSortDescending = column != "name";
        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortDescending));
        Main.SaveSettingsSoon();
        Rebuild(force: true);
    }

    private Comparison<ProcessRow> Comparer()
    {
        Comparison<ProcessRow> compare = Settings.ProcessSort switch
        {
            "cpu" => (a, b) => a.Cpu.CompareTo(b.Cpu),
            "memory" => (a, b) => a.Memory.CompareTo(b.Memory),
            "io" => (a, b) => a.Io.CompareTo(b.Io),
            "gpu" => (a, b) => a.Gpu.CompareTo(b.Gpu),
            "pid" => (a, b) => a.Pid.CompareTo(b.Pid),
            "status" => (a, b) => a.IsSuspended.CompareTo(b.IsSuspended),
            _ => (a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase)
        };

        var descending = Settings.ProcessSortDescending;
        return (a, b) =>
        {
            var result = descending ? compare(b, a) : compare(a, b);
            return result != 0 ? result : a.Pid.CompareTo(b.Pid);
        };
    }

    // ------------------------------------------------------------------------------------------- list

    protected override void OnActivated() => Rebuild(force: true);

    public override void OnSnapshot(SystemSnapshot snapshot)
    {
        Rebuild(force: false);
        OnPropertyChanged(nameof(ShowGpu));
        OnPropertyChanged(nameof(SuspendText));
        OnPropertyChanged(nameof(SuspendIcon));
    }

    public override void OnSearchChanged() => Rebuild(force: true);

    public override void OnSettingsChanged() => OnPropertyChanged(nameof(ShowGpu));

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var group in _groups.Values)
        {
            group.RefreshText();
        }

        OnPropertyChanged(nameof(SuspendText));
        Rebuild(force: true);
    }

    private void OnGroupExpanded(GroupRow group)
    {
        var name = group.Group.ToString();
        Settings.CollapsedGroups.Remove(name);
        if (!group.IsExpanded)
        {
            Settings.CollapsedGroups.Add(name);
        }

        Main.SaveSettings();
        Rebuild(force: true);
    }

    private bool Matches(ProcessRow row) =>
        Settings.ProcessFilter switch
        {
            "suspended" => row.IsSuspended,
            // Efficiency mode sets the idle priority class (base priority 4).
            "efficiency" => row.BasePriority == 4,
            _ => true
        } &&
        (Search.IsEmpty || Search.Matches(row.Pid, row.SessionId, row.Name, row.Description, row.Path, row.UserName, row.Company));

    // ------------------------------------------------------------------ filter chips (all / suspended / efficiency mode)

    public bool FilterAll
    {
        get => Settings.ProcessFilter == "all";
        set => SetFilter(value, "all");
    }

    public bool FilterSuspended
    {
        get => Settings.ProcessFilter == "suspended";
        set => SetFilter(value, "suspended");
    }

    public bool FilterEfficiency
    {
        get => Settings.ProcessFilter == "efficiency";
        set => SetFilter(value, "efficiency");
    }

    private void SetFilter(bool on, string filter)
    {
        if (on && Settings.ProcessFilter != filter)
        {
            Settings.ProcessFilter = filter;
            Main.SaveSettingsSoon();
            Rebuild(force: true);
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(FilterAll));
            OnPropertyChanged(nameof(FilterSuspended));
            OnPropertyChanged(nameof(FilterEfficiency));
        });
    }

    /// <summary>
    /// Rebuilds the display order. Value sorts change every tick; name/PID sorts only when processes start or end.
    /// </summary>
    private void Rebuild(bool force)
    {
        var store = Main.Processes;
        var sortKey = Settings.ProcessSort + Settings.ProcessSortDescending;
        // Value sorts and state filters (suspended, efficiency) change with every tick.
        var valueSort = Settings.ProcessSort is "cpu" or "memory" or "io" or "gpu" || Settings.ProcessFilter != "all";
        if (!force && !valueSort && _builtStructure == store.StructureVersion && _builtDetails == store.DetailsVersion && _builtSort == sortKey)
        {
            UpdateSummary();
            return;
        }

        _builtStructure = store.StructureVersion;
        _builtDetails = store.DetailsVersion;
        _builtSort = sortKey;

        var compare = Comparer();
        var desired = new List<object>(store.Rows.Count + 3);
        if (TreeMode)
        {
            BuildTree(store.Rows.Where(r => r.Pid != 0).ToList(), compare, desired);
        }
        else
        {
            foreach (var group in new[] { ProcessGroup.App, ProcessGroup.Background, ProcessGroup.Windows })
            {
                var members = store.Rows.Where(r => r.Pid != 0 && r.Group == group && Matches(r)).ToList();
                if (members.Count == 0)
                {
                    continue;
                }

                members.Sort(compare);
                var header = _groups[group];
                header.Count = members.Count;
                desired.Add(header);
                if (header.IsExpanded)
                {
                    foreach (var row in members)
                    {
                        row.Depth = 0;
                        row.HasChildren = false;
                        desired.Add(row);
                    }
                }
            }
        }

        ListSync.Sync(Items, desired, () => SelectedItem, v => SelectedItem = v);
        UpdateSummary();
    }

    private void BuildTree(List<ProcessRow> rows, Comparison<ProcessRow> compare, List<object> desired)
    {
        var byPid = new Dictionary<int, ProcessRow>(rows.Count);
        foreach (var row in rows)
        {
            byPid[row.Pid] = row;
        }

        // A parent only counts when it started before the child (process ids are reused).
        ProcessRow? ParentOf(ProcessRow row) =>
            byPid.TryGetValue(row.ParentPid, out var parent) && parent != row
            && (parent.Key.CreateTime < row.Key.CreateTime || (parent.Key.CreateTime == row.Key.CreateTime && parent.Pid < row.Pid))
                ? parent
                : null;

        var children = new Dictionary<ProcessRow, List<ProcessRow>>();
        var roots = new List<ProcessRow>();
        foreach (var row in rows)
        {
            var parent = ParentOf(row);
            if (parent is null)
            {
                roots.Add(row);
            }
            else
            {
                if (!children.TryGetValue(parent, out var list))
                {
                    list = [];
                    children[parent] = list;
                }

                list.Add(row);
            }
        }

        // Searching: matches plus their ancestors stay visible, everything shown expanded.
        HashSet<ProcessRow>? visible = null;
        if (!Search.IsEmpty)
        {
            visible = [];
            foreach (var row in rows.Where(Matches))
            {
                var current = row;
                var guard = 0;
                while (current is not null && visible.Add(current) && guard++ < 64)
                {
                    current = ParentOf(current);
                }
            }
        }

        void Add(ProcessRow row, int depth)
        {
            if (visible is not null && !visible.Contains(row))
            {
                return;
            }

            row.Depth = depth;
            var kids = children.GetValueOrDefault(row);
            row.HasChildren = kids is { Count: > 0 };
            desired.Add(row);
            if (kids is null || depth > 48 || (!row.IsExpanded && visible is null))
            {
                return;
            }

            kids.Sort(compare);
            foreach (var child in kids)
            {
                Add(child, depth + 1);
            }
        }

        roots.Sort(compare);
        foreach (var root in roots)
        {
            Add(root, 0);
        }
    }

    private void UpdateSummary()
    {
        var last = Main.Last;
        var count = Main.Processes.Rows.Count(r => r.Pid != 0);
        Summary = last is null
            ? ""
            : Loc.F("ProcessesSummary", count, Fmt.Percent(last.Cpu.TotalPercent), Fmt.Percent(last.Memory.UsedPercent));
        if (last is not null)
        {
            _pillProcesses.Set(count);
            _pillCpu.Value = Fmt.Percent(last.Cpu.TotalPercent);
            _pillMemory.Value = Fmt.Percent(last.Memory.UsedPercent);
        }
    }

    /// <summary>Selects a row from another page: expands its group and shows all processes first.</summary>
    public void Reveal(ProcessRow row)
    {
        if (!TreeMode && _groups.TryGetValue(row.Group, out var group) && !group.IsExpanded)
        {
            group.IsExpanded = true;
        }

        Rebuild(force: true);
        SelectedItem = row;
    }

    // ------------------------------------------------------------------------------------------- actions

    [RelayCommand]
    private Task EndTask() => ProcessCommands.EndAsync(Main, Selected, tree: false);

    [RelayCommand]
    private Task EndTree() => ProcessCommands.EndAsync(Main, Selected, tree: true);

    [RelayCommand]
    private Task ToggleSuspend() => ProcessCommands.ToggleSuspendAsync(Main, Selected);

    [RelayCommand]
    private Task SetPriority(string level) => ProcessCommands.SetPriorityAsync(Main, Selected, level);

    [RelayCommand]
    private Task Affinity() => ProcessCommands.AffinityAsync(Main, Selected);

    [RelayCommand]
    private Task Dump() => ProcessCommands.DumpAsync(Main, Selected);

    [RelayCommand]
    private void Efficiency() => ProcessCommands.ToggleEfficiency(Main, Selected);

    [RelayCommand]
    private Task RestartProcess() => ProcessCommands.RestartAsync(Main, Selected);

    [RelayCommand]
    private Task WaitChain() => ProcessCommands.WaitChainAsync(Main, Selected);

    [RelayCommand]
    private void ProcessWindow() => ProcessCommands.ShowDetailsWindow(Main, Selected);

    [RelayCommand]
    private void OpenLocation() => ProcessActions.OpenFileLocation(Selected?.Path);

    [RelayCommand]
    private void Properties() => ProcessCommands.Properties(Main, Selected);

    [RelayCommand]
    private Task Copy() => ProcessCommands.CopyAsync(Main, Selected);

    [RelayCommand]
    private void GoToDetails()
    {
        if (Selected is { } row)
        {
            Main.SearchOn(Main.Details, $"pid:{row.Pid}");
        }
    }

    [RelayCommand]
    private void GoToServices()
    {
        if (Selected is { } row)
        {
            Main.SearchOn(Main.Services, $"pid:{row.Pid}");
        }
    }

    [RelayCommand]
    private void ToggleExpand(ProcessRow? row)
    {
        if (row is not null)
        {
            row.IsExpanded = !row.IsExpanded;
        }
    }
}

/// <summary>Process actions shared by the Processes and Details pages (with confirmations and toasts).</summary>
public static class ProcessCommands
{
    public static async Task EndAsync(MainViewModel main, ProcessRow? row, bool tree)
    {
        if (row is null)
        {
            return;
        }

        if (row.Pid == Environment.ProcessId)
        {
            main.Host.ExitApplication?.Invoke();
            return;
        }

        if (row.IsCritical)
        {
            var critical = await main.Host.ConfirmAsync(Loc.T("DlgCriticalTitle"), Loc.F("DlgCriticalText", row.Name), Loc.T("ActEndAnyway"),
                danger: true, confirmIcon: "IconCloseOctagon");
            if (!critical.Confirmed)
            {
                return;
            }
        }
        else if (main.Settings.ConfirmEndTask)
        {
            var answer = await main.Host.ConfirmAsync(
                Loc.F(tree ? "DlgEndTreeTitle" : "DlgEndTitle", row.DisplayName),
                Loc.T(tree ? "DlgEndTreeText" : "DlgEndText"),
                Loc.T(tree ? "ActEndTree" : "ActEndTask"),
                danger: true,
                optionText: Loc.T("DlgDontAskAgain"),
                confirmIcon: "IconCloseOctagon");
            if (!answer.Confirmed)
            {
                return;
            }

            if (answer.OptionChecked)
            {
                main.Settings.ConfirmEndTask = false;
                main.SaveSettings();
            }
        }

        var result = tree ? ProcessActions.TerminateTree(row.Key, main.Processes.Tree()) : ProcessActions.Terminate(row.Pid);
        main.Report(result, Loc.F("ToastEnded", row.DisplayName));
        main.Sampling.RefreshNow();
    }

    /// <summary>Full memory dump (.dmp) for WinDbg / Visual Studio – written in the background, can be large.</summary>
    public static async Task DumpAsync(MainViewModel main, ProcessRow? row)
    {
        if (row is null)
        {
            return;
        }

        var name = $"{Path.GetFileNameWithoutExtension(row.Name)}_{row.Pid}_{DateTime.Now:yyyyMMdd_HHmmss}.dmp";
        var file = await main.Host.SaveFileAsync(Loc.T("DlgDumpTitle"), name, Loc.T("FileTypeDump"), "dmp");
        if (file is null)
        {
            return;
        }

        main.ShowToast(Loc.F("ToastDumpRunning", row.DisplayName));
        var pid = row.Pid;
        var result = await Task.Run(() => SystemTools.WriteDump(pid, file));
        main.Report(result, Loc.F("ToastDumpDone", Path.GetFileName(file)));
        if (result.Success)
        {
            ProcessActions.OpenFileLocation(file);
        }
    }

    /// <summary>Efficiency mode on/off (EcoQoS + idle priority) – the current state is read at the click.</summary>
    public static void ToggleEfficiency(MainViewModel main, ProcessRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.IsCritical || row.Pid is 0 or 4)
        {
            main.ShowToast(Loc.F("ToastEfficiencyNotAllowed", row.Name), error: true);
            return;
        }

        var on = !ProcessTools.IsEfficiencyMode(row.Pid, row.BasePriority);
        main.Report(ProcessTools.SetEfficiencyMode(row.Pid, on), Loc.F(on ? "ToastEfficiencyOn" : "ToastEfficiencyOff", row.DisplayName));
        main.Sampling.RefreshNow();
    }

    /// <summary>Ends the process and starts the same program with the same arguments again (as the TaskNova user).</summary>
    public static async Task RestartAsync(MainViewModel main, ProcessRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.IsCritical || row.Pid == Environment.ProcessId || row.Path is null ||
            string.Equals(row.Name, "svchost.exe", StringComparison.OrdinalIgnoreCase))
        {
            main.ShowToast(Loc.F("ToastRestartNotAllowed", row.Name), error: true);
            return;
        }

        var answer = await main.Host.ConfirmAsync(Loc.F("DlgRestartTitle", row.DisplayName), Loc.T("DlgRestartText"), Loc.T("ActRestartProcess"),
            danger: true, confirmIcon: "IconRestart");
        if (!answer.Confirmed)
        {
            return;
        }

        var pid = row.Pid;
        var path = row.Path;
        var commandLine = row.CommandLine;
        var result = await Task.Run(() => ProcessTools.Restart(pid, path, commandLine));
        main.Report(result, Loc.F("ToastRestarted", row.DisplayName));
        main.Sampling.RefreshNow();
    }

    /// <summary>"Analyze wait chain": which locks, messages or other processes the threads are waiting for.</summary>
    public static async Task WaitChainAsync(MainViewModel main, ProcessRow? row)
    {
        if (row is null)
        {
            return;
        }

        var pid = row.Pid;
        var (chains, error) = await Task.Run(() => ProcessTools.AnalyzeWaitChain(pid));
        if (error is not null)
        {
            main.ShowToast(Loc.F("ToastActionFailed", error), error: true);
            return;
        }

        var text = new System.Text.StringBuilder();
        if (chains.Count == 0)
        {
            text.AppendLine(Loc.F("WaitNone", row.DisplayName, pid));
        }
        else
        {
            text.AppendLine(Loc.F("WaitFound", row.DisplayName, pid, chains.Count));
            if (chains.Any(c => c.IsDeadlock))
            {
                text.AppendLine(Loc.T("WaitDeadlock"));
            }

            foreach (var chain in chains)
            {
                text.AppendLine();
                text.AppendLine(Loc.F("WaitThread", chain.ThreadId) + (chain.IsDeadlock ? "  ⟲ " + Loc.T("WaitCycle") : ""));
                var indent = "  ";
                foreach (var node in chain.Nodes.Skip(1))
                {
                    string line;
                    if (node.IsThread)
                    {
                        var owner = main.Processes.FindByPid(node.ProcessId)?.DisplayName ?? $"PID {node.ProcessId}";
                        line = Loc.F("WaitOwnerThread", node.ThreadId, owner, node.ProcessId, Loc.T("WaitStatus" + node.Status));
                    }
                    else
                    {
                        line = Loc.T("WaitType" + node.ObjectType) + (node.Name is { } name ? $" \"{name}\"" : "") + $" ({Loc.T("WaitStatus" + node.Status)})";
                    }

                    text.Append(indent).Append("→ ").AppendLine(line);
                    indent += "  ";
                }
            }

            text.AppendLine();
            text.AppendLine(Loc.T("WaitHint"));
        }

        await main.Host.ShowTextAsync(Loc.F("WaitTitle", row.DisplayName), text.ToString());
    }

    /// <summary>Own window with facts, live charts and the loaded modules of the process.</summary>
    public static void ShowDetailsWindow(MainViewModel main, ProcessRow? row)
    {
        if (row is not null)
        {
            main.Host.ShowProcessWindow(main, row);
        }
    }

    public static async Task ToggleSuspendAsync(MainViewModel main, ProcessRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.IsSuspended)
        {
            main.Report(ProcessActions.Resume(row.Pid), Loc.F("ToastResumed", row.DisplayName));
            main.Sampling.RefreshNow();
            return;
        }

        if (row.IsCritical || row.Pid == Environment.ProcessId)
        {
            main.ShowToast(Loc.F("ToastCannotSuspend", row.Name), error: true);
            return;
        }

        var answer = await main.Host.ConfirmAsync(Loc.F("DlgSuspendTitle", row.DisplayName), Loc.T("DlgSuspendText"), Loc.T("ActSuspend"),
            danger: false, confirmIcon: "IconSnowflake");
        if (answer.Confirmed)
        {
            main.Report(ProcessActions.Suspend(row.Pid), Loc.F("ToastSuspended", row.DisplayName));
            main.Sampling.RefreshNow();
        }
    }

    public static async Task SetPriorityAsync(MainViewModel main, ProcessRow? row, string level)
    {
        if (row is null || !Enum.TryParse<PriorityLevel>(level, out var priority))
        {
            return;
        }

        if (priority == PriorityLevel.Realtime)
        {
            var answer = await main.Host.ConfirmAsync(Loc.T("DlgRealtimeTitle"), Loc.T("DlgRealtimeText"), Loc.T("PriorityRealtime"),
                danger: true, confirmIcon: "IconSpeedometer");
            if (!answer.Confirmed)
            {
                return;
            }
        }

        main.Report(ProcessActions.SetPriority(row.Pid, priority), Loc.F("ToastPriority", row.DisplayName, Loc.T("Priority" + priority)));
        main.Sampling.RefreshNow();
    }

    public static async Task AffinityAsync(MainViewModel main, ProcessRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (ProcessActions.GetAffinity(row.Pid) is not { } affinity)
        {
            main.ShowToast(Loc.F("ToastAccessDenied", row.Name), error: true);
            return;
        }

        var processors = Math.Min(64, main.Sampling.LogicalProcessors);
        var mask = await main.Host.AffinityAsync(row.DisplayName, affinity.Process, affinity.System, processors);
        if (mask is { } value)
        {
            main.Report(ProcessActions.SetAffinity(row.Pid, value), Loc.F("ToastAffinity", row.DisplayName));
        }
    }

    public static void Properties(MainViewModel main, ProcessRow? row)
    {
        if (row?.Path is null || !ProcessActions.ShowProperties(row.Path, main.Host.MainHandle))
        {
            main.ShowToast(Loc.T("ToastNoPath"), error: true);
        }
    }

    public static async Task CopyAsync(MainViewModel main, ProcessRow? row)
    {
        if (row is null)
        {
            return;
        }

        var text = new StringBuilder()
            .AppendLine($"{Loc.T("ColName")}: {row.Name}")
            .AppendLine($"{Loc.T("ColDescription")}: {row.Description}")
            .AppendLine($"PID: {row.Pid}")
            .AppendLine($"{Loc.T("ColUser")}: {row.UserName}")
            .AppendLine($"{Loc.T("ColPath")}: {row.Path}")
            .AppendLine($"{Loc.T("ColCommandLine")}: {row.CommandLine}")
            .AppendLine($"{Loc.T("ColCompany")}: {row.Company}")
            .ToString();
        await main.Host.CopyAsync(text);
        main.ShowToast(Loc.T("ToastCopied"));
    }
}
