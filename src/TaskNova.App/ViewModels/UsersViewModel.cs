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
public sealed partial class SessionRow(SessionInfo info) : ObservableObject
{
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

    public bool IsOwn { get; init; }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ProtocolText));
    }
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
                var row = Items.FirstOrDefault(r => r.SessionId == session.SessionId && r.Info.UserName == session.UserName);
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

            rows.Sort((a, b) => a.SessionId.CompareTo(b.SessionId));
            ListSync.Sync(Items, rows, () => Selected, v => Selected = v);
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
        foreach (var row in Items)
        {
            var processes = bySession.GetValueOrDefault(row.SessionId) ?? [];
            row.Cpu = processes.Sum(p => p.Cpu);
            row.CpuText = Fmt.CpuPercent(row.Cpu);
            row.MemoryText = Fmt.MemoryMb(processes.Aggregate(0UL, (sum, p) => sum + p.Memory));
            row.ProcessCount = Fmt.Number(processes.Count);
        }

        Summary = Loc.F("UsersSummary", Items.Count, Items.Count(i => i.IsActive));
        _pillSessions.Set(Items.Count);
        _pillActive.Set(Items.Count(i => i.IsActive));
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
