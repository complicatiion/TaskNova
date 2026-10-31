using Microsoft.Win32;

namespace TaskNova.Platform.Windows;

/// <summary>
/// Classic shell integration without a signed package:
/// <list type="bullet">
/// <item>Desktop context menu entry (HKCU\Software\Classes\DesktopBackground\Shell\TaskNova) – per user, no administrator
/// rights. On Windows 11 it appears under "Show more options" (the new menu only accepts packaged, signed extensions).</item>
/// <item>Optional: replace the Windows Task Manager (Image File Execution Options "Debugger" of taskmgr.exe, like
/// Process Explorer). Needs administrator rights; Ctrl+Shift+Esc then opens TaskNova.</item>
/// </list>
/// </summary>
public static class ShellIntegration
{
    private const string MenuKey = @"Software\Classes\DesktopBackground\Shell\TaskNova";
    private const string IfeoKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe";

    public static bool IsContextMenuRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(MenuKey);
            return key is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool RegisterContextMenu(string executable, string label)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(MenuKey, writable: true);
            key.SetValue("MUIVerb", label, RegistryValueKind.String);
            key.SetValue("Icon", $"\"{executable}\",0", RegistryValueKind.String);
            key.SetValue("Position", "Bottom", RegistryValueKind.String);
            using var command = key.CreateSubKey("command", writable: true);
            command.SetValue("", $"\"{executable}\"", RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    public static bool UnregisterContextMenu()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(MenuKey, throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>Updates label and path of an existing entry (language switch, moved installation).</summary>
    public static void RefreshContextMenu(string executable, string label)
    {
        if (IsContextMenuRegistered())
        {
            RegisterContextMenu(executable, label);
        }
    }

    /// <summary>True when Ctrl+Shift+Esc / taskmgr.exe is redirected to a TaskNova executable.</summary>
    public static bool IsTaskManagerReplaced()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(IfeoKey);
            return key?.GetValue("Debugger") is string debugger && debugger.Contains("tasknova", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool ReplaceTaskManager(string executable)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(IfeoKey, writable: true);
            key.SetValue("Debugger", $"\"{executable}\"", RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>Removes the redirect – only when it points to TaskNova (another tool's redirect stays).</summary>
    public static bool RestoreTaskManager()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(IfeoKey, writable: true);
            if (key?.GetValue("Debugger") is string debugger && debugger.Contains("tasknova", StringComparison.OrdinalIgnoreCase))
            {
                key.DeleteValue("Debugger", throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// When Windows starts TaskNova as taskmgr.exe "debugger", the first argument is the path of taskmgr.exe followed by
    /// its own switches (e.g. "/4"). Those arguments are ignored.
    /// </summary>
    public static bool IsTaskManagerRedirect(IReadOnlyList<string> args) =>
        args.Count > 0 && args[0].EndsWith("taskmgr.exe", StringComparison.OrdinalIgnoreCase);
}
