using System.Runtime.InteropServices;
using TaskNova.Core.Models;
using TaskNova.Platform.Windows.Native;

namespace TaskNova.Platform.Windows;

/// <summary>
/// User sessions through the Terminal Services API (console, RDP, Citrix ICA): user, state, client, logon and idle time;
/// disconnect, log off, send a message.
/// </summary>
public static unsafe class SessionManager
{
    public static IReadOnlyList<SessionInfo> ReadAll()
    {
        var result = new List<SessionInfo>();
        if (!OperatingSystem.IsWindows())
        {
            return result;
        }

        try
        {
            if (!Wtsapi32.WTSEnumerateSessions(0, 0, 1, out var sessions, out var count))
            {
                return result;
            }

            try
            {
                for (var i = 0; i < count; i++)
                {
                    var session = sessions[i];
                    var id = session.SessionId;
                    var user = QueryString(id, Wtsapi32.WtsUserName);

                    // Services session 0 and listeners (RDP-Tcp) have no user: not shown.
                    if (string.IsNullOrEmpty(user))
                    {
                        continue;
                    }

                    var info = QueryInfo(id);
                    result.Add(new SessionInfo
                    {
                        SessionId = (int)id,
                        WinStation = session.pWinStationName == null ? "" : new string(session.pWinStationName),
                        UserName = user,
                        Domain = QueryString(id, Wtsapi32.WtsDomainName) ?? "",
                        State = (SessionState)session.State,
                        Protocol = QueryProtocol(id),
                        ClientName = NullIfEmpty(QueryString(id, Wtsapi32.WtsClientName)),
                        ClientAddress = QueryAddress(id),
                        LogonTimeUtc = info?.Logon,
                        LastInputUtc = info?.LastInput,
                        CurrentTimeUtc = info?.Current
                    });
                }
            }
            finally
            {
                Wtsapi32.WTSFreeMemory(sessions);
            }
        }
        catch (DllNotFoundException)
        {
            // Terminal Services API missing (stripped images).
        }

        return result;
    }

    public static ActionResult Disconnect(int sessionId) =>
        Wtsapi32.WTSDisconnectSession(0, (uint)sessionId, false) ? ActionResult.Ok : ActionResult.Fail(Marshal.GetLastPInvokeError());

    public static ActionResult LogOff(int sessionId) =>
        Wtsapi32.WTSLogoffSession(0, (uint)sessionId, false) ? ActionResult.Ok : ActionResult.Fail(Marshal.GetLastPInvokeError());

    /// <summary>Message box in the session (does not wait for an answer).</summary>
    public static ActionResult SendMessage(int sessionId, string title, string message)
    {
        fixed (char* t = title)
        fixed (char* m = message)
        {
            return Wtsapi32.WTSSendMessage(0, (uint)sessionId, t, (uint)(title.Length * 2), m, (uint)(message.Length * 2), 0x40 /* MB_ICONINFORMATION */,
                0, out _, false)
                ? ActionResult.Ok
                : ActionResult.Fail(Marshal.GetLastPInvokeError());
        }
    }

    private static string? QueryString(uint session, int infoClass)
    {
        if (!Wtsapi32.WTSQuerySessionInformation(0, session, infoClass, out var buffer, out _) || buffer == 0)
        {
            return null;
        }

        try
        {
            return new string((char*)buffer);
        }
        finally
        {
            Wtsapi32.WTSFreeMemory((void*)buffer);
        }
    }

    private static SessionProtocol QueryProtocol(uint session)
    {
        if (!Wtsapi32.WTSQuerySessionInformation(0, session, Wtsapi32.WtsClientProtocolType, out var buffer, out var bytes) || buffer == 0)
        {
            return SessionProtocol.Unknown;
        }

        try
        {
            return bytes >= 2 ? (SessionProtocol)(*(ushort*)buffer) : SessionProtocol.Unknown;
        }
        finally
        {
            Wtsapi32.WTSFreeMemory((void*)buffer);
        }
    }

    private static string? QueryAddress(uint session)
    {
        if (!Wtsapi32.WTSQuerySessionInformation(0, session, Wtsapi32.WtsClientAddress, out var buffer, out var bytes) || buffer == 0)
        {
            return null;
        }

        try
        {
            if (bytes < sizeof(WTS_CLIENT_ADDRESS))
            {
                return null;
            }

            var address = (WTS_CLIENT_ADDRESS*)buffer;
            // AF_INET: the IPv4 address sits at bytes 2–5 of the address field.
            return address->AddressFamily == 2
                ? $"{address->Address[2]}.{address->Address[3]}.{address->Address[4]}.{address->Address[5]}"
                : null;
        }
        finally
        {
            Wtsapi32.WTSFreeMemory((void*)buffer);
        }
    }

    private static (DateTime? Logon, DateTime? LastInput, DateTime? Current)? QueryInfo(uint session)
    {
        if (!Wtsapi32.WTSQuerySessionInformation(0, session, Wtsapi32.WtsSessionInfo, out var buffer, out var bytes) || buffer == 0)
        {
            return null;
        }

        try
        {
            if (bytes < sizeof(WTSINFOW))
            {
                return null;
            }

            var info = (WTSINFOW*)buffer;
            return (FromFileTime(info->LogonTime), FromFileTime(info->LastInputTime), FromFileTime(info->CurrentTime));
        }
        finally
        {
            Wtsapi32.WTSFreeMemory((void*)buffer);
        }
    }

    private static DateTime? FromFileTime(long value) => value > 0 ? DateTime.FromFileTimeUtc(value) : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
