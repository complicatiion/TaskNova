using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNova.App.Controls;
using TaskNova.App.Localization;
using TaskNova.App.Services;
using TaskNova.Core.Formatting;
using TaskNova.Core.History;
using TaskNova.Core.Models;
using TaskNova.Core.Search;
using TaskNova.Core.Storage;
using TaskNova.Platform.Windows;

namespace TaskNova.App.ViewModels;

/// <summary>Shell: navigation, header search and metrics, sampling demand, toasts.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _searchTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _saveTimer;
    private bool _windowVisible;
    private bool _miniMonitorOpen;
    private SampleDemand _miniDemand;
    private bool _syncingSelection;

    public MainViewModel(UiHost host, AppSettings settings, StaticSystemInfo system, SamplingService sampling, ProcessEnricher enricher,
        AppHistoryStore appHistory)
    {
        Host = host;
        Settings = settings;
        SystemInfo = system;
        Sampling = sampling;
        AppHistory = appHistory;
        Processes = new ProcessStore(enricher, appHistory);
        Alerts = new AlertService(this);
        Processes.SetActive(false); // started with the window (OnWindowVisibilityChanged)
        Metrics.Configure(settings.HistorySeconds, settings.IntervalMs);

        Overview = new OverviewViewModel(this);
        ProcessesPage = new ProcessesViewModel(this);
        Performance = new PerformanceViewModel(this);
        AppHistoryPage = new AppHistoryViewModel(this);
        Network = new NetworkViewModel(this);
        Details = new DetailsViewModel(this);
        Startup = new StartupViewModel(this);
        Users = new UsersViewModel(this);
        Services = new ServicesViewModel(this);
        FileLocks = new FileLocksViewModel(this);
        Ports = new PortsViewModel(this);
        PowerPlans = new PowerPlansViewModel(this);
        ReportPage = new SystemReportViewModel(this);
        SettingsPage = new SettingsViewModel(this);
        About = new AboutViewModel(this);

        NavGroups =
        [
            new NavGroup(null, [new NavItem(Overview)]),
            new NavGroup("NavGroupMonitor", [new NavItem(ProcessesPage), new NavItem(Performance), new NavItem(AppHistoryPage), new NavItem(Network)]),
            new NavGroup("NavGroupManage", [new NavItem(Details), new NavItem(Startup), new NavItem(Users), new NavItem(Services)]),
            new NavGroup("NavGroupTools", [new NavItem(FileLocks), new NavItem(Ports), new NavItem(PowerPlans), new NavItem(ReportPage)])
        ];
        BottomGroup = new NavGroup(null, [new NavItem(SettingsPage), new NavItem(About)]);
        foreach (var group in NavGroups.Append(BottomGroup))
        {
            group.Activated += OnNavActivated;
        }

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            Matcher = new SearchMatcher(SearchText);
            CurrentPage.OnSearchChanged();
        };

        // Settings are written shortly after every change (not only on exit): a log-off, crash or kill keeps them.
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SettingsStore.Save(Settings);
        };

        _rotationTimer.Tick += (_, _) => Rotate();
        _clockTimer.Tick += (_, _) => UpdateClock();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast = null;
        };

        Sampling.SnapshotReady += OnSnapshot;
        Loc.LanguageChanged += OnLanguageChanged;

        // Start page: a fixed page (default: overview) or "last" = the page of the previous session.
        var startId = settings.StartPage == "last" ? settings.LastPage : settings.StartPage;
        var start = AllPages().FirstOrDefault(p => p.Id == startId && p is not SettingsViewModel and not AboutViewModel) ?? Overview;
        // The page is activated when the window is shown (OnWindowVisibilityChanged).
        _currentPage = start;
        SelectNav(start);
        UpdateDemand();
    }

    public UiHost Host { get; }

    public AppSettings Settings { get; }

    public StaticSystemInfo SystemInfo { get; }

    public SamplingService Sampling { get; }

    public AppHistoryStore AppHistory { get; }

    public ProcessStore Processes { get; }

    public MetricsHistory Metrics { get; } = new();

    public AlertService Alerts { get; }

    public SystemSnapshot? Last { get; private set; }

    public OverviewViewModel Overview { get; }

    public ProcessesViewModel ProcessesPage { get; }

    public PerformanceViewModel Performance { get; }

    public AppHistoryViewModel AppHistoryPage { get; }

    public NetworkViewModel Network { get; }

    public DetailsViewModel Details { get; }

    public StartupViewModel Startup { get; }

    public UsersViewModel Users { get; }

    public ServicesViewModel Services { get; }

    public FileLocksViewModel FileLocks { get; }

    public PortsViewModel Ports { get; }

    public PowerPlansViewModel PowerPlans { get; }

    public SystemReportViewModel ReportPage { get; }

    public SettingsViewModel SettingsPage { get; }

    public AboutViewModel About { get; }

    public ObservableCollection<NavGroup> NavGroups { get; }

    public NavGroup BottomGroup { get; }

    public IEnumerable<PageViewModel> AllPages() =>
    [
        Overview, ProcessesPage, Performance, AppHistoryPage, Network, Details, Startup, Users, Services, FileLocks, Ports, PowerPlans,
        ReportPage, SettingsPage, About
    ];

    /// <summary>Raised after every snapshot (tray tooltip, mini monitor).</summary>
    public event Action? StateChanged;

    // ------------------------------------------------------------------------------------------- navigation

    private PageViewModel _currentPage;

    public PageViewModel CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (ReferenceEquals(_currentPage, value))
            {
                return;
            }

            if (_windowVisible)
            {
                _currentPage.Deactivate();
            }

            // Browser-like history: every page change (sidebar, links, cards) can be undone with Back.
            if (!_historyMove && !_rotating)
            {
                _back.Add(_currentPage);
                if (_back.Count > MaxHistory)
                {
                    _back.RemoveAt(0);
                }

                _forward.Clear();
            }

            SetProperty(ref _currentPage, value);
            NotifyHistory();
            RestartRotation();
            OnPropertyChanged(nameof(CurrentPageIcon));
            OnPropertyChanged(nameof(SearchVisible));
            OnPropertyChanged(nameof(SearchHint));
            if (Settings.LastPage != value.Id)
            {
                Settings.LastPage = value.Id;
                SaveSettingsSoon();
            }

            if (_windowVisible)
            {
                value.Activate();
            }

            UpdateDemand();
            Sampling.RefreshNow();
        }
    }

    public Geometry? CurrentPageIcon => Res.Icon(CurrentPage.IconKey);

    // ------------------------------------------------------------------------------------------- back / forward

    private const int MaxHistory = 50;
    private readonly List<PageViewModel> _back = [];
    private readonly List<PageViewModel> _forward = [];
    private bool _historyMove;

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    public string BackTip => Loc.T("TipBack") + (CanGoBack ? " · " + _back[^1].Title : "");

    public string ForwardTip => Loc.T("TipForward") + (CanGoForward ? " · " + _forward[^1].Title : "");

    private void NotifyHistory()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(BackTip));
        OnPropertyChanged(nameof(ForwardTip));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Back (Alt+←, mouse back button, header / dashboard button).</summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        // The dashboard rotation moves without history: skip entries that are the current page already.
        while (_back.Count > 0 && ReferenceEquals(_back[^1], CurrentPage))
        {
            _back.RemoveAt(_back.Count - 1);
        }

        if (_back.Count == 0)
        {
            NotifyHistory();
            return;
        }

        var target = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        _forward.Add(CurrentPage);
        MoveInHistory(target);
    }

    /// <summary>Forward (Alt+→, mouse forward button).</summary>
    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        while (_forward.Count > 0 && ReferenceEquals(_forward[^1], CurrentPage))
        {
            _forward.RemoveAt(_forward.Count - 1);
        }

        if (_forward.Count == 0)
        {
            NotifyHistory();
            return;
        }

        var target = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        _back.Add(CurrentPage);
        MoveInHistory(target);
    }

    private void MoveInHistory(PageViewModel target)
    {
        _historyMove = true;
        try
        {
            NavigateTo(target);
        }
        finally
        {
            _historyMove = false;
        }

        NotifyHistory();
    }

    public void NavigateTo(PageViewModel page)
    {
        SelectNav(page);
        CurrentPage = page;
    }

    private void SelectNav(PageViewModel page)
    {
        _syncingSelection = true;
        try
        {
            foreach (var group in NavGroups.Append(BottomGroup))
            {
                group.Selected = group.Items.FirstOrDefault(i => ReferenceEquals(i.Page, page));
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void OnNavActivated(NavGroup source, NavItem item)
    {
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            foreach (var group in NavGroups.Append(BottomGroup).Where(g => !ReferenceEquals(g, source)))
            {
                group.Selected = null;
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        CurrentPage = item.Page;
    }

    // ------------------------------------------------------------------------------------------- sidebar

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    public double SidebarWidth => IsSidebarCollapsed ? 68 : 220;

    partial void OnIsSidebarCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(SidebarWidth));
        if (Settings.SidebarCollapsed != value)
        {
            Settings.SidebarCollapsed = value;
            SaveSettingsSoon();
        }
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    // ------------------------------------------------------------------------------------------- dashboard mode

    /// <summary>
    /// Dashboard mode (dedicated monitoring screen): no sidebar and header, full screen. F11 toggles, Esc leaves it.
    /// The window reacts to the property (full screen on/off).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChromeVisible))]
    private bool _isDashboard;

    public bool ChromeVisible => !IsDashboard;

    [RelayCommand]
    private void ToggleDashboard()
    {
        if (!IsDashboard && !ReferenceEquals(CurrentPage, Overview) && !ReferenceEquals(CurrentPage, Performance))
        {
            NavigateTo(Overview);
        }

        IsDashboard = !IsDashboard;
    }

    [RelayCommand]
    private void ExitDashboard() => IsDashboard = false;

    partial void OnIsDashboardChanged(bool value)
    {
        RestartRotation();
        if (value)
        {
            UpdateClock();
            _clockTimer.Start();
        }
        else
        {
            _clockTimer.Stop();
        }

        OnPropertyChanged(nameof(DashboardClockVisible));
    }

    // Dashboard extras: page rotation, computer name + clock.
    private readonly DispatcherTimer _rotationTimer = new();
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _rotating;

    public bool DashboardClockVisible => IsDashboard && Settings.DashboardShowClock;

    [ObservableProperty]
    private string _clockText = "";

    public string ComputerName => SystemInfo.ComputerName;

    private void UpdateClock() => ClockText = DateTime.Now.ToString("t", Fmt.Culture) + " · " + DateTime.Now.ToString("d", Fmt.Culture);

    /// <summary>(Re)starts the rotation timer – also after every manual page change, so a click is not overridden at once.</summary>
    public void RestartRotation()
    {
        _rotationTimer.Stop();
        if (IsDashboard && Settings.DashboardRotate && RotationPages().Count > 1)
        {
            _rotationTimer.Interval = TimeSpan.FromSeconds(Math.Max(5, Settings.DashboardRotateSeconds));
            _rotationTimer.Start();
        }
    }

    private List<PageViewModel> RotationPages() =>
        Settings.DashboardPages.Select(id => AllPages().FirstOrDefault(p => p?.Id == id)).OfType<PageViewModel>().ToList();

    private void Rotate()
    {
        var pages = RotationPages();
        if (pages.Count < 2)
        {
            return;
        }

        var index = pages.IndexOf(CurrentPage);
        _rotating = true;
        try
        {
            NavigateTo(pages[(index + 1) % pages.Count]);
        }
        finally
        {
            _rotating = false;
        }
    }

    /// <summary>Settings › Dashboard (right-click on the dashboard button).</summary>
    [RelayCommand]
    private void OpenDashboardSettings()
    {
        IsDashboard = false;
        NavigateTo(SettingsPage);
        SettingsPage.RequestScrollTo("dashboard");
    }

    /// <summary>Dashboard settings changed (rotation, clock).</summary>
    public void ApplyDashboardSettings()
    {
        RestartRotation();
        OnPropertyChanged(nameof(DashboardClockVisible));
    }

    /// <summary>UI size setting changed: the window recomputes its scale factor.</summary>
    public event Action? UiScaleChanged;

    public void NotifyUiScaleChanged() => UiScaleChanged?.Invoke();

    // ------------------------------------------------------------------------------------------- search

    [ObservableProperty]
    private string _searchText = "";

    public SearchMatcher Matcher { get; private set; } = new("");

    public bool SearchVisible => CurrentPage.SupportsSearch;

    public string SearchHint => Loc.T(CurrentPage.SearchHintKey);

    partial void OnSearchTextChanged(string value)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    /// <summary>Sets the search and opens a page (e.g. "pid:1234" on the Details page).</summary>
    public void SearchOn(PageViewModel page, string text)
    {
        NavigateTo(page);
        SearchText = text;
        _searchTimer.Stop();
        Matcher = new SearchMatcher(text);
        page.OnSearchChanged();
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    // ------------------------------------------------------------------------------------------- header

    [ObservableProperty]
    private string _cpuText = "–";

    [ObservableProperty]
    private string _memoryText = "–";

    /// <summary>Busiest disk and total network throughput (mini monitor).</summary>
    [ObservableProperty]
    private string _diskText = "–";

    [ObservableProperty]
    private string _networkText = "–";

    public bool ChartFill => Settings.ChartFill;

    public bool IsElevated => SystemInfo.IsElevated;

    public string ElevationText => Loc.T(IsElevated ? "PillAdmin" : "PillStandard");

    public bool IsPaused => Sampling.IsPaused;

    public string PauseTip => Loc.T(IsPaused ? "TipResume" : "TipPause");

    public Geometry? PauseIcon => Res.Icon(IsPaused ? "IconPlay" : "IconPause");

    [RelayCommand]
    private void TogglePause()
    {
        if (IsPaused)
        {
            if (Settings.IntervalMs <= 0)
            {
                Settings.IntervalMs = 1000;
                SaveSettings();
                SettingsPage.OnSettingsChanged();
            }

            Sampling.IntervalMs = Settings.IntervalMs;
        }
        else
        {
            // Pause for this session only; the interval in the settings stays.
            Sampling.IntervalMs = 0;
        }

        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(PauseTip));
        OnPropertyChanged(nameof(PauseIcon));
        StateChanged?.Invoke();
    }

    [RelayCommand]
    private void Refresh()
    {
        Sampling.RefreshNow();
        CurrentPage.Refresh();
    }

    [RelayCommand]
    private void RestartElevated()
    {
        if (Privileges.StartElevated(AppEnvironment.ExecutablePath, "--elevated-restart"))
        {
            Host.ExitApplication?.Invoke();
        }
    }

    /// <summary>"Run new task" (Ctrl+N, header, tray): program, document, folder or URL, optionally as administrator.</summary>
    [RelayCommand]
    private async Task RunTask()
    {
        var answer = await Host.RunTaskAsync(SystemInfo.IsElevated, Settings.RunHistory);
        if (answer is not { } run)
        {
            return;
        }

        var result = ProcessTools.Run(run.Command, run.Elevated);
        Report(result, Loc.F("ToastTaskStarted", run.Command));
        if (result.Success)
        {
            Settings.RunHistory.RemoveAll(c => string.Equals(c, run.Command, StringComparison.OrdinalIgnoreCase));
            Settings.RunHistory.Insert(0, run.Command);
            if (Settings.RunHistory.Count > 12)
            {
                Settings.RunHistory.RemoveRange(12, Settings.RunHistory.Count - 12);
            }

            SaveSettingsSoon();
            Sampling.RefreshNow();
        }
    }

    [RelayCommand]
    private void ToggleMiniMonitor() => MiniMonitorRequested?.Invoke();

    public event Action? MiniMonitorRequested;

    public void SetMiniMonitorOpen(bool open)
    {
        _miniMonitorOpen = open;
        UpdateDemand();
    }

    /// <summary>Extra data the mini monitor needs for an expanded detail (process list for the top lists).</summary>
    public void SetMiniMonitorDemand(SampleDemand demand)
    {
        if (_miniDemand != demand)
        {
            _miniDemand = demand;
            UpdateDemand();
            Sampling.RefreshNow();
        }
    }

    private readonly Dictionary<SampleDemand, int> _extraDemands = new();

    /// <summary>Data an extra window needs while it is open (process windows: the process list). Reference counted.</summary>
    public void AddDemand(SampleDemand demand)
    {
        _extraDemands[demand] = _extraDemands.GetValueOrDefault(demand) + 1;
        UpdateDemand();
        Sampling.RefreshNow();
    }

    public void RemoveDemand(SampleDemand demand)
    {
        var count = _extraDemands.GetValueOrDefault(demand) - 1;
        if (count <= 0)
        {
            _extraDemands.Remove(demand);
        }
        else
        {
            _extraDemands[demand] = count;
        }

        UpdateDemand();
    }

    /// <summary>Mini monitor settings changed (position, monitor, theme, opacity, topmost).</summary>
    public event Action? MiniMonitorSettingsChanged;

    public void NotifyMiniMonitorSettingsChanged() => MiniMonitorSettingsChanged?.Invoke();

    [RelayCommand]
    private void FocusSearch() => FocusSearchRequested?.Invoke();

    public event Action? FocusSearchRequested;

    // ------------------------------------------------------------------------------------------- toast

    [ObservableProperty]
    private string? _toast;

    [ObservableProperty]
    private bool _toastIsError;

    public bool HasToast => !string.IsNullOrEmpty(Toast);

    partial void OnToastChanged(string? value) => OnPropertyChanged(nameof(HasToast));

    public void ShowToast(string message, bool error = false)
    {
        ToastIsError = error;
        Toast = message;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(error ? 8 : 4);
        _toastTimer.Start();
    }

    [RelayCommand]
    private void CloseToast() => Toast = null;

    /// <summary>Shows the result of an action: nothing on success (optional message), the Windows error otherwise.</summary>
    public void Report(ActionResult result, string? successMessage = null)
    {
        if (result.Success)
        {
            if (successMessage is not null)
            {
                ShowToast(successMessage);
            }
        }
        else
        {
            ShowToast(Loc.F("ToastActionFailed", result.Error), error: true);
        }
    }

    // ------------------------------------------------------------------------------------------- sampling

    public void OnWindowVisibilityChanged(bool visible)
    {
        if (_windowVisible == visible)
        {
            return;
        }

        _windowVisible = visible;
        Processes.SetActive(visible);
        if (visible)
        {
            CurrentPage.Activate();
        }
        else
        {
            CurrentPage.Deactivate();
        }

        UpdateDemand();
        if (visible)
        {
            Sampling.RefreshNow();
        }
    }

    /// <summary>
    /// Visible window: totals, cores, disks and network always (so the charts have history), plus what the current page
    /// needs. Hidden window: only the totals for the tray tooltip (and the mini monitor when open).
    /// </summary>
    public void UpdateDemand()
    {
        var demand = SampleDemand.System;
        if (_windowVisible)
        {
            demand |= SampleDemand.Cores | SampleDemand.Disks | SampleDemand.Network | CurrentPage.Demand;
        }

        if (_miniMonitorOpen)
        {
            demand |= SampleDemand.Disks | SampleDemand.Network | _miniDemand;
        }

        foreach (var extra in _extraDemands.Keys)
        {
            demand |= extra;
        }

        if (!Settings.GpuMonitoring)
        {
            demand &= ~SampleDemand.Gpu;
        }

        Sampling.Demand = demand;
        Sampling.BackgroundHistory = Settings.RecordAppHistory;
    }

    private void OnSnapshot(SystemSnapshot snapshot)
    {
        Last = snapshot;
        Metrics.Add(snapshot);
        Processes.Apply(snapshot);

        CpuText = Fmt.Percent(snapshot.Cpu.TotalPercent);
        MemoryText = Fmt.Percent(snapshot.Memory.UsedPercent);
        Alerts.OnSnapshot(snapshot);
        if (snapshot.Demand.HasFlag(SampleDemand.Disks))
        {
            DiskText = Fmt.Percent(snapshot.Disks.Count == 0 ? 0 : snapshot.Disks.Max(d => d.ActivePercent));
        }

        if (snapshot.Demand.HasFlag(SampleDemand.Network))
        {
            NetworkText = Fmt.BitsPerSecond(snapshot.Networks.Sum(n => n.SendBytesPerSec + n.ReceiveBytesPerSec));
        }

        if (_windowVisible)
        {
            CurrentPage.OnSnapshot(snapshot);
        }

        ChartClock.Raise();
        StateChanged?.Invoke();
    }

    // ------------------------------------------------------------------------------------------- settings & language

    public void SaveSettings()
    {
        _saveTimer.Stop();
        SettingsStore.Save(Settings);
    }

    /// <summary>Debounced save (≈ 0.5 s): many quick changes – sliders, switches – write the file once.</summary>
    public void SaveSettingsSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SetLanguage(string code)
    {
        Settings.Language = code;
        Loc.Apply(code);
        SaveSettings();
    }

    private void OnLanguageChanged()
    {
        foreach (var group in NavGroups.Append(BottomGroup))
        {
            group.RefreshText();
        }

        foreach (var page in AllPages())
        {
            page.OnLanguageChanged();
        }

        foreach (var row in Processes.Rows)
        {
            row.RefreshTexts();
        }

        OnPropertyChanged(nameof(SearchHint));
        OnPropertyChanged(nameof(ElevationText));
        OnPropertyChanged(nameof(PauseTip));
        OnPropertyChanged(nameof(BackTip));
        OnPropertyChanged(nameof(ForwardTip));
        StateChanged?.Invoke();
    }

    /// <summary>Monitoring settings changed (interval, GPU, utility …).</summary>
    public void ApplyMonitoringSettings()
    {
        Sampling.IntervalMs = Settings.IntervalMs;
        Sampling.UseProcessorUtility = Settings.UseProcessorUtility;
        Sampling.GpuEnabled = Settings.GpuMonitoring;
        Sampling.RecordHistory = Settings.RecordAppHistory;
        Metrics.Configure(Settings.HistorySeconds, Settings.IntervalMs);
        UpdateDemand();
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(PauseTip));
        OnPropertyChanged(nameof(PauseIcon));
        OnPropertyChanged(nameof(ChartFill));
        foreach (var page in AllPages())
        {
            page.OnSettingsChanged();
        }
    }

    /// <summary>
    /// Gives unused memory back to Windows (window hidden to the tray). With "trim memory in the tray" the working set is
    /// emptied too – Task Manager then shows a few MB; pages come back from RAM/standby on demand when the window opens.
    /// </summary>
    public void ReleaseMemory()
    {
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        if (Settings.TrimMemoryInTray && !_windowVisible)
        {
            SystemTools.TrimOwnWorkingSet();
        }
    }
}
