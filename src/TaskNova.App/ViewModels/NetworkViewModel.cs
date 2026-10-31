using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Net;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNova.App.Localization;
using TaskNova.Core.Formatting;
using TaskNova.Core.Models;
using TaskNova.Platform.Windows;

namespace TaskNova.App.ViewModels;

/// <summary>Adapter card at the top of the network page.</summary>
public sealed record AdapterCard(string Name, string Description, string Throughput, Avalonia.Media.Geometry? Icon);

/// <summary>One TCP connection or UDP endpoint.</summary>
public sealed partial class ConnectionRow(ConnectionInfo info) : ObservableObject
{
    public ConnectionInfo Info { get; } = info;

    public string Protocol => Info.Protocol switch
    {
        ConnectionProtocol.Tcp => "TCP",
        ConnectionProtocol.Tcp6 => "TCPv6",
        ConnectionProtocol.Udp => "UDP",
        _ => "UDPv6"
    };

    public string Local => $"{Info.LocalAddress}:{Info.LocalPort}";

    public string Remote => string.IsNullOrEmpty(Info.RemoteAddress) ? "–" : $"{Info.RemoteAddress}:{Info.RemotePort}";

    public string State => string.IsNullOrEmpty(Info.State) ? "–" : Info.State;

    public int Pid => Info.ProcessId;

    [ObservableProperty]
    private string _processName = "";

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _icon;

    [ObservableProperty]
    private string _remoteHost = "";
}

/// <summary>Network page: adapters with live throughput and all connections with their process.</summary>
public sealed partial class NetworkViewModel : PageViewModel
{
    private static readonly ConcurrentDictionary<string, string> DnsCache = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, ConnectionRow> _rows = new(StringComparer.Ordinal);
    private IReadOnlyList<ConnectionInfo> _latest = [];
    private bool _loading;
    private int _dnsInFlight;

    public NetworkViewModel(MainViewModel main) : base(main)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => _ = LoadAsync();
    }

    public override string TitleKey => "NavNetwork";

    public override string IconKey => "IconLan";

    public override string Id => "network";

    public override string SearchHintKey => "SearchConnections";

    public override SampleDemand Demand => SampleDemand.Processes | SampleDemand.Network;

    public ObservableCollection<ConnectionRow> Items { get; } = [];

    public ObservableCollection<AdapterCard> Adapters { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ConnectionRow? _selected;

    public bool HasSelection => Selected is not null;

    [ObservableProperty]
    private string _summary = "";

    private readonly StatPill _pillEntries = new("PillEntries");
    private readonly StatPill _pillEstablished = new("PillEstablished");
    private readonly StatPill _pillListening = new("PillListening");

    /// <summary>Key figures of the toolbar, each in its own pill.</summary>
    public IReadOnlyList<StatPill> Stats => [_pillEntries, _pillEstablished, _pillListening];


    [ObservableProperty]
    private bool _showListening = true;

    [ObservableProperty]
    private bool _showUdp = true;

    public bool ResolveHosts => Settings.ResolveHostNames;

    partial void OnShowListeningChanged(bool value) => Apply();

    partial void OnShowUdpChanged(bool value) => Apply();

    protected override void OnActivated()
    {
        _ = LoadAsync();
        _timer.Start();
    }

    protected override void OnDeactivated() => _timer.Stop();

    public override void Refresh() => _ = LoadAsync();

    public override void OnSearchChanged() => Apply();

    public override void OnSettingsChanged() => OnPropertyChanged(nameof(ResolveHosts));

    public override void OnSnapshot(SystemSnapshot snapshot)
    {
        if (!snapshot.Demand.HasFlag(SampleDemand.Network))
        {
            return;
        }

        // Adapter cards: replace in place.
        var list = snapshot.Networks.Select(n => new AdapterCard(n.Name, n.Description,
            Loc.F("SendReceiveShort", Fmt.BitsPerSecond(n.SendBytesPerSec), Fmt.BitsPerSecond(n.ReceiveBytesPerSec)),
            Res.Icon(n.Kind == "wifi" ? "IconWifi" : "IconEthernet"))).ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (i < Adapters.Count)
            {
                if (Adapters[i] != list[i])
                {
                    Adapters[i] = list[i];
                }
            }
            else
            {
                Adapters.Add(list[i]);
            }
        }

        while (Adapters.Count > list.Count)
        {
            Adapters.RemoveAt(Adapters.Count - 1);
        }
    }

    private async Task LoadAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        try
        {
            _latest = await Task.Run(ConnectionReader.ReadAll);
            Apply();
        }
        finally
        {
            _loading = false;
        }
    }

    private void Apply()
    {
        var processes = Main.Processes.Rows.GroupBy(r => r.Pid).ToDictionary(g => g.Key, g => g.First());
        var desired = new List<ConnectionRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var info in _latest)
        {
            var udp = info.Protocol is ConnectionProtocol.Udp or ConnectionProtocol.Udp6;
            if ((udp && !ShowUdp) || (info.State == "LISTEN" && !ShowListening))
            {
                continue;
            }

            processes.TryGetValue(info.ProcessId, out var process);
            var name = info.ProcessId == 0 ? "System Idle Process" : process?.DisplayName ?? $"PID {info.ProcessId}";
            if (!Search.IsEmpty && !Search.Matches(info.ProcessId, process?.SessionId ?? -1, name, process?.Name, info.LocalAddress,
                    info.RemoteAddress, info.LocalPort.ToString(), info.RemotePort.ToString(), info.State))
            {
                continue;
            }

            if (!seen.Add(info.Key))
            {
                continue;
            }

            if (!_rows.TryGetValue(info.Key, out var row))
            {
                row = new ConnectionRow(info);
                _rows[info.Key] = row;
            }

            row.ProcessName = name;
            row.Icon = process?.Icon;
            if (ResolveHosts && !string.IsNullOrEmpty(info.RemoteAddress) && info.RemoteAddress is not ("0.0.0.0" or "::"))
            {
                row.RemoteHost = Resolve(info.RemoteAddress);
            }

            desired.Add(row);
        }

        foreach (var key in _rows.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _rows.Remove(key);
        }

        desired.Sort((a, b) =>
        {
            var byName = string.Compare(a.ProcessName, b.ProcessName, StringComparison.CurrentCultureIgnoreCase);
            return byName != 0 ? byName : string.CompareOrdinal(a.Info.Key, b.Info.Key);
        });
        ListSync.Sync(Items, desired, () => Selected, v => Selected = v);

        var tcp = _latest.Count(c => c.Protocol is ConnectionProtocol.Tcp or ConnectionProtocol.Tcp6 && c.State == "ESTABLISHED");
        var listen = _latest.Count(c => c.State == "LISTEN");
        Summary = Loc.F("NetworkSummary", desired.Count, tcp, listen);
        _pillEntries.Set(desired.Count);
        _pillEstablished.Set(tcp);
        _pillListening.Set(listen);
    }

    /// <summary>Cached reverse lookup (at most 8 at a time); the row updates when the answer arrives.</summary>
    private string Resolve(string address)
    {
        if (DnsCache.TryGetValue(address, out var host))
        {
            return host;
        }

        if (_dnsInFlight >= 8 || !IPAddress.TryParse(address, out var ip))
        {
            return "";
        }

        DnsCache[address] = "";
        Interlocked.Increment(ref _dnsInFlight);
        _ = Task.Run(async () =>
        {
            try
            {
                var entry = await Dns.GetHostEntryAsync(ip);
                DnsCache[address] = entry.HostName;
            }
            catch (Exception)
            {
                DnsCache[address] = "–";
            }
            finally
            {
                Interlocked.Decrement(ref _dnsInFlight);
            }
        });
        return "";
    }

    /// <summary>The process of the connection on the Processes page (double-click); Details when it is gone from the list.</summary>
    [RelayCommand]
    private void GoToProcesses()
    {
        if (Selected is not { } row)
        {
            return;
        }

        if (Main.Processes.FindByPid(row.Pid) is { } process)
        {
            Main.SearchOn(Main.ProcessesPage, "");
            Main.ProcessesPage.Reveal(process);
        }
        else
        {
            Main.SearchOn(Main.Details, $"pid:{row.Pid}");
        }
    }

    [RelayCommand]
    private void GoToProcess()
    {
        if (Selected is { } row)
        {
            Main.SearchOn(Main.Details, $"pid:{row.Pid}");
        }
    }

    [RelayCommand]
    private async Task CopyRow()
    {
        if (Selected is { } row)
        {
            await Host.CopyAsync($"{row.ProcessName}\t{row.Pid}\t{row.Protocol}\t{row.Local}\t{row.Remote}\t{row.State}\t{row.RemoteHost}");
            Main.ShowToast(Loc.T("ToastCopied"));
        }
    }
}
