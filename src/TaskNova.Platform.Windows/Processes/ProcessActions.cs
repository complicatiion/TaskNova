using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TaskNova.Core.Models;
using TaskNova.Platform.Windows.Native;

namespace TaskNova.Platform.Windows;

/// <summary>Result of a process or service action: null error = success.</summary>
public readonly record struct ActionResult(bool Success, string? Error)
{
    public static ActionResult Ok => new(true, null);

    public static ActionResult Fail(int win32Error) => new(false, new Win32Exception(win32Error).Message);

    public static ActionResult Fail(string message) => new(false, message);
}

public enum PriorityLevel
{
    Idle,
    BelowNormal,
    Normal,
    AboveNormal,
    High,
    Realtime
}

/// <summary>End, suspend, resume, priority, affinity and shell actions for processes.</summary>
public static unsafe class ProcessActions
{
    public static ActionResult Terminate(int pid)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ActionResult.Fail("Windows only");
        }

        var handle = Kernel32.OpenProcess(Kernel32.ProcessTerminate, false, pid);
        if (handle == 0)
        {
            return ActionResult.Fail(Marshal.GetLastPInvokeError());
        }

        using var h = new SafeKernelHandle(handle);
        return Kernel32.TerminateProcess(handle, 1) ? ActionResult.Ok : ActionResult.Fail(Marshal.GetLastPInvokeError());
    }

    /// <summary>
    /// Ends the process and all its descendants (children first). A child only counts when it started after its parent –
    /// a re-used parent id never pulls in an unrelated process.
    /// </summary>
    public static ActionResult TerminateTree(ProcessKey root, IReadOnlyList<(ProcessKey Key, int ParentPid)> processes)
    {
        var children = processes.ToLookup(p => p.ParentPid);
        var order = new List<int>();

        void Collect(ProcessKey parent, int depth)
        {
            if (depth > 64)
            {
                return;
            }

            foreach (var child in children[parent.Pid])
            {
                if (child.Key.Pid != parent.Pid && child.Key.CreateTime >= parent.CreateTime && !order.Contains(child.Key.Pid))
                {
                    Collect(child.Key, depth + 1);
                    order.Add(child.Key.Pid);
                }
            }
        }

        Collect(root, 0);
        order.Add(root.Pid);

        ActionResult? rootResult = null;
        foreach (var pid in order)
        {
            var result = Terminate(pid);
            if (pid == root.Pid)
            {
                rootResult = result;
            }
        }

        return rootResult ?? ActionResult.Ok;
    }

    public static ActionResult Suspend(int pid) => SuspendResume(pid, suspend: true);

    public static ActionResult Resume(int pid) => SuspendResume(pid, suspend: false);

    private static ActionResult SuspendResume(int pid, bool suspend)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ActionResult.Fail("Windows only");
        }

        var handle = Kernel32.OpenProcess(Kernel32.ProcessSuspendResume, false, pid);
        if (handle == 0)
        {
            return ActionResult.Fail(Marshal.GetLastPInvokeError());
        }

        using var h = new SafeKernelHandle(handle);
        var status = suspend ? Ntdll.NtSuspendProcess(handle) : Ntdll.NtResumeProcess(handle);
        return status >= 0 ? ActionResult.Ok : ActionResult.Fail((int)Ntdll.RtlNtStatusToDosError(status));
    }

    public static ActionResult SetPriority(int pid, PriorityLevel level)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ActionResult.Fail("Windows only");
        }

        uint value = level switch
        {
            PriorityLevel.Idle => 0x40,
            PriorityLevel.BelowNormal => 0x4000,
            PriorityLevel.AboveNormal => 0x8000,
            PriorityLevel.High => 0x80,
            PriorityLevel.Realtime => 0x100,
            _ => 0x20
        };

        var handle = Kernel32.OpenProcess(Kernel32.ProcessSetInformation | Kernel32.ProcessQueryLimitedInformation, false, pid);
        if (handle == 0)
        {
            return ActionResult.Fail(Marshal.GetLastPInvokeError());
        }

        using var h = new SafeKernelHandle(handle);
        return Kernel32.SetPriorityClass(handle, value) ? ActionResult.Ok : ActionResult.Fail(Marshal.GetLastPInvokeError());
    }

    /// <summary>Priority class from the base priority the kernel reports (no extra handle needed).</summary>
    public static PriorityLevel FromBasePriority(int basePriority) => basePriority switch
    {
        <= 4 => PriorityLevel.Idle,
        <= 6 => PriorityLevel.BelowNormal,
        <= 8 => PriorityLevel.Normal,
        <= 10 => PriorityLevel.AboveNormal,
        <= 15 => PriorityLevel.High,
        _ => PriorityLevel.Realtime
    };

    /// <summary>Affinity mask of the process and of the system (current processor group, up to 64 processors).</summary>
    public static (ulong Process, ulong System)? GetAffinity(int pid)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var handle = Kernel32.OpenProcess(Kernel32.ProcessQueryLimitedInformation, false, pid);
        if (handle == 0)
        {
            return null;
        }

        using var h = new SafeKernelHandle(handle);
        return Kernel32.GetProcessAffinityMask(handle, out var process, out var system) ? (process, system) : null;
    }

    public static ActionResult SetAffinity(int pid, ulong mask)
    {
        if (!OperatingSystem.IsWindows() || mask == 0)
        {
            return ActionResult.Fail("invalid");
        }

        var handle = Kernel32.OpenProcess(Kernel32.ProcessSetInformation | Kernel32.ProcessQueryLimitedInformation, false, pid);
        if (handle == 0)
        {
            return ActionResult.Fail(Marshal.GetLastPInvokeError());
        }

        using var h = new SafeKernelHandle(handle);
        return Kernel32.SetProcessAffinityMask(handle, (nuint)mask) ? ActionResult.Ok : ActionResult.Fail(Marshal.GetLastPInvokeError());
    }

    /// <summary>Explorer with the file selected.</summary>
    public static void OpenFileLocation(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
        }
        catch (Exception)
        {
            // Explorer not available (Server Core).
        }
    }

    /// <summary>The Explorer "Properties" dialog of a file. Call on the UI (STA) thread.</summary>
    public static bool ShowProperties(string? path, nint owner)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return false;
        }

        fixed (char* verb = "properties")
        fixed (char* file = path)
        {
            var info = new SHELLEXECUTEINFOW
            {
                cbSize = (uint)sizeof(SHELLEXECUTEINFOW),
                fMask = Shell32.SeeMaskInvokeIdList | Shell32.SeeMaskNoUi,
                hwnd = owner,
                lpVerb = verb,
                lpFile = file,
                nShow = 5 // SW_SHOW
            };
            return Shell32.ShellExecuteEx(&info);
        }
    }
}
