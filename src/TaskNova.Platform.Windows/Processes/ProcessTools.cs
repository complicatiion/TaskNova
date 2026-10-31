using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TaskNova.Platform.Windows.Native;

namespace TaskNova.Platform.Windows;

// ---------------------------------------------------------------------------------------------- native

[StructLayout(LayoutKind.Sequential)]
internal struct PROCESS_POWER_THROTTLING_STATE
{
    public uint Version;
    public uint ControlMask;
    public uint StateMask;
}

/// <summary>THREADENTRY32 (28 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct THREADENTRY32
{
    public uint dwSize;
    public uint cntUsage;
    public uint th32ThreadID;
    public uint th32OwnerProcessID;
    public int tpBasePri;
    public int tpDeltaPri;
    public uint dwFlags;
}

/// <summary>WAITCHAIN_NODE_INFO (280 bytes): type, status and a union of lock object / thread object.</summary>
[StructLayout(LayoutKind.Explicit, Size = 280)]
internal unsafe struct WAITCHAIN_NODE_INFO
{
    [FieldOffset(0)] public int ObjectType;
    [FieldOffset(4)] public int ObjectStatus;
    // LockObject
    [FieldOffset(8)] public fixed char ObjectName[128];
    [FieldOffset(264)] public long Timeout;
    [FieldOffset(272)] public int Alertable;
    // ThreadObject
    [FieldOffset(8)] public int ProcessId;
    [FieldOffset(12)] public int ThreadId;
    [FieldOffset(16)] public int WaitTime;
    [FieldOffset(20)] public int ContextSwitches;
}

/// <summary>MODULEINFO (24 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MODULEINFO
{
    public nint lpBaseOfDll;
    public uint SizeOfImage;
    public nint EntryPoint;
}

internal static unsafe partial class ProcessToolsNative
{
    public const int ProcessPowerThrottling = 4;
    public const uint ThrottleExecutionSpeed = 0x1;
    public const uint IdlePriorityClass = 0x40;
    public const uint NormalPriorityClass = 0x20;
    public const uint ProcessVmRead = 0x0010;
    public const uint SnapThread = 0x4;
    public const uint WctOutOfProcAll = 0x1 | 0x2 | 0x4; // COM, CS, out of process
    public const int WctMaxNodes = 16;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessInformation(nint process, int infoClass, void* info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessInformation(nint process, int infoClass, void* info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint CreateToolhelp32Snapshot(uint flags, int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Thread32First(nint snapshot, ref THREADENTRY32 entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Thread32Next(nint snapshot, ref THREADENTRY32 entry);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    public static partial nint OpenThreadWaitChainSession(uint flags, nint callback);

    [LibraryImport("advapi32.dll")]
    public static partial void CloseThreadWaitChainSession(nint session);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetThreadWaitChain(nint session, nint context, uint flags, uint threadId, ref uint nodeCount,
        WAITCHAIN_NODE_INFO* nodes, [MarshalAs(UnmanagedType.Bool)] out bool isCycle);

    [LibraryImport("kernel32.dll", EntryPoint = "K32EnumProcessModulesEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumProcessModulesEx(nint process, nint* modules, uint size, out uint needed, uint filter);

    [LibraryImport("kernel32.dll", EntryPoint = "K32GetModuleFileNameExW", SetLastError = true)]
    public static partial uint GetModuleFileNameEx(nint process, nint module, char* name, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "K32GetModuleInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetModuleInformation(nint process, nint module, out MODULEINFO info, uint size);
}

// ---------------------------------------------------------------------------------------------- public API

/// <summary>One node of a wait chain: what a thread waits for (lock, message, process …) or which thread owns it.</summary>
public sealed record WaitNode(int ObjectType, int Status, string? Name, int ProcessId, int ThreadId, int WaitMs)
{
    public bool IsThread => ObjectType == 8;

    public bool IsBlocked => Status == 3;
}

/// <summary>The wait chain of one blocked thread (first node = the thread itself).</summary>
public sealed record WaitChain(int ThreadId, IReadOnlyList<WaitNode> Nodes, bool IsDeadlock);

/// <summary>A module (DLL / EXE) loaded into a process.</summary>
public sealed record ModuleEntry(string Name, string Path, ulong BaseAddress, uint Size)
{
    public string BaseText => $"0x{BaseAddress:X}";

    public string SizeText => Core.Formatting.Fmt.Bytes(Size);
}

/// <summary>Efficiency mode, restart, wait chain analysis, loaded modules and starting new tasks.</summary>
public static unsafe class ProcessTools
{
    /// <summary>
    /// Efficiency mode like the Windows 11 Task Manager: EcoQoS (execution-speed throttling) plus idle priority.
    /// Off returns the process to system-managed throttling and normal priority.
    /// </summary>
    public static ActionResult SetEfficiencyMode(int pid, bool on)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ActionResult.Fail("Windows only");
        }

        var handle = Kernel32.OpenProcess(Kernel32.ProcessSetInformation, false, pid);
        if (handle == 0)
        {
            return ActionResult.Fail(Marshal.GetLastPInvokeError());
        }

        using var h = new SafeKernelHandle(handle);
        var state = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = 1,
            ControlMask = on ? ProcessToolsNative.ThrottleExecutionSpeed : 0,
            StateMask = on ? ProcessToolsNative.ThrottleExecutionSpeed : 0
        };
        if (!ProcessToolsNative.SetProcessInformation(handle, ProcessToolsNative.ProcessPowerThrottling, &state, (uint)sizeof(PROCESS_POWER_THROTTLING_STATE)))
        {
            return ActionResult.Fail(Marshal.GetLastPInvokeError());
        }

        return Kernel32.SetPriorityClass(handle, on ? ProcessToolsNative.IdlePriorityClass : ProcessToolsNative.NormalPriorityClass)
            ? ActionResult.Ok
            : ActionResult.Fail(Marshal.GetLastPInvokeError());
    }

    /// <summary>
    /// Whether efficiency mode is on. Windows 11 / Server 2022 report the throttling state; on older systems the idle priority
    /// class (base priority 4) is the best hint.
    /// </summary>
    public static bool IsEfficiencyMode(int pid, int basePriority)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var handle = Kernel32.OpenProcess(Kernel32.ProcessQueryLimitedInformation, false, pid);
        if (handle != 0)
        {
            using var h = new SafeKernelHandle(handle);
            var state = new PROCESS_POWER_THROTTLING_STATE { Version = 1 };
            if (ProcessToolsNative.GetProcessInformation(handle, ProcessToolsNative.ProcessPowerThrottling, &state, (uint)sizeof(PROCESS_POWER_THROTTLING_STATE)))
            {
                return (state.ControlMask & state.StateMask & ProcessToolsNative.ThrottleExecutionSpeed) != 0;
            }
        }

        return basePriority == 4;
    }

    /// <summary>Thread ids of a process (Toolhelp snapshot).</summary>
    public static IReadOnlyList<int> Threads(int pid)
    {
        var result = new List<int>();
        if (!OperatingSystem.IsWindows())
        {
            return result;
        }

        var snapshot = ProcessToolsNative.CreateToolhelp32Snapshot(ProcessToolsNative.SnapThread, 0);
        if (snapshot is 0 or -1)
        {
            return result;
        }

        using var s = new SafeKernelHandle(snapshot);
        var entry = new THREADENTRY32 { dwSize = (uint)sizeof(THREADENTRY32) };
        if (ProcessToolsNative.Thread32First(snapshot, ref entry))
        {
            do
            {
                if (entry.th32OwnerProcessID == (uint)pid)
                {
                    result.Add((int)entry.th32ThreadID);
                }

                entry.dwSize = (uint)sizeof(THREADENTRY32);
            } while (ProcessToolsNative.Thread32Next(snapshot, ref entry));
        }

        return result;
    }

    /// <summary>
    /// "Analyze wait chain" like the Windows Task Manager: for every thread of the process the chain of objects it waits on.
    /// Only chains with a blocked thread are returned – an empty list means nothing is blocking.
    /// </summary>
    public static (IReadOnlyList<WaitChain> Chains, string? Error) AnalyzeWaitChain(int pid)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ([], "Windows only");
        }

        var session = ProcessToolsNative.OpenThreadWaitChainSession(0, 0);
        if (session == 0)
        {
            return ([], new Win32Exception(Marshal.GetLastPInvokeError()).Message);
        }

        try
        {
            var chains = new List<WaitChain>();
            var nodes = stackalloc WAITCHAIN_NODE_INFO[ProcessToolsNative.WctMaxNodes];
            foreach (var thread in Threads(pid))
            {
                uint count = ProcessToolsNative.WctMaxNodes;
                if (!ProcessToolsNative.GetThreadWaitChain(session, 0, ProcessToolsNative.WctOutOfProcAll, (uint)thread, ref count, nodes, out var cycle))
                {
                    continue;
                }

                var list = new List<WaitNode>((int)count);
                for (var i = 0; i < count && i < ProcessToolsNative.WctMaxNodes; i++)
                {
                    var n = nodes + i;
                    if (n->ObjectType == 8)
                    {
                        list.Add(new WaitNode(n->ObjectType, n->ObjectStatus, null, n->ProcessId, n->ThreadId, n->WaitTime));
                    }
                    else
                    {
                        // Bounded read: a name of full length (128 chars) has no terminating zero.
                        var raw = new ReadOnlySpan<char>(n->ObjectName, 128);
                        var zero = raw.IndexOf('\0');
                        var name = (zero < 0 ? raw : raw[..zero]).ToString();
                        list.Add(new WaitNode(n->ObjectType, n->ObjectStatus, name.Length == 0 ? null : name, 0, 0, 0));
                    }
                }

                // Interesting: the thread is blocked and waits on something (more than itself), or a deadlock.
                if (cycle || (list.Count > 1 && list.Any(x => x.IsThread && x.IsBlocked)))
                {
                    chains.Add(new WaitChain(thread, list, cycle));
                }
            }

            return (chains, null);
        }
        finally
        {
            ProcessToolsNative.CloseThreadWaitChainSession(session);
        }
    }

    /// <summary>Loaded modules (32- and 64-bit) with path, base address and size.</summary>
    public static (IReadOnlyList<ModuleEntry> Modules, string? Error) Modules(int pid)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ([], "Windows only");
        }

        var handle = Kernel32.OpenProcess(Kernel32.ProcessQueryInformation | ProcessToolsNative.ProcessVmRead, false, pid);
        if (handle == 0)
        {
            return ([], new Win32Exception(Marshal.GetLastPInvokeError()).Message);
        }

        using var h = new SafeKernelHandle(handle);
        var buffer = new nint[1024];
        uint needed;
        fixed (nint* p = buffer)
        {
            if (!ProcessToolsNative.EnumProcessModulesEx(handle, p, (uint)(buffer.Length * sizeof(nint)), out needed, 3))
            {
                return ([], new Win32Exception(Marshal.GetLastPInvokeError()).Message);
            }
        }

        var count = Math.Min(buffer.Length, (int)(needed / (uint)sizeof(nint)));
        var result = new List<ModuleEntry>(count);
        var name = stackalloc char[1024];
        for (var i = 0; i < count; i++)
        {
            var length = ProcessToolsNative.GetModuleFileNameEx(handle, buffer[i], name, 1024);
            if (length == 0)
            {
                continue;
            }

            var path = new string(name, 0, (int)length);
            ProcessToolsNative.GetModuleInformation(handle, buffer[i], out var info, (uint)sizeof(MODULEINFO));
            result.Add(new ModuleEntry(Path.GetFileName(path), path, (ulong)info.lpBaseOfDll, info.SizeOfImage));
        }

        return (result, null);
    }

    /// <summary>Splits a command line into the program and its arguments (quoted or unquoted program path).</summary>
    public static (string File, string Arguments) SplitCommandLine(string commandLine)
    {
        var text = commandLine.Trim();
        if (text.Length == 0)
        {
            return ("", "");
        }

        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            return end < 0 ? (text.Trim('"'), "") : (text[1..end], text[(end + 1)..].Trim());
        }

        // Unquoted path with spaces ("C:\Program Files\App\app.exe -x", "C:\Program Files"): the longest prefix that is an
        // existing file or folder.
        for (var i = text.Length; i > 0; i = text.LastIndexOf(' ', i - 1))
        {
            var candidate = text[..i];
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return (candidate, text[i..].Trim());
            }

            if (i <= 1)
            {
                break;
            }
        }

        var space = text.IndexOf(' ');
        return space < 0 ? (text, "") : (text[..space], text[(space + 1)..].Trim());
    }

    /// <summary>
    /// "Run new task": starts a program, document, folder or URL through the shell (like Win+R). With
    /// <paramref name="elevated"/> the start asks for administrator rights (UAC) unless TaskNova already has them.
    /// </summary>
    public static ActionResult Run(string commandLine, bool elevated, string? workingDirectory = null)
    {
        var (file, arguments) = SplitCommandLine(Environment.ExpandEnvironmentVariables(commandLine));
        if (file.Length == 0)
        {
            return ActionResult.Fail("Empty command");
        }

        try
        {
            var info = new ProcessStartInfo(file, arguments) { UseShellExecute = true };
            if (elevated)
            {
                info.Verb = "runas";
            }

            if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
            {
                info.WorkingDirectory = workingDirectory;
            }

            using var process = Process.Start(info);
            return ActionResult.Ok;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // UAC prompt cancelled.
            return ActionResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return ActionResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Restarts a process: ends it and starts the same executable with the same arguments in its folder.
    /// The new process runs as the TaskNova user with TaskNova's rights.
    /// </summary>
    public static ActionResult Restart(int pid, string path, string? commandLine)
    {
        if (!File.Exists(path))
        {
            return ActionResult.Fail("File not found: " + path);
        }

        var arguments = "";
        if (!string.IsNullOrWhiteSpace(commandLine))
        {
            var (_, args) = SplitCommandLine(commandLine);
            arguments = args;
        }

        var end = ProcessActions.Terminate(pid);
        if (!end.Success)
        {
            return end;
        }

        // Give the process a moment to release files and single-instance locks.
        try
        {
            using var old = Process.GetProcessById(pid);
            old.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(path, arguments)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(path) ?? ""
            });
            return ActionResult.Ok;
        }
        catch (Exception ex)
        {
            return ActionResult.Fail(ex.Message);
        }
    }
}
