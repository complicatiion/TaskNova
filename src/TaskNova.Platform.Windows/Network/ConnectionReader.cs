using System.Net;
using TaskNova.Core.Models;
using TaskNova.Platform.Windows.Native;

namespace TaskNova.Platform.Windows;

/// <summary>Open TCP connections and UDP endpoints with owning process (GetExtendedTcpTable / GetExtendedUdpTable).</summary>
public static unsafe class ConnectionReader
{
    private static readonly string[] TcpStates =
    [
        "", "CLOSED", "LISTEN", "SYN_SENT", "SYN_RCVD", "ESTABLISHED", "FIN_WAIT1", "FIN_WAIT2", "CLOSE_WAIT", "CLOSING", "LAST_ACK",
        "TIME_WAIT", "DELETE_TCB"
    ];

    public static IReadOnlyList<ConnectionInfo> ReadAll()
    {
        var result = new List<ConnectionInfo>();
        if (!OperatingSystem.IsWindows())
        {
            return result;
        }

        ReadTcp4(result);
        ReadTcp6(result);
        ReadUdp4(result);
        ReadUdp6(result);
        return result;
    }

    private static byte[]? Table(bool tcp, uint family, int tableClass)
    {
        uint size = 0;
        _ = tcp
            ? Iphlpapi.GetExtendedTcpTable(null, ref size, false, family, tableClass, 0)
            : Iphlpapi.GetExtendedUdpTable(null, ref size, false, family, tableClass, 0);
        for (var attempt = 0; attempt < 4 && size > 0; attempt++)
        {
            var buffer = new byte[size + 4096];
            size = (uint)buffer.Length;
            uint status;
            fixed (byte* p = buffer)
            {
                status = tcp
                    ? Iphlpapi.GetExtendedTcpTable(p, ref size, false, family, tableClass, 0)
                    : Iphlpapi.GetExtendedUdpTable(p, ref size, false, family, tableClass, 0);
            }

            if (status == 0)
            {
                return buffer;
            }

            if (status != Iphlpapi.ErrorInsufficientBuffer)
            {
                return null;
            }
        }

        return null;
    }

    private static int Port(uint value) => (int)(((value & 0xFF) << 8) | ((value >> 8) & 0xFF));

    private static string State(uint state) => state < TcpStates.Length ? TcpStates[state] : state.ToString();

    private static void ReadTcp4(List<ConnectionInfo> result)
    {
        var table = Table(true, Iphlpapi.AfInet, Iphlpapi.TcpTableOwnerPidAll);
        if (table is null)
        {
            return;
        }

        fixed (byte* p = table)
        {
            var count = *(uint*)p;
            var rows = (MIB_TCPROW_OWNER_PID*)(p + 4);
            for (var i = 0; i < count; i++)
            {
                var row = rows[i];
                var listen = row.dwState == 2;
                result.Add(new ConnectionInfo(ConnectionProtocol.Tcp, new IPAddress(row.dwLocalAddr).ToString(), Port(row.dwLocalPort),
                    listen ? "" : new IPAddress(row.dwRemoteAddr).ToString(), listen ? 0 : Port(row.dwRemotePort), State(row.dwState),
                    (int)row.dwOwningPid));
            }
        }
    }

    private static void ReadTcp6(List<ConnectionInfo> result)
    {
        var table = Table(true, Iphlpapi.AfInet6, Iphlpapi.TcpTableOwnerPidAll);
        if (table is null)
        {
            return;
        }

        fixed (byte* p = table)
        {
            var count = *(uint*)p;
            var rows = (MIB_TCP6ROW_OWNER_PID*)(p + 4);
            for (var i = 0; i < count; i++)
            {
                var row = rows + i;
                var listen = row->dwState == 2;
                result.Add(new ConnectionInfo(ConnectionProtocol.Tcp6, V6(row->ucLocalAddr, row->dwLocalScopeId), Port(row->dwLocalPort),
                    listen ? "" : V6(row->ucRemoteAddr, row->dwRemoteScopeId), listen ? 0 : Port(row->dwRemotePort), State(row->dwState),
                    (int)row->dwOwningPid));
            }
        }
    }

    private static void ReadUdp4(List<ConnectionInfo> result)
    {
        var table = Table(false, Iphlpapi.AfInet, Iphlpapi.UdpTableOwnerPid);
        if (table is null)
        {
            return;
        }

        fixed (byte* p = table)
        {
            var count = *(uint*)p;
            var rows = (MIB_UDPROW_OWNER_PID*)(p + 4);
            for (var i = 0; i < count; i++)
            {
                var row = rows[i];
                result.Add(new ConnectionInfo(ConnectionProtocol.Udp, new IPAddress(row.dwLocalAddr).ToString(), Port(row.dwLocalPort), "", 0, "",
                    (int)row.dwOwningPid));
            }
        }
    }

    private static void ReadUdp6(List<ConnectionInfo> result)
    {
        var table = Table(false, Iphlpapi.AfInet6, Iphlpapi.UdpTableOwnerPid);
        if (table is null)
        {
            return;
        }

        fixed (byte* p = table)
        {
            var count = *(uint*)p;
            var rows = (MIB_UDP6ROW_OWNER_PID*)(p + 4);
            for (var i = 0; i < count; i++)
            {
                var row = rows + i;
                result.Add(new ConnectionInfo(ConnectionProtocol.Udp6, V6(row->ucLocalAddr, row->dwLocalScopeId), Port(row->dwLocalPort), "", 0, "",
                    (int)row->dwOwningPid));
            }
        }
    }

    private static string V6(byte* address, uint scope) => new IPAddress(new ReadOnlySpan<byte>(address, 16), scope).ToString();
}
