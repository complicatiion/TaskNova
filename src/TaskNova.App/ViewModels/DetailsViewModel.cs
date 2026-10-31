using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNova.Core.Models;
using TaskNova.Platform.Windows;

namespace TaskNova.App.ViewModels;

/// <summary>Details page: every process (including System Idle Process) as a flat table with all columns.</summary>
public sealed partial class DetailsViewModel(MainViewModel main) : PageViewModel(main)
{
    private int _builtStructure = -1;
    private int _builtDetails = -1;
    private string? _builtSort;

    public override string TitleKey => "NavDetails";

    public override string IconKey => "IconFormatListBulleted";

    public override string Id => "details";

    public override string SearchHintKey => "SearchDetails";

    public override SampleDemand Demand => SampleDemand.Processes | SampleDemand.Gpu;

    public ObservableCollection<ProcessRow> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ProcessRow? _selected;

    public bool HasSelection => Selected is not null;

    [ObservableProperty]
    private string _summary = "";

    private readonly StatPill _pillProcesses = new("PillProcesses");

    /// <summary>Key figures of the toolbar, each in its own pill.</summary>
    public IReadOnlyList<StatPill> Stats => [_pillProcesses];


    public string SortColumn => Settings.DetailsSort;

    public bool SortDescending => Settings.DetailsSortDescending;

    [RelayCommand]
    private void Sort(string column)
    {
        if (Settings.DetailsSort == column)
        {
            Settings.DetailsSortDescending = !Settings.DetailsSortDescending;
        }
        else
        {
            Settings.DetailsSort = column;
            Settings.DetailsSortDescending = column is not ("name" or "user" or "description" or "status" or "platform" or "commandline");
        }

        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortDescending));
        Main.SaveSettings();
        Rebuild(force: true);
    }

    protected override void OnActivated() => Rebuild(force: true);

    public override void OnSnapshot(SystemSnapshot snapshot) => Rebuild(force: false);

    public override void OnSearchChanged() => Rebuild(force: true);

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        Rebuild(force: true);
    }

    private void Rebuild(bool force)
    {
        var store = Main.Processes;
        var sortKey = Settings.DetailsSort + Settings.DetailsSortDescending;
        var valueSort = Settings.DetailsSort is "cpu" or "memory" or "io" or "gpu" or "cputime" or "workingset" or "private" or "threads" or "handles";
        if (!force && !valueSort && _builtStructure == store.StructureVersion && _builtDetails == store.DetailsVersion && _builtSort == sortKey)
        {
            return;
        }

        _builtStructure = store.StructureVersion;
        _builtDetails = store.DetailsVersion;
        _builtSort = sortKey;

        var rows = store.Rows
            .Where(r => Search.IsEmpty || Search.Matches(r.Pid, r.SessionId, r.Name, r.Description, r.Path, r.UserName, r.CommandLine, r.Company))
            .ToList();
        rows.Sort(Comparer());
        ListSync.Sync(Items, rows, () => Selected, v => Selected = v);
        Summary = Localization.Loc.F("DetailsSummary", rows.Count, store.Rows.Count);
        _pillProcesses.Value = $"{rows.Count:N0} / {store.Rows.Count:N0}";
    }

    private Comparison<ProcessRow> Comparer()
    {
        Comparison<ProcessRow> compare = Settings.DetailsSort switch
        {
            "pid" => (a, b) => a.Pid.CompareTo(b.Pid),
            "status" => (a, b) => a.IsSuspended.CompareTo(b.IsSuspended),
            "user" => (a, b) => string.Compare(a.UserName, b.UserName, StringComparison.CurrentCultureIgnoreCase),
            "session" => (a, b) => a.SessionId.CompareTo(b.SessionId),
            "cpu" => (a, b) => a.Cpu.CompareTo(b.Cpu),
            "cputime" => (a, b) => a.CpuTime.CompareTo(b.CpuTime),
            "memory" => (a, b) => a.Memory.CompareTo(b.Memory),
            "workingset" => (a, b) => a.WorkingSet.CompareTo(b.WorkingSet),
            "private" => (a, b) => a.PrivateBytes.CompareTo(b.PrivateBytes),
            "threads" => (a, b) => a.Threads.CompareTo(b.Threads),
            "handles" => (a, b) => a.Handles.CompareTo(b.Handles),
            "priority" => (a, b) => a.BasePriority.CompareTo(b.BasePriority),
            "io" => (a, b) => a.Io.CompareTo(b.Io),
            "gpu" => (a, b) => a.Gpu.CompareTo(b.Gpu),
            "platform" => (a, b) => string.Compare(a.Platform, b.Platform, StringComparison.Ordinal),
            "description" => (a, b) => string.Compare(a.Description, b.Description, StringComparison.CurrentCultureIgnoreCase),
            "commandline" => (a, b) => string.Compare(a.CommandLine, b.CommandLine, StringComparison.OrdinalIgnoreCase),
            _ => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
        };

        var descending = Settings.DetailsSortDescending;
        return (a, b) =>
        {
            var result = descending ? compare(b, a) : compare(a, b);
            return result != 0 ? result : a.Pid.CompareTo(b.Pid);
        };
    }

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
    private void GoToServices()
    {
        if (Selected is { } row)
        {
            Main.SearchOn(Main.Services, $"pid:{row.Pid}");
        }
    }
}
