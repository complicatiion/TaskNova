using System.Diagnostics;
using Microsoft.Win32;

namespace TaskNova.Platform.Windows;

/// <summary>Logon mode of a terminal server (Remote Desktop Session Host), like "change logon".</summary>
public enum LogonMode
{
    Unknown,
    Enabled,
    Disabled,
    Drain,
    DrainUntilRestart
}

/// <summary>
/// Terminal server administration for expert mode: read and set the logon mode (drain mode before maintenance).
/// The mode is read from the registry and set with change.exe (part of every RD Session Host).
/// </summary>
public static class TerminalServer
{
    private const string Key = @"SYSTEM\CurrentControlSet\Control\Terminal Server";

    /// <summary>True on Windows Server with the Remote Desktop Session Host role (change.exe and the drain setting exist).</summary>
    public static bool IsSessionHost()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(Key);
            // TSAppCompat = 1 on RD Session Hosts ("application server mode").
            return key?.GetValue("TSAppCompat") is int compat && compat != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static LogonMode GetLogonMode()
    {
        if (!OperatingSystem.IsWindows())
        {
            return LogonMode.Unknown;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(Key);
            if (key is null)
            {
                return LogonMode.Unknown;
            }

            if (key.GetValue("WinStationsDisabled") is string disabled && disabled.Trim() == "1")
            {
                return LogonMode.Disabled;
            }

            return key.GetValue("TSServerDrainMode") switch
            {
                1 => LogonMode.DrainUntilRestart,
                2 => LogonMode.Drain,
                _ => LogonMode.Enabled
            };
        }
        catch (Exception)
        {
            return LogonMode.Unknown;
        }
    }

    /// <summary>Sets the logon mode with "change logon /enable | /disable | /drain | /drainuntilrestart" (administrator).</summary>
    public static ActionResult SetLogonMode(LogonMode mode)
    {
        var argument = mode switch
        {
            LogonMode.Enabled => "/enable",
            LogonMode.Disabled => "/disable",
            LogonMode.Drain => "/drain",
            LogonMode.DrainUntilRestart => "/drainuntilrestart",
            _ => null
        };
        if (argument is null || !OperatingSystem.IsWindows())
        {
            return ActionResult.Fail("Not supported");
        }

        try
        {
            var change = Path.Combine(Environment.SystemDirectory, "change.exe");
            using var process = Process.Start(new ProcessStartInfo(change, "logon " + argument)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null)
            {
                return ActionResult.Fail("change.exe");
            }

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(10000);
            return process.ExitCode == 0 ? ActionResult.Ok : ActionResult.Fail(output.Trim().Length > 0 ? output.Trim() : $"Exit code {process.ExitCode}");
        }
        catch (Exception ex)
        {
            return ActionResult.Fail(ex.Message);
        }
    }
}
