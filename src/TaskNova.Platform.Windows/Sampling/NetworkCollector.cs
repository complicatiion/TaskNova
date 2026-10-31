using System.Net.NetworkInformation;
using System.Net.Sockets;
using TaskNova.Core.Metrics;
using TaskNova.Core.Models;

namespace TaskNova.Platform.Windows.Sampling;

/// <summary>
/// Throughput of the network adapters that are up. The adapter list is refreshed every 30 s; each tick only reads the
/// interface counters (GetIfEntry2 inside System.Net), which is cheap.
/// </summary>
internal sealed class NetworkCollector
{
    private sealed class Adapter
    {
        public required NetworkInterface Nic { get; init; }

        public required string Kind { get; init; }

        public string? IPv4 { get; init; }

        public long Sent { get; set; } = -1;

        public long Received { get; set; } = -1;
    }

    private readonly Dictionary<string, Adapter> _adapters = new(StringComparer.Ordinal);
    private long _refreshedTicks;

    public IReadOnlyList<NetworkSample> Sample(double elapsedSeconds)
    {
        if (Environment.TickCount64 - _refreshedTicks > 30_000 || _refreshedTicks == 0)
        {
            Refresh();
        }

        var result = new List<NetworkSample>(_adapters.Count);
        foreach (var adapter in _adapters.Values)
        {
            long sent;
            long received;
            long speed;
            try
            {
                var stats = adapter.Nic.GetIPStatistics();
                sent = stats.BytesSent;
                received = stats.BytesReceived;
                speed = adapter.Nic.Speed;
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            var sendRate = adapter.Sent < 0 ? 0 : CpuMath.Rate((ulong)adapter.Sent, (ulong)Math.Max(0, sent), elapsedSeconds);
            var receiveRate = adapter.Received < 0 ? 0 : CpuMath.Rate((ulong)adapter.Received, (ulong)Math.Max(0, received), elapsedSeconds);
            adapter.Sent = sent;
            adapter.Received = received;
            result.Add(new NetworkSample(adapter.Nic.Id, adapter.Nic.Name, adapter.Nic.Description, adapter.Kind, sendRate, receiveRate,
                speed, adapter.IPv4));
        }

        return result;
    }

    private void Refresh()
    {
        _refreshedTicks = Environment.TickCount64;
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Unknown)
            {
                continue;
            }

            seen.Add(nic.Id);
            if (_adapters.ContainsKey(nic.Id))
            {
                continue;
            }

            string? ipv4 = null;
            try
            {
                ipv4 = nic.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            }
            catch (NetworkInformationException)
            {
            }

            var kind = nic.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Wireless80211 => "wifi",
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
                    or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => "ethernet",
                NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => "mobile",
                _ => "other"
            };
            _adapters[nic.Id] = new Adapter { Nic = nic, Kind = kind, IPv4 = ipv4 };
        }

        foreach (var id in _adapters.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _adapters.Remove(id);
        }
    }
}

/// <summary>Process ids that own a visible, unowned, non-tool top-level window with a title ("Apps" group).</summary>
internal static unsafe class WindowEnumerator
{
    [ThreadStatic]
    private static HashSet<int>? _current;

    public static HashSet<int> Collect()
    {
        var set = new HashSet<int>();
        if (!OperatingSystem.IsWindows())
        {
            return set;
        }

        _current = set;
        try
        {
            Native.User32.EnumWindows(&Callback, 0);
        }
        finally
        {
            _current = null;
        }

        return set;
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static int Callback(nint hwnd, nint lParam)
    {
        if (Native.User32.IsWindowVisible(hwnd)
            && Native.User32.GetWindow(hwnd, Native.User32.GwOwner) == 0
            && (Native.User32.GetWindowLongPtr(hwnd, Native.User32.GwlExStyle) & (nint)Native.User32.WsExToolWindow) == 0
            && Native.User32.GetWindowTextLength(hwnd) > 0)
        {
            Native.User32.GetWindowThreadProcessId(hwnd, out var pid);
            _current?.Add((int)pid);
        }

        return 1;
    }
}
