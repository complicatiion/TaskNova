using System.ComponentModel;
using System.Runtime.InteropServices;
using TaskNova.Platform.Windows.Native;

namespace TaskNova.Platform.Windows;

// ---------------------------------------------------------------------------------------------- native

[StructLayout(LayoutKind.Sequential)]
internal struct RM_UNIQUE_PROCESS
{
    public int dwProcessId;
    public long ProcessStartTime; // FILETIME
}

/// <summary>RM_PROCESS_INFO (668 bytes, 4-byte packing).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal unsafe struct RM_PROCESS_INFO
{
    public int dwProcessId;
    public uint StartTimeLow;
    public uint StartTimeHigh;
    public fixed char strAppName[256];
    public fixed char strServiceShortName[64];
    public int ApplicationType;
    public uint AppStatus;
    public int TSSessionId;
    public int bRestartable;
}

internal static unsafe partial class Rstrtmgr
{
    public const int ErrorMoreData = 234;

    [LibraryImport("rstrtmgr.dll")]
    public static partial int RmStartSession(out uint session, int flags, char* sessionKey);

    [LibraryImport("rstrtmgr.dll")]
    public static partial int RmRegisterResources(uint session, uint fileCount, char** files, uint appCount, RM_UNIQUE_PROCESS* apps,
        uint serviceCount, char** services);

    [LibraryImport("rstrtmgr.dll")]
    public static partial int RmGetList(uint session, out uint needed, ref uint count, RM_PROCESS_INFO* apps, out uint rebootReasons);

    [LibraryImport("rstrtmgr.dll")]
    public static partial int RmEndSession(uint session);
}

internal static unsafe partial class Powrprof
{
    public const uint AccessScheme = 16;
    public const int ErrorNoMoreItems = 259;

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerEnumerate(nint rootKey, Guid* scheme, Guid* subGroup, uint accessFlags, uint index, byte* buffer, ref uint size);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerReadFriendlyName(nint rootKey, Guid* scheme, Guid* subGroup, Guid* setting, byte* buffer, ref uint size);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerGetActiveScheme(nint rootKey, out Guid* active);

    [LibraryImport("powrprof.dll")]
    public static partial uint PowerSetActiveScheme(nint rootKey, Guid* scheme);
}

internal static partial class Dbghelp
{
    // MiniDumpWithFullMemory | WithHandleData | WithUnloadedModules | WithFullMemoryInfo | WithThreadInfo | WithTokenInformation
    public const int FullDumpType = 0x2 | 0x4 | 0x20 | 0x800 | 0x1000 | 0x40000;

    [LibraryImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MiniDumpWriteDump(nint process, int processId, nint file, int dumpType, nint exception, nint userStream, nint callback);
}

internal static partial class Psapi
{
    [LibraryImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyWorkingSet(nint process);

    [LibraryImport("kernel32.dll")]
    public static partial nint LocalFree(nint memory);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();
}

// ---------------------------------------------------------------------------------------------- public API

/// <summary>A process that holds a file or folder open (Restart Manager).</summary>
public sealed record FileLocker(int Pid, string AppName, string? ServiceName, int SessionId, bool Restartable, int AppType);

/// <summary>A Windows power plan.</summary>
public sealed record PowerPlan(Guid Id, string Name, bool Active);

/// <summary>
/// Tools page backends: who locks a file (Restart Manager – the same API Explorer and installers use),
/// power plans, process dumps and trimming TaskNova's own working set.
/// </summary>
public static unsafe class SystemTools
{
    /// <summary>Processes holding the given files open. For a folder, the files directly in it (up to 512) are checked.</summary>
    public static (IReadOnlyList<FileLocker> Lockers, string? Error) FindLockers(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ([], "Windows only");
        }

        List<string> files;
        try
        {
            files = Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false }).Take(512).ToList()
                : File.Exists(path) ? [path] : [];
        }
        catch (Exception ex)
        {
            return ([], ex.Message);
        }

        if (files.Count == 0)
        {
            return ([], null);
        }

        var key = stackalloc char[33];
        var result = Rstrtmgr.RmStartSession(out var session, 0, key);
        if (result != 0)
        {
            return ([], new Win32Exception(result).Message);
        }

        var handles = new List<GCHandle>(files.Count);
        try
        {
            var nameArray = new nint[files.Count];
            for (var i = 0; i < files.Count; i++)
            {
                var pin = GCHandle.Alloc((files[i] + '\0').ToCharArray(), GCHandleType.Pinned);
                handles.Add(pin);
                nameArray[i] = pin.AddrOfPinnedObject();
            }

            fixed (nint* array = nameArray)
            {
                result = Rstrtmgr.RmRegisterResources(session, (uint)files.Count, (char**)array, 0, null, 0, null);
            }

            if (result != 0)
            {
                return ([], new Win32Exception(result).Message);
            }

            uint count = 0;
            result = Rstrtmgr.RmGetList(session, out var needed, ref count, null, out _);
            if (result == 0 && needed == 0)
            {
                return ([], null);
            }

            if (result != Rstrtmgr.ErrorMoreData && result != 0)
            {
                return ([], new Win32Exception(result).Message);
            }

            // The list can grow between the two calls – retry a few times.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                count = needed + 4;
                var buffer = new RM_PROCESS_INFO[count];
                fixed (RM_PROCESS_INFO* info = buffer)
                {
                    result = Rstrtmgr.RmGetList(session, out needed, ref count, info, out _);
                }

                if (result == Rstrtmgr.ErrorMoreData)
                {
                    continue;
                }

                if (result != 0)
                {
                    return ([], new Win32Exception(result).Message);
                }

                var lockers = new List<FileLocker>((int)count);
                for (var i = 0; i < count; i++)
                {
                    fixed (RM_PROCESS_INFO* p = &buffer[i])
                    {
                        var service = new string(p->strServiceShortName);
                        lockers.Add(new FileLocker(p->dwProcessId, new string(p->strAppName), service.Length == 0 ? null : service,
                            p->TSSessionId, p->bRestartable != 0, p->ApplicationType));
                    }
                }

                return (lockers, null);
            }

            return ([], new Win32Exception(Rstrtmgr.ErrorMoreData).Message);
        }
        finally
        {
            foreach (var pin in handles)
            {
                pin.Free();
            }

            Rstrtmgr.RmEndSession(session);
        }
    }

    /// <summary>All power plans with their localized names and the active one marked.</summary>
    public static IReadOnlyList<PowerPlan> GetPowerPlans()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var active = Guid.Empty;
        if (Powrprof.PowerGetActiveScheme(0, out var activePtr) == 0 && activePtr != null)
        {
            active = *activePtr;
            Psapi.LocalFree((nint)activePtr);
        }

        var plans = new List<PowerPlan>();
        for (uint index = 0; index < 64; index++)
        {
            var guid = Guid.Empty;
            var size = (uint)sizeof(Guid);
            var status = Powrprof.PowerEnumerate(0, null, null, Powrprof.AccessScheme, index, (byte*)&guid, ref size);
            if (status != 0)
            {
                break;
            }

            plans.Add(new PowerPlan(guid, ReadName(guid), guid == active));
        }

        return plans;
    }

    private static string ReadName(Guid scheme)
    {
        uint size = 0;
        Powrprof.PowerReadFriendlyName(0, &scheme, null, null, null, ref size);
        if (size is 0 or > 4096)
        {
            return scheme.ToString();
        }

        var buffer = stackalloc byte[(int)size];
        if (Powrprof.PowerReadFriendlyName(0, &scheme, null, null, buffer, ref size) != 0)
        {
            return scheme.ToString();
        }

        return new string((char*)buffer).Trim();
    }

    /// <summary>Activates a power plan (needs no elevation for the built-in plans).</summary>
    public static ActionResult SetActivePowerPlan(Guid id)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ActionResult.Fail("Windows only");
        }

        var status = Powrprof.PowerSetActiveScheme(0, &id);
        return status == 0 ? ActionResult.Ok : ActionResult.Fail((int)status);
    }

    /// <summary>Writes a full memory dump (.dmp) of the process – readable by WinDbg and Visual Studio.</summary>
    public static ActionResult WriteDump(int pid, string targetFile)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ActionResult.Fail("Windows only");
        }

        const uint vmRead = 0x0010;
        var handle = Kernel32.OpenProcess(Kernel32.ProcessQueryInformation | vmRead, false, pid);
        if (handle == 0)
        {
            return ActionResult.Fail(Marshal.GetLastPInvokeError());
        }

        using var process = new SafeKernelHandle(handle);
        try
        {
            bool ok;
            int error;
            using (var stream = new FileStream(targetFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                ok = Dbghelp.MiniDumpWriteDump(handle, pid, stream.SafeFileHandle.DangerousGetHandle(), Dbghelp.FullDumpType, 0, 0, 0);
                error = ok ? 0 : Marshal.GetLastPInvokeError();
            }

            if (ok)
            {
                return ActionResult.Ok;
            }

            TryDelete(targetFile);
            // MiniDumpWriteDump reports HRESULTs in the last error: keep only the Win32 code part.
            return ActionResult.Fail(error & 0xFFFF);
        }
        catch (Exception ex)
        {
            TryDelete(targetFile);
            return ActionResult.Fail(ex.Message);
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Hands TaskNova's own idle pages back to Windows (used when hidden in the tray, if enabled).</summary>
    public static void TrimOwnWorkingSet()
    {
        if (OperatingSystem.IsWindows())
        {
            Psapi.EmptyWorkingSet(Psapi.GetCurrentProcess());
        }
    }
}
