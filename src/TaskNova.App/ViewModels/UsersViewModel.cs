using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNova.App.Localization;
using TaskNova.Core.Formatting;
using TaskNova.Core.Models;
using TaskNova.Platform.Windows;

namespace TaskNova.App.ViewModels;

/// <summary>A signed-in user session with its resource usage.</summary>
public sealed partial class SessionRow(SessionInfo info) : ObservableObject, Controls.ITypeAheadItem
{
    string Controls.ITypeAheadItem.TypeAheadText => Info.UserName ?? "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(ProtocolText), nameof(ClientText), nameof(LogonText), nameof(IdleText),
        nameof(IsActive), nameof(UserText))]
    private SessionInfo _info = info;

    public int SessionId => Info.SessionId;

    public string UserText => Info.FullUserName;

    public bool IsActive => Info.State == SessionState.Active;

    public string StateText => Loc.T("Session" + Info.State);

    public string ProtocolText => Info.Protocol switch
    {
        SessionProtocol.Console => Loc.T("ProtocolConsole"),
        SessionProtocol.Rdp => "RDP",
        SessionProtocol.Ica => "Citrix ICA",
        _ => "–"
    };

    public string ClientText => string.Join(" · ", new[] { Info.ClientName, Info.ClientAddress }.Where(s => !string.IsNullOrEmpty(s))) is { Length: > 0 } text
        ? text
        : "–";

    public string LogonText => Fmt.Date(Info.LogonTimeUtc);

    public string IdleText => Info is { LastInputUtc: { } input, CurrentTimeUtc: { } now } && input > DateTime.MinValue.AddYears(1)
        ? Fmt.Idle(now - input)
        : "–";

    [ObservableProperty]
    private string _cpuText = "–";

    [ObservableProperty]
    private string _memoryText = "–";

    [ObservableProperty]
    private string _processCount = "–";

    [ObservableProperty]
    private double _cpu;

    public ulong MemoryBytes { get; set; }

    public int Processes { get; set; }

    public TimeSpan Idle => Info is { LastInputUtc: { } input, CurrentTimeUtc: { } now } && input > DateTime.MinValue.AddYears(1)
        ? now - input
        : TimeSpan.Zero;

    public bool IsOwn { get; init; }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ProtocolText));
    }
}

/// <summary>One user with all of his sessions added up ("per user" view).</summary>
public sealed partial class UserTotalRow(string user) : ObservableObject
{
    public string User { get; } = user;

    [ObservableProperty]
    private int _sessions;

    [ObservableProperty]
    private string _stateText = "";

    [ObservableProperty]
    private string _cpuText = "–";

    [ObservableProperty]
    private string _memoryText = "–";

    [ObservableProperty]
    private string _processCount = "–";

    public double Cpu { get; set; }

    public ulong MemoryBytes { get; set; }

    public int Processes { get; set; }

    public bool IsActive { get; set; }
}

/// <summary>
/// Users: all sessions (console, RDP, Citrix ICA) with state, client, logon and idle time, CPU/memory/process count per
/// session; disconnect, log off, send a message, show the session's processes.
/// </summary>
public sealed partial class UsersViewModel : PageViewModel
{
    private readonly DispatcherTimer _timer;
    private readonly int _ownSession = Privileges.CurrentSessionId();
    private bool _loading;
    private bool _reloadPending;

    public UsersViewModel(MainViewModel main) : base(main)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) => _ = LoadAsync();
    }

    public override string TitleKey => "NavUsers";

    public override string IconKey => "IconAccountMultiple";

    public override string Id => "users";

    public override string SearchHintKey => "SearchUsers";

    public override SampleDemand Demand => SampleDemand.Processes;

    public ObservableCollection<SessionRow> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanLogOff), nameof(CanShadow), nameof(CanEndAll))]
    private SessionRow? _selected;

    // ------------------------------------------------------------------ expert mode (terminal server / Citrix)

    /// <summary>Session administration in Settings › Expert mode (off by default).</summary>
    public bool IsExpert => Settings.ExpertMode;

    /// <summary>RDP shadowing: another active RDP or console session (Citrix ICA sessions are shadowed with Citrix Director).</summary>
    public bool CanShadow => IsExpert && Selected is { IsOwn: false, IsActive: true } row && row.Info.Protocol != SessionProtocol.Ica;

    public bool CanEndAll => IsExpert && Selected is not null && (Main.SystemInfo.IsElevated || Selected.IsOwn);

    public override void OnSettingsChanged()
    {
        OnPropertyChanged(nameof(IsExpert));
        OnPropertyChanged(nameof(CanShadow));
        OnPropertyChanged(nameof(CanEndAll));
        OnPropertyChanged(nameof(PerUser));
        OnPropertyChanged(nameof(PerSession));
        UpdateTotals();
        OnPropertyChanged(nameof(IsSessionHost));
        RefreshLogonMode();
    }

    private static readonly HashSet<string> SessionCore = new(StringComparer.OrdinalIgnoreCase)
    {
        "csrss.exe", "winlogon.exe", "dwm.exe", "fontdrvhost.exe", "LogonUI.exe", "smss.exe", "wininit.exe", "services.exe", "lsass.exe"
    };

    /// <summary>Ends every process of the session except the Windows session processes (the user stays signed in).</summary>
    [RelayCommand]
    private async Task EndAllProcesses()
    {
        if (Selected is not { } row || !CanEndAll)
        {
            return;
        }

        var targets = Main.Processes.Rows
            .Where(r => r.SessionId == row.SessionId && r.Pid is not (0 or 4) && r.Pid != Environment.ProcessId && !r.IsCritical &&
                        !SessionCore.Contains(r.Name))
            .ToList();
        if (targets.Count == 0)
        {
            Main.ShowToast(Loc.F("ToastNoUserProcesses", row.UserText));
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.F("DlgEndAllTitle", row.UserText), Loc.F("DlgEndAllText", targets.Count, row.SessionId),
            Loc.T("ActEndAllProcesses"), danger: true, confirmIcon: "IconCloseOctagon");
        if (!answer.Confirmed)
        {
            return;
        }

        var failed = 0;
        await Task.Run(() =>
        {
            foreach (var target in targets)
            {
                if (!ProcessActions.Terminate(target.Pid).Success)
                {
                    failed++;
                }
            }
        });
        if (failed == 0)
        {
            Main.ShowToast(Loc.F("ToastEndedAll", targets.Count, row.UserText));
        }
        else
        {
            Main.ShowToast(Loc.F("ToastEndedAllPartly", targets.Count - failed, targets.Count, row.UserText), error: true);
        }

        Main.Sampling.RefreshNow();
    }

    // ------------------------------------------------------------------ expert: server-wide actions

    private bool? _isSessionHost;

    /// <summary>Logon mode controls only on an RD Session Host (Windows Server with Remote Desktop Services).</summary>
    public bool IsSessionHost => IsExpert && (_isSessionHost ??= TerminalServer.IsSessionHost());

    [ObservableProperty]
    private string _logonModeText = "";

    private void RefreshLogonMode()
    {
        if (IsSessionHost)
        {
            LogonModeText = Loc.T("LogonMode" + TerminalServer.GetLogonMode());
        }
    }

    /// <summary>"change logon": enable, drain (no new logons, reconnects allowed), drain until restart, disable.</summary>
    [RelayCommand]
    private async Task SetLogonMode(string? mode)
    {
        if (!Enum.TryParse<LogonMode>(mode, out var target) || !IsSessionHost)
        {
            return;
        }

        if (target is LogonMode.Disabled or LogonMode.Drain or LogonMode.DrainUntilRestart)
        {
            var answer = await Host.ConfirmAsync(Loc.T("DlgLogonModeTitle"), Loc.T("DlgLogonModeText" + target), Loc.T("LogonModeSet" + target),
                danger: target == LogonMode.Disabled, confirmIcon: "IconLock");
            if (!answer.Confirmed)
            {
                return;
            }
        }

        var result = await Task.Run(() => TerminalServer.SetLogonMode(target));
        Main.Report(result, Loc.F("ToastLogonMode", Loc.T("LogonMode" + target)));
        RefreshLogonMode();
    }

    /// <summary>Signs out every disconnected session (optionally only those idle for more than 2 hours).</summary>
    [RelayCommand]
    private async Task SignOutDisconnected()
    {
        if (!IsExpert)
        {
            return;
        }

        var disconnected = _sessions.Where(s => s.Info.State == SessionState.Disconnected && !s.IsOwn && !string.IsNullOrEmpty(s.Info.UserName)).ToList();
        if (disconnected.Count == 0)
        {
            Main.ShowToast(Loc.T("ToastNoDisconnected"));
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.T("DlgSignOutDisconnectedTitle"), Loc.F("DlgSignOutDisconnectedText", disconnected.Count),
            Loc.T("ActSignOutDisconnected"), danger: true, optionText: Loc.T("DlgOnlyIdle2h"), confirmIcon: "IconAccountArrowRight");
        if (!answer.Confirmed)
        {
            return;
        }

        var targets = answer.OptionChecked ? disconnected.Where(s => s.Idle >= TimeSpan.FromHours(2)).ToList() : disconnected;
        var done = 0;
        await Task.Run(() =>
        {
            foreach (var session in targets)
            {
                if (SessionManager.LogOff(session.SessionId).Success)
                {
                    done++;
                }
            }
        });
        Main.ShowToast(Loc.F("ToastSignedOutDisconnected", done, targets.Count), error: done < targets.Count);
        _ = LoadAsync();
    }

    /// <summary>Message to every signed-in session (e.g. "server restarts in 15 minutes").</summary>
    [RelayCommand]
    private async Task MessageAll()
    {
        if (!IsExpert)
        {
            return;
        }

        var targets = _sessions.Where(s => !string.IsNullOrEmpty(s.Info.UserName) && !s.IsOwn).ToList();
        if (targets.Count == 0)
        {
            Main.ShowToast(Loc.T("ToastNoOtherSessions"));
            return;
        }

        var text = await Host.PromptAsync(Loc.F("DlgMessageAllTitle", targets.Count), Loc.T("DlgMessageText"), Loc.T("ActSend"),
            Loc.T("DlgMessageWatermark"), multiline: true);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var title = Loc.F("MessageFrom", Main.SystemInfo.UserName);
        var sent = await Task.Run(() => targets.Count(s => SessionManager.SendMessage(s.SessionId, title, text).Success));
        Main.ShowToast(Loc.F("ToastMessageAllSent", sent, targets.Count), error: sent < targets.Count);
    }

    /// <summary>RDP shadowing with mstsc: "view" or "control". The user is asked for consent unless a policy says otherwise.</summary>
    [RelayCommand]
    private void Shadow(string? mode)
    {
        if (Selected is not { } row || !CanShadow)
        {
            return;
        }

        var arguments = $"/shadow:{row.SessionId}" + (mode == "control" ? " /control" : "");
        Main.Report(ProcessTools.Run("mstsc.exe " + arguments, elevated: false), Loc.F("ToastShadowStarted", row.UserText));
    }

    // ------------------------------------------------------------------ list: sessions, sorting, filter chips, per-user view

    private List<SessionRow> _sessions = [];

    public ObservableCollection<UserTotalRow> UserTotals { get; } = [];

    private ListSort? _sortState;

    private ListSort ListSortState => _sortState ??= new ListSort("users", Settings, Main.SaveSettingsSoon, "session", false,
        "cpu", "memory", "processes", "idle", "logon");

    public string SortColumn => ListSortState.Column;

    public bool SortDescending => ListSortState.Descending;

    [RelayCommand]
    private void Sort(string column)
    {
        ListSortState.Toggle(column);
        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortDescending));
        ShowSessions();
        UpdateTotals();
    }

    public bool FilterAll
    {
        get => Settings.UsersFilter == "all";
        set => SetFilter(value, "all");
    }

    public bool FilterActive
    {
        get => Settings.UsersFilter == "active";
        set => SetFilter(value, "active");
    }

    public bool FilterDisconnected
    {
        get => Settings.UsersFilter == "disconnected";
        set => SetFilter(value, "disconnected");
    }

    private void SetFilter(bool on, string filter)
    {
        if (on && Settings.UsersFilter != filter)
        {
            Settings.UsersFilter = filter;
            Main.SaveSettingsSoon();
            ShowSessions();
            UpdateTotals();
        }

        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(FilterAll));
            OnPropertyChanged(nameof(FilterActive));
            OnPropertyChanged(nameof(FilterDisconnected));
        });
    }

    /// <summary>Per user (expert mode): one row per user with all sessions added up – who uses the server most.</summary>
    public bool PerUser
    {
        get => Settings.UsersPerUser && IsExpert;
        set
        {
            if (Settings.UsersPerUser != value)
            {
                Settings.UsersPerUser = value;
                Main.SaveSettingsSoon();
                if (value)
                {
                    Selected = null;
                }

                UpdateTotals();
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(PerSession));
        }
    }

    public bool PerSession => !PerUser;

    private bool PassesFilter(SessionRow row) => Settings.UsersFilter switch
    {
        "active" => row.IsActive,
        "disconnected" => row.Info.State == SessionState.Disconnected,
        _ => true
    };

    private void ShowSessions()
    {
        var rows = _sessions.Where(PassesFilter).ToList();
        rows.Sort(ListSortState.Comparer(new Dictionary<string, Comparison<SessionRow>>
        {
            ["session"] = (a, b) => a.SessionId.CompareTo(b.SessionId),
            ["user"] = (a, b) => ListSort.Text(a.UserText, b.UserText),
            ["state"] = (a, b) => ListSort.Text(a.StateText, b.StateText),
            ["protocol"] = (a, b) => ListSort.Text(a.ProtocolText, b.ProtocolText),
            ["client"] = (a, b) => ListSort.Text(a.ClientText, b.ClientText),
            ["logon"] = (a, b) => Nullable.Compare(a.Info.LogonTimeUtc, b.Info.LogonTimeUtc),
            ["idle"] = (a, b) => a.Idle.CompareTo(b.Idle),
            ["cpu"] = (a, b) => a.Cpu.CompareTo(b.Cpu),
            ["memory"] = (a, b) => a.MemoryBytes.CompareTo(b.MemoryBytes),
            ["processes"] = (a, b) => a.Processes.CompareTo(b.Processes)
        }, (a, b) => a.SessionId.CompareTo(b.SessionId)));
        ListSync.Sync(Items, rows, () => Selected, v => Selected = v);
    }

    private void UpdateTotals()
    {
        if (!PerUser)
        {
            if (UserTotals.Count > 0)
            {
                UserTotals.Clear();
            }

            return;
        }

        var totals = _sessions.Where(PassesFilter).Where(s => !string.IsNullOrEmpty(s.Info.UserName))
            .GroupBy(s => s.UserText, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var row = UserTotals.FirstOrDefault(t => string.Equals(t.User, g.Key, StringComparison.OrdinalIgnoreCase)) ?? new UserTotalRow(g.Key);
                row.Sessions = g.Count();
                row.IsActive = g.Any(s => s.IsActive);
                row.StateText = Loc.F("UsersActiveOf", g.Count(s => s.IsActive), g.Count());
                row.Cpu = g.Sum(s => s.Cpu);
                row.CpuText = Fmt.CpuPercent(row.Cpu);
                row.MemoryBytes = g.Aggregate(0UL, (sum, s) => sum + s.MemoryBytes);
                row.MemoryText = Fmt.MemoryMb(row.MemoryBytes);
                row.Processes = g.Sum(s => s.Processes);
                row.ProcessCount = Fmt.Number(row.Processes);
                return row;
            })
            .ToList();
        totals.Sort(ListSortState.Comparer(new Dictionary<string, Comparison<UserTotalRow>>
        {
            ["user"] = (a, b) => ListSort.Text(a.User, b.User),
            ["session"] = (a, b) => a.Sessions.CompareTo(b.Sessions),
            ["state"] = (a, b) => a.IsActive.CompareTo(b.IsActive),
            ["cpu"] = (a, b) => a.Cpu.CompareTo(b.Cpu),
            ["memory"] = (a, b) => a.MemoryBytes.CompareTo(b.MemoryBytes),
            ["processes"] = (a, b) => a.Processes.CompareTo(b.Processes)
        }, (a, b) => ListSort.Text(a.User, b.User)));
        ListSync.Sync(UserTotals, totals);
    }

    public bool HasSelection => Selected is not null;

    public bool CanLogOff => Selected is not null && Main.SystemInfo.IsElevated || Selected?.IsOwn == true;

    [ObservableProperty]
    private string _summary = "";

    private readonly StatPill _pillSessions = new("PillSessions");
    private readonly StatPill _pillActive = new("PillActive");

    /// <summary>Key figures of the toolbar, each in its own pill.</summary>
    public IReadOnlyList<StatPill> Stats => [_pillSessions, _pillActive];


    protected override void OnActivated()
    {
        RefreshLogonMode();
        _ = LoadAsync();
        _timer.Start();
    }

    protected override void OnDeactivated() => _timer.Stop();

    public override void Refresh() => _ = LoadAsync();

    public override void OnSearchChanged() => _ = LoadAsync();

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var row in Items)
        {
            row.RefreshTexts();
        }
    }

    private async Task LoadAsync()
    {
        if (_loading)
        {
            _reloadPending = true;
            return;
        }

        _loading = true;
        try
        {
            var sessions = await Task.Run(SessionManager.ReadAll);
            var rows = new List<SessionRow>();
            foreach (var session in sessions.Where(s => Search.IsEmpty || Search.Matches(-1, s.SessionId, s.FullUserName, s.ClientName, s.ClientAddress)))
            {
                var row = _sessions.FirstOrDefault(r => r.SessionId == session.SessionId && r.Info.UserName == session.UserName);
                if (row is null)
                {
                    row = new SessionRow(session) { IsOwn = session.SessionId == _ownSession };
                }
                else
                {
                    row.Info = session;
                }

                rows.Add(row);
            }

            _sessions = rows;
            ShowSessions();
            // The selected session may have changed state (active ↔ disconnected) without a new selection.
            OnPropertyChanged(nameof(CanShadow));
            OnPropertyChanged(nameof(CanEndAll));
            UpdateUsage();
        }
        finally
        {
            _loading = false;
        }

        if (_reloadPending)
        {
            _reloadPending = false;
            await LoadAsync();
        }
    }

    public override void OnSnapshot(SystemSnapshot snapshot) => UpdateUsage();

    private void UpdateUsage()
    {
        var bySession = Main.Processes.Rows.GroupBy(r => r.SessionId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var row in _sessions)
        {
            var processes = bySession.GetValueOrDefault(row.SessionId) ?? [];
            row.Cpu = processes.Sum(p => p.Cpu);
            row.CpuText = Fmt.CpuPercent(row.Cpu);
            row.MemoryBytes = processes.Aggregate(0UL, (sum, p) => sum + p.Memory);
            row.MemoryText = Fmt.MemoryMb(row.MemoryBytes);
            row.Processes = processes.Count;
            row.ProcessCount = Fmt.Number(processes.Count);
        }

        // Live columns re-sort with every tick; the others only when the list changes.
        if (SortColumn is "cpu" or "memory" or "processes" or "idle")
        {
            ShowSessions();
        }

        UpdateTotals();

        Summary = Loc.F("UsersSummary", _sessions.Count, _sessions.Count(i => i.IsActive));
        _pillSessions.Set(_sessions.Count);
        _pillActive.Set(_sessions.Count(i => i.IsActive));
    }

    [RelayCommand]
    private void ShowProcesses()
    {
        if (Selected is { } row)
        {
            Main.SearchOn(Main.Details, $"session:{row.SessionId}");
        }
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.F("DlgDisconnectTitle", row.UserText), Loc.T("DlgDisconnectText"), Loc.T("ActDisconnect"),
            danger: false, confirmIcon: "IconLanDisconnect");
        if (answer.Confirmed)
        {
            Main.Report(SessionManager.Disconnect(row.SessionId), Loc.F("ToastDisconnected", row.UserText));
            _ = LoadAsync();
        }
    }

    [RelayCommand]
    private async Task LogOff()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var answer = await Host.ConfirmAsync(Loc.F("DlgLogOffTitle", row.UserText), Loc.T("DlgLogOffText"), Loc.T("ActLogOff"), danger: true,
            confirmIcon: "IconAccountArrowRight");
        if (answer.Confirmed)
        {
            Main.Report(SessionManager.LogOff(row.SessionId), Loc.F("ToastLoggedOff", row.UserText));
            _ = LoadAsync();
        }
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (Selected is not { } row)
        {
            return;
        }

        var text = await Host.PromptAsync(Loc.F("DlgMessageTitle", row.UserText), Loc.T("DlgMessageText"), Loc.T("ActSend"),
            Loc.T("DlgMessageWatermark"), multiline: true);
        if (!string.IsNullOrWhiteSpace(text))
        {
            Main.Report(SessionManager.SendMessage(row.SessionId, Loc.F("MessageFrom", Main.SystemInfo.UserName), text),
                Loc.F("ToastMessageSent", row.UserText));
        }
    }
}
