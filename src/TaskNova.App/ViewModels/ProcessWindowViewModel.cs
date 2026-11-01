using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNova.App.Localization;
using TaskNova.Core.Formatting;
using TaskNova.Core.Metrics;
using TaskNova.Core.Models;
using TaskNova.Platform.Windows;

namespace TaskNova.App.ViewModels;

/// <summary>One fact line of the process window (label + value, selectable).</summary>
public sealed record FactLine(string Label, string Value);

/// <summary>
/// Process window: facts, live CPU / memory charts of this process and its loaded modules. It keeps the process list
/// sampled while open (also when the main window shows another page) and stops when it closes.
/// </summary>
public sealed partial class ProcessWindowViewModel : ObservableObject, IDisposable
{
    private const int HistoryPoints = 120;
    private SystemSnapshot? _sampled;
    private int _factsVersion = -1;
    private bool? _factsSigned;

    public ProcessWindowViewModel(MainViewModel main, ProcessRow row)
    {
        Main = main;
        Row = row;
        main.AddDemand(SampleDemand.Processes);
        main.StateChanged += Update;
        BuildFacts();
        Update();
    }

    public MainViewModel Main { get; }

    public ProcessRow Row { get; }

    public string Title => $"{Row.DisplayName} · PID {Row.Pid}";

    public RingBuffer CpuHistory { get; } = new(HistoryPoints);

    /// <summary>Private working set in MB.</summary>
    public RingBuffer MemoryHistory { get; } = new(HistoryPoints);

    public bool ChartFill => Main.ChartFill;

    public ObservableCollection<FactLine> Facts { get; } = [];

    public ObservableCollection<ModuleEntry> Modules { get; } = [];

    private IReadOnlyList<ModuleEntry> _modules = [];

    private ListSort? _sortState;

    private ListSort ListSortState => _sortState ??= new ListSort("modules", Main.Settings, Main.SaveSettingsSoon, "name", false, "size");

    public string SortColumn => ListSortState.Column;

    public bool SortDescending => ListSortState.Descending;

    [RelayCommand]
    private void Sort(string column)
    {
        ListSortState.Toggle(column);
        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortDescending));
        ShowModules();
    }

    private void ShowModules()
    {
        var list = _modules.ToList();
        list.Sort(ListSortState.Comparer(new Dictionary<string, Comparison<ModuleEntry>>
        {
            ["name"] = (a, b) => ListSort.Text(a.Name, b.Name),
            ["path"] = (a, b) => ListSort.Text(a.Path, b.Path),
            ["base"] = (a, b) => a.BaseAddress.CompareTo(b.BaseAddress),
            ["size"] = (a, b) => a.Size.CompareTo(b.Size)
        }, (a, b) => ListSort.Text(a.Path, b.Path)));
        Modules.Clear();
        foreach (var module in list)
        {
            Modules.Add(module);
        }
    }

    [ObservableProperty]
    private string _cpuText = "–";

    [ObservableProperty]
    private string _memoryText = "–";

    [ObservableProperty]
    private string _threadsText = "–";

    [ObservableProperty]
    private string _handlesText = "–";

    [ObservableProperty]
    private bool _hasEnded;

    [ObservableProperty]
    private bool _modulesLoading;

    [ObservableProperty]
    private string _modulesSummary = "";

    private void BuildFacts()
    {
        _factsVersion = Main.Processes.DetailsVersion;
        _factsSigned = Row.Signed;
        Facts.Clear();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Facts.Add(new FactLine(Loc.T(key), value));
            }
        }

        Add("ColName", Row.Name);
        Add("ColDescription", Row.Description);
        Add("ColCompany", Row.Company);
        Add("ColPath", Row.Path);
        Add("ColCommandLine", Row.CommandLine);
        Add("ColUser", Row.UserName);
        Add("ColSession", Row.SessionId.ToString(Fmt.Culture));
        var parent = Main.Processes.FindByPid(Row.ParentPid);
        Add("FactParent", parent is null ? Row.ParentPid.ToString(Fmt.Culture) : $"{parent.DisplayName} ({Row.ParentPid})");
        if (Row.Key.CreateTime > 0)
        {
            Add("FactStarted", DateTime.FromFileTime(Row.Key.CreateTime).ToString("g", Fmt.Culture));
        }

        Add("ColPlatform", Row.Platform);
        Add("ColPriority", Row.PriorityText);
        Add("FactSignature", Row.Signed switch
        {
            true => Loc.T("FactSigned") + (Row.Signer is { } signer ? " · " + signer : ""),
            false => Loc.T("FactUnsigned"),
            _ => null
        });
    }

    private void Update()
    {
        // StateChanged also fires for pause, language … and for snapshots without the process list: one point per new process sample.
        var snapshot = Main.Last;
        if (snapshot is null || ReferenceEquals(snapshot, _sampled) || !snapshot.Demand.HasFlag(SampleDemand.Processes))
        {
            return;
        }

        _sampled = snapshot;
        if (Main.Processes.Find(Row.Key) is null)
        {
            HasEnded = Main.Processes.HasData;
            return;
        }

        CpuHistory.Add(Row.Cpu);
        MemoryHistory.Add(Row.Memory / (1024d * 1024d));
        CpuText = Row.CpuText;
        MemoryText = Row.MemoryText;
        ThreadsText = Fmt.Number(Row.Threads);
        HandlesText = Fmt.Number(Row.Handles);

        // Path, description, user and signature arrive after the window may have opened.
        if (_factsVersion != Main.Processes.DetailsVersion || _factsSigned != Row.Signed)
        {
            BuildFacts();
            OnPropertyChanged(nameof(Title));
        }
    }

    [RelayCommand]
    private async Task LoadModules()
    {
        if (ModulesLoading)
        {
            return;
        }

        ModulesLoading = true;
        try
        {
            var pid = Row.Pid;
            var (modules, error) = await Task.Run(() => ProcessTools.Modules(pid));
            _modules = modules;
            ShowModules();

            ModulesSummary = error is not null ? Loc.F("ToastActionFailed", error) : Loc.F("ModulesSummary", modules.Count);
        }
        finally
        {
            ModulesLoading = false;
        }
    }

    [RelayCommand]
    private void OpenLocation() => ProcessActions.OpenFileLocation(Row.Path);

    [RelayCommand]
    private void ShowInProcesses()
    {
        Main.Host.ShowMainWindow?.Invoke();
        Main.SearchOn(Main.ProcessesPage, "");
        Main.ProcessesPage.Reveal(Row);
    }

    [RelayCommand]
    private Task End() => ProcessCommands.EndAsync(Main, Row, tree: false);

    public void Dispose()
    {
        Main.StateChanged -= Update;
        Main.RemoveDemand(SampleDemand.Processes);
    }
}
