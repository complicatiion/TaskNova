using Microsoft.Win32;
using TaskNova.Core.Models;
using TaskNova.Platform.Windows.Native;

namespace TaskNova.Platform.Windows;

/// <summary>Reads the values that do not change while TaskNova runs.</summary>
public static unsafe class StaticInfoReader
{
    private const string CurrentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string CpuKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";

    public static StaticSystemInfo Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new StaticSystemInfo { ComputerName = Environment.MachineName, LogicalProcessors = Environment.ProcessorCount };
        }

        var (cores, sockets) = CountCores();
        var memory = new MEMORYSTATUSEX { dwLength = (uint)sizeof(MEMORYSTATUSEX) };
        Kernel32.GlobalMemoryStatusEx(ref memory);

        return new StaticSystemInfo
        {
            ComputerName = Environment.MachineName,
            OsName = OsName(),
            OsVersion = OsVersion(),
            CpuName = (ReadString(Registry.LocalMachine, CpuKey, "ProcessorNameString") ?? "").Trim(),
            CpuCores = cores,
            CpuSockets = sockets,
            LogicalProcessors = LogicalProcessorCount(),
            BaseMhz = ReadInt(Registry.LocalMachine, CpuKey, "~MHz"),
            VirtualMachine = IsVirtualMachine(),
            TotalMemoryBytes = memory.ullTotalPhys,
            GpuNames = GpuNames(),
            IsServer = IsServer(),
            IsElevated = Privileges.IsElevated(),
            UserName = Privileges.CurrentUser()
        };
    }

    /// <summary>All logical processors in all processor groups.</summary>
    public static int LogicalProcessorCount()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Environment.ProcessorCount;
        }

        var count = (int)Kernel32.GetActiveProcessorCount(0xFFFF); // ALL_PROCESSOR_GROUPS
        return count > 0 ? count : Environment.ProcessorCount;
    }

    /// <summary>Physical cores (RelationProcessorCore) and sockets (RelationProcessorPackage).</summary>
    private static (int Cores, int Sockets) CountCores() => (CountRelation(0), CountRelation(3));

    private static int CountRelation(int relationship)
    {
        uint length = 0;
        Kernel32.GetLogicalProcessorInformationEx(relationship, null, ref length);
        if (length == 0)
        {
            return 0;
        }

        var buffer = new byte[length];
        fixed (byte* p = buffer)
        {
            if (!Kernel32.GetLogicalProcessorInformationEx(relationship, p, ref length))
            {
                return 0;
            }

            var count = 0;
            var offset = 0u;
            while (offset + 8 <= length)
            {
                var size = *(uint*)(p + offset + 4);
                if (size == 0)
                {
                    break;
                }

                if (*(int*)(p + offset) == relationship)
                {
                    count++;
                }

                offset += size;
            }

            return count;
        }
    }

    public static string OsName()
    {
        var name = ReadString(Registry.LocalMachine, CurrentVersion, "ProductName") ?? "Windows";
        // Windows 11 still reports "Windows 10" as product name; the build number tells the truth.
        if (Build() >= 22000 && name.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            name = "Windows 11" + name["Windows 10".Length..];
        }

        return name;
    }

    public static string OsVersion()
    {
        var display = ReadString(Registry.LocalMachine, CurrentVersion, "DisplayVersion") ?? ReadString(Registry.LocalMachine, CurrentVersion, "ReleaseId");
        var ubr = ReadInt(Registry.LocalMachine, CurrentVersion, "UBR");
        var build = $"{Build()}{(ubr > 0 ? "." + ubr : "")}";
        return string.IsNullOrEmpty(display) ? build : $"{display} ({build})";
    }

    public static int Build() => int.TryParse(ReadString(Registry.LocalMachine, CurrentVersion, "CurrentBuild"), out var b) ? b : 0;

    public static bool IsServer()
    {
        var type = ReadString(Registry.LocalMachine, CurrentVersion, "InstallationType") ?? "";
        return type.StartsWith("Server", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVirtualMachine()
    {
        const string key = @"SYSTEM\CurrentControlSet\Control\SystemInformation";
        var text = $"{ReadString(Registry.LocalMachine, key, "SystemManufacturer")} {ReadString(Registry.LocalMachine, key, "SystemProductName")}";
        string[] markers = ["VMware", "Virtual Machine", "VirtualBox", "KVM", "QEMU", "Xen", "Hyper-V", "Parallels", "Nutanix"];
        return markers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Display adapters (attached to the desktop first), without duplicates.</summary>
    public static IReadOnlyList<string> GpuNames()
    {
        var names = new List<string>();
        for (uint i = 0; i < 16; i++)
        {
            var device = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
            if (!User32.EnumDisplayDevices(null, i, ref device, 0))
            {
                break;
            }

            var name = new string(device.DeviceString).TrimEnd('\0').Trim();
            if (name.Length > 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static string? ReadString(RegistryKey hive, string path, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            return key?.GetValue(name) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int ReadInt(RegistryKey hive, string path, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            return key?.GetValue(name) is int value ? value : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
