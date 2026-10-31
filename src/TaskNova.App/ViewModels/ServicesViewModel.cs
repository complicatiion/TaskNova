using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNova.App.Localization;
using TaskNova.App.Services;
using TaskNova.Core.Models;
using TaskNova.Platform.Windows;

namespace TaskNova.App.ViewModels;

public sealed partial class ServiceRow(ServiceInfo info) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(PidText), nameof(StartText), nameof(IsRunning), nameof(IsStopped), nameof(StateClass),
        nameof(DisplayName), nameof(Description))]
    private ServiceInfo _info = info;

    public string Name => Info.Name;

    public string DisplayName => Info.DisplayName;

    public string Description => Info.Description ?? "";

    public bool IsRunning => Info.State == ServiceState.Running;

    public bool IsStopped => Info.State == ServiceState.Stopped;

    public string StateText => Loc.T("Svc" + Info.State);

    /// <summary>Badge color: safe (running), neutral (stopped), moderate (pending).</summary>
    public string StateClass => Info.State switch
    {
        ServiceState.Running => "safe",
        ServiceState.Stopped => "neutral",
        _ => "moderate"
    };

    public string PidText => Info.ProcessId > 0 ? Info.ProcessId.ToString() : "";

    public string StartText => Info.StartMode == ServiceStartMode.Unknown ? "" : Loc.T("Start" + Info.StartMode);

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StartText));
    }
}

public sealed record StartModeOption(ServiceStartMode Mode, string Title);

/// <summary>
/// Services through the Service Control Manager API: list with PID, state and start type; start, stop (with dependents),
/// restart, change the start type; dependencies of the selected service.
/// </summary>
public sealed partial class ServicesViewModel : PageViewModel
{
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, ServiceRow> _rows = new(StringComparer.OrdinalIgnoreCase);
    private bool _loading;
    private bool _configLoaded;

    public ServicesViewModel(MainViewModel main) : base(main)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => _ = LoadAsync(withConfiguration: false);
    }

    public override string TitleKey => "NavServices";

    public override string IconKey => "IconCogs";

    public override string Id => "services";

    public override string SearchHintKey => "SearchServices";

    public ObservableCollection<ServiceRow> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ServiceRow? _selected;

    public bool HasSelection => Selected is not null;

    [ObservableProperty]
    private string _summary = "";

    private readonly StatPill _pillServices = new("PillServices");
    private readonly StatPill _pillRunning = new("PillRunning");
    private readonly StatPill _pillShown = new("PillShown");

    /// <summary>Key figures of the toolbar, each in its own pill.</summary>
    public IReadOnlyList<StatPill> Stats => [_pillServices, _pillRunning, _pillShown];


    /// <summary>all, running, stopped</summary>
    [ObservableProperty]
    private string _filter = "all";

    public bool FilterAll
    {
        get => Filter == "all";
        set
        {
            if (value)
            {
                Filter = "all";
            }
            else
            {
                // Clicking the active chip must not uncheck it.
                Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(FilterAll)));
            }
        }
    }

    public bool FilterRunning
    {
        get => Filter == "running";
        set
        {
            if (value)
            {
                Filter = "running";
            }
            else
            {
                // Clicking the active chip must not uncheck it.
                Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(FilterRunning)));
            }
        }
    }

    public bool FilterStopped
    {
        get => Filter == "stopped";
        set
        {
            if (value)
            {
                Filter = "stopped";
            }
            else
            {
                // Clicking the active chip must not uncheck it.
                Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(FilterStopped)));
            }
        }
    }

    partial void OnFilterChanged(string value)
    {
        OnPropertyChanged(nameof(FilterAll));
        OnPropertyChanged(nameof(FilterRunning));
        OnPropertyChanged(nameof(FilterStopped));
        Apply();
    }

    public IReadOnlyList<StartModeOption> StartModes { get; private set; } = BuildStartModes();

    private static IReadOnlyList<StartModeOption> BuildStartModes() =>
    [
        new(ServiceStartMode.Automatic, Loc.T("StartAutomatic")),
        new(ServiceStartMode.AutomaticDelayed, Loc.T("StartAutomaticDelayed")),
        new(ServiceStartMode.Manual, Loc.T("StartManual")),
        new(ServiceStartMode.Disabled, Loc.T("StartDisabled"))
    ];

    // ------------------------------------------------------------------ details of the selection

    [ObservableProperty]
    private string _detailPath = "";

    [ObservableProperty]
    private string _detailAccount = "";

    [ObservableProperty]
    private string _detailDependencies = "";

    [ObservableProperty]
    private string _detailDependents = "";

    [ObservableProperty]
    private StartModeOption? _selectedStartMode;

    private bool _syncingStartMode;

    partial void OnSelectedChanged(ServiceRow? value) => _ = LoadDetailsAsync(value);

    private async Task LoadDetailsAsync(ServiceRow? row)
    {
        if (row is null)
        {
            DetailPath = DetailAccount = DetailDependencies = DetailDependents = "";
            return;
        }

        var (info, dependents) = await Task.Run(() => (ServiceManager.ReadConfiguration(row.Info), ServiceManager.Dependents(row.Name, activeOnly: false)));
        if (!ReferenceEquals(Selected, row))
        {
            return;
        }

        row.Info = info;
        DetailPath = info.BinaryPath ?? "–";
        DetailAccount = info.Account ?? "–";
        DetailDependencies = info.Dependencies.Count == 0 ? "–" : string.Join(", ", info.Dependencies);
        DetailDependents = dependents.Count == 0 ? "–" : string.Join(", ", dependents);
        _syncingStartMode = true;
        SelectedStartMode = StartModes.FirstOrDefault(m => m.Mode == info.StartMode);
        _syncingStartMode = false;
    }

    partial void OnSelectedStartModeChanged(StartModeOption? value)
    {
        if (!_syncingStartMode && value is not null && Selected is { } row && row.Info.StartMode != value.Mode)
        {
            _ = SetStartModeAsync(row, value.Mode);
        }
    }

    private async Task SetStartModeAsync(ServiceRow row, ServiceStartMode mode)
    {
        var result = await Task.Run(() => ServiceManager.SetStartMode(row.Name, mode));
        Main.Report(result, Loc.F("ToastStartMode", row.DisplayName, Loc.T("Start" + mode)));
        await LoadDetailsAsync(row);
    }

    // ------------------------------------------------------------------ list

    protected override void OnActivated()
    {
        _ = LoadAsync(withConfiguration: !_configLoaded);
        _timer.Start();
    }

    protected override void OnDeactivated() => _timer.Stop();

    public override void Refresh() => _ = LoadAsync(withConfiguration: true);

    public override void OnSearchChanged() => Apply();

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        var mode = SelectedStartMode?.Mode;
        StartModes = BuildStartModes();
        OnPropertyChanged(nameof(StartModes));
        _syncingStartMode = true;
        SelectedStartMode = StartModes.FirstOrDefault(m => m.Mode == mode);
        _syncingStartMode = false;
        foreach (var row in _rows.Values)
        {
            row.RefreshTexts();
        }
    }

    private async Task LoadAsync(bool withConfiguration)
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        try
        {
            var services = await Task.Run(() => ServiceManager.ReadAll(withConfiguration));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in services)
            {
                seen.Add(info.Name);
                if (_rows.TryGetValue(info.Name, out var row))
                {
                    // Status refresh keeps the configuration read earlier.
                    var merged = withConfiguration
                        ? info
                        : row.Info with { State = info.State, ProcessId = info.ProcessId, CanStop = info.CanStop, DisplayName = info.DisplayName };
                    if (merged != row.Info)
                    {
                        row.Info = merged;
                    }
                }
                else
                {
                    _rows[info.Name] = new ServiceRow(info);
                }
            }

            foreach (var name in _rows.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _rows.Remove(name);
            }

            _configLoaded |= withConfiguration;
            Apply();
        }
        finally
        {
            _loading = false;
        }
    }

    private void Apply()
    {
        var rows = _rows.Values
            .Where(r => Filter switch
            {
                "running" => r.IsRunning,
                "stopped" => r.IsStopped,
                _ => true
            })
            .Where(r => Search.IsEmpty || Search.Matches(r.Info.ProcessId, -1, r.Name, r.DisplayName, r.Description))
            .OrderBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        ListSync.Sync(Items, rows, () => Selected, v => Selected = v);
        Summary = Loc.F("ServicesSummary", _rows.Count, _rows.Values.Count(r => r.IsRunning), rows.Count);
        _pillServices.Set(_rows.Count);
        _pillRunning.Set(_rows.Values.Count(r => r.IsRunning));
        _pillShown.Set(rows.Count);
    }

    // ------------------------------------------------------------------ actions

    [RelayCommand]
    private async Task Start()
    {
        if (Selected is not { } row)
        {
            return;
        }

        Main.ShowToast(Loc.F("ToastServiceStarting", row.DisplayName));
        var result = await Task.Run(() => ServiceManager.Start(row.Name));
        Main.Report(result, Loc.F("ToastServiceStarted", row.DisplayName));
        await LoadAsync(withConfiguration: false);
    }

    [RelayCommand]
    private async Task Stop()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.F("DlgStopServiceTitle", row.DisplayName), Loc.T("DlgStopServiceText"), Loc.T("ActStop"),
            danger: true, confirmIcon: "IconStop");
        if (!answer.Confirmed)
        {
            return;
        }

        var result = await Task.Run(() => ServiceManager.Stop(row.Name, includeDependents: false));
        if (!result.Success && result.Error == ServiceManager.DependentsRunning)
        {
            var dependents = await Task.Run(() => ServiceManager.Dependents(row.Name, activeOnly: true));
            var withDependents = await Host.ConfirmAsync(Loc.T("DlgDependentsTitle"), Loc.F("DlgDependentsText", string.Join(", ", dependents)),
                Loc.T("ActStopAll"), danger: true, confirmIcon: "IconStop");
            if (!withDependents.Confirmed)
            {
                return;
            }

            result = await Task.Run(() => ServiceManager.Stop(row.Name, includeDependents: true));
        }

        Main.Report(result, Loc.F("ToastServiceStopped", row.DisplayName));
        await LoadAsync(withConfiguration: false);
    }

    [RelayCommand]
    private async Task Restart()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.F("DlgRestartServiceTitle", row.DisplayName), Loc.T("DlgRestartServiceText"), Loc.T("ActRestart"),
            danger: false, confirmIcon: "IconRestart");
        if (!answer.Confirmed)
        {
            return;
        }

        var result = await Task.Run(() => ServiceManager.Restart(row.Name));
        Main.Report(result, Loc.F("ToastServiceRestarted", row.DisplayName));
        await LoadAsync(withConfiguration: false);
    }

    [RelayCommand]
    private void GoToProcess()
    {
        if (Selected is { Info.ProcessId: > 0 } row)
        {
            Main.SearchOn(Main.Details, $"pid:{row.Info.ProcessId}");
        }
    }

    [RelayCommand]
    private void OpenServicesConsole() => AppEnvironment.Open("services.msc");

    [RelayCommand]
    private async Task CopyName()
    {
        if (Selected is { } row)
        {
            await Host.CopyAsync(row.Name);
            Main.ShowToast(Loc.T("ToastCopied"));
        }
    }
}
