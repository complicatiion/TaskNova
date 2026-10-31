using System.Diagnostics;
using System.Runtime.InteropServices;
using TaskNova.Platform.Windows.Native;

namespace TaskNova.Platform.Windows;

/// <summary>Elevation state, SeDebugPrivilege and the restart as administrator.</summary>
public static unsafe class Privileges
{
    // GetCurrentProcess() pseudo handle: no handle to close.
    private const nint CurrentProcessPseudoHandle = -1;

    public static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!Advapi32.OpenProcessToken(CurrentProcessPseudoHandle, Advapi32.TokenQuery, out var token))
        {
            return false;
        }

        using var handle = new SafeKernelHandle(token);
        uint elevated = 0;
        return Advapi32.GetTokenInformation(token, Advapi32.TokenElevation, &elevated, sizeof(uint), out _) && elevated != 0;
    }

    /// <summary>
    /// Enables SeDebugPrivilege (only possible elevated): opens paths, command lines and users of processes of other
    /// accounts and services, like the Windows Task Manager does.
    /// </summary>
    public static bool EnableDebugPrivilege()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!Advapi32.OpenProcessToken(CurrentProcessPseudoHandle, Advapi32.TokenAdjustPrivileges | Advapi32.TokenQuery, out var token))
        {
            return false;
        }

        using var handle = new SafeKernelHandle(token);
        if (!Advapi32.LookupPrivilegeValue(null, "SeDebugPrivilege", out var luid))
        {
            return false;
        }

        var privileges = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = 0x00000002 /* SE_PRIVILEGE_ENABLED */ };
        return Advapi32.AdjustTokenPrivileges(token, false, ref privileges, (uint)sizeof(TOKEN_PRIVILEGES), 0, 0)
               && Marshal.GetLastPInvokeError() == 0;
    }

    /// <summary>Starts the executable elevated (UAC prompt). False when the user declined.</summary>
    public static bool StartElevated(string executable, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static string CurrentUser()
    {
        var domain = Environment.UserDomainName;
        return string.IsNullOrEmpty(domain) ? Environment.UserName : $"{domain}\\{Environment.UserName}";
    }

    public static int CurrentSessionId() =>
        OperatingSystem.IsWindows() && Kernel32.ProcessIdToSessionId(Kernel32.GetCurrentProcessId(), out var session) ? (int)session : 0;
}
