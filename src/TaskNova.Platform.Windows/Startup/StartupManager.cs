using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Win32;
using TaskNova.Core.Diagnostics;
using TaskNova.Core.Shell;

namespace TaskNova.Platform.Windows;

public enum StartupLocation
{
    RunUser,
    RunMachine,
    RunMachine32,
    FolderUser,
    FolderCommon,
    Task
}

/// <summary>Program that starts with Windows or at sign-in.</summary>
public sealed record StartupEntry
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required StartupLocation Location { get; init; }

    public string Command { get; init; } = "";

    /// <summary>Resolved executable (signature, icon, "open location").</summary>
    public string? TargetPath { get; init; }

    public bool Enabled { get; init; }

    public string? Publisher { get; init; }

    public bool Signed { get; init; }

    public bool TargetMissing { get; init; }

    /// <summary>Registry value name, file path (startup folder) or full task path.</summary>
    public required string Key { get; init; }
}

/// <summary>
/// Startup entries from Run keys, startup folders and scheduled tasks (logon/boot trigger, read directly from the task
/// XML files – no PowerShell). Enable/disable works like the Windows Task Manager (StartupApproved, reversible).
/// </summary>
public static class StartupManager
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Path = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    private static readonly XNamespace TaskNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static IReadOnlyList<StartupEntry> ReadAll()
    {
        var list = new List<StartupEntry>();
        if (!OperatingSystem.IsWindows())
        {
            return list;
        }

        ReadRun(Registry.CurrentUser, RunPath, StartupLocation.RunUser, "Run", list);
        ReadRun(Registry.LocalMachine, RunPath, StartupLocation.RunMachine, "Run", list);
        ReadRun(Registry.LocalMachine, Run32Path, StartupLocation.RunMachine32, "Run32", list);
        ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupLocation.FolderUser, list);
        ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupLocation.FolderCommon, list);
        ReadTasks(list);
        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void ReadRun(RegistryKey hive, string path, StartupLocation location, string approvedName, List<StartupEntry> list)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            foreach (var name in key?.GetValueNames() ?? [])
            {
                if (string.IsNullOrEmpty(name) || key!.GetValue(name) is not string command)
                {
                    continue;
                }

                list.Add(Describe(new StartupEntry
                {
                    Id = $"{location}:{name}",
                    Name = name,
                    Location = location,
                    Command = command,
                    TargetPath = ExecutableFromCommand(command),
                    Key = name,
                    Enabled = IsApproved(hive, approvedName, name)
                }));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // No access.
        }
    }

    private static void ReadFolder(string folder, StartupLocation location, List<StartupEntry> list)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        var hive = location == StartupLocation.FolderUser ? Registry.CurrentUser : Registry.LocalMachine;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? target = file;
            var command = file;
            if (Path.GetExtension(file).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var link = LnkParser.TryRead(file);
                target = link?.Path;
                command = link is null ? file : $"\"{link.Path}\"{(link.Arguments is null ? "" : " " + link.Arguments)}";
            }

            list.Add(Describe(new StartupEntry
            {
                Id = $"{location}:{fileName}",
                Name = Path.GetFileNameWithoutExtension(file),
                Location = location,
                Command = command,
                TargetPath = target,
                Key = file,
                Enabled = IsApproved(hive, "StartupFolder", fileName)
            }));
        }
    }

    /// <summary>Non-Microsoft tasks with a logon or boot trigger, read from %SystemRoot%\System32\Tasks (needs administrator rights).</summary>
    private static void ReadTasks(List<StartupEntry> list)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks");
        if (!Directory.Exists(root))
        {
            return;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(folder))
                {
                    var name = Path.GetFileName(sub);
                    if (folder == root && (name.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) || name.Equals("TaskNova", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    pending.Push(sub);
                }

                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    var entry = ReadTask(root, file);
                    if (entry is not null)
                    {
                        list.Add(entry);
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Not elevated or folder protected.
            }
        }
    }

    private static StartupEntry? ReadTask(string root, string file)
    {
        try
        {
            var doc = XDocument.Load(file);
            var task = doc.Root;
            if (task is null)
            {
                return null;
            }

            var triggers = task.Element(TaskNs + "Triggers");
            if (triggers is null || (triggers.Element(TaskNs + "LogonTrigger") is null && triggers.Element(TaskNs + "BootTrigger") is null))
            {
                return null;
            }

            var exec = task.Element(TaskNs + "Actions")?.Element(TaskNs + "Exec");
            var command = Environment.ExpandEnvironmentVariables(exec?.Element(TaskNs + "Command")?.Value.Trim().Trim('"') ?? "");
            var arguments = exec?.Element(TaskNs + "Arguments")?.Value.Trim();
            var enabled = !string.Equals(task.Element(TaskNs + "Settings")?.Element(TaskNs + "Enabled")?.Value.Trim(), "false",
                StringComparison.OrdinalIgnoreCase);
            var path = "\\" + Path.GetRelativePath(root, file);

            return Describe(new StartupEntry
            {
                Id = "Task:" + path,
                Name = Path.GetFileName(file),
                Location = StartupLocation.Task,
                Command = string.IsNullOrEmpty(arguments) ? command : $"{command} {arguments}",
                TargetPath = command.Length > 0 ? command : null,
                Key = path,
                Enabled = enabled
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>Like the Windows Task Manager: StartupApproved value (02 = on, 03 = off). Tasks: schtasks /Enable|/Disable.</summary>
    public static bool SetEnabled(StartupEntry entry, bool enabled)
    {
        if (entry.Location == StartupLocation.Task)
        {
            return ProcessRunner.Run("schtasks.exe", ["/Change", "/TN", entry.Key, enabled ? "/Enable" : "/Disable"]).Success;
        }

        var (hive, name) = ApprovedTarget(entry);
        try
        {
            using var key = hive.CreateSubKey($@"{ApprovedPath}\{name}", writable: true);
            var data = new byte[12];
            data[0] = enabled ? (byte)0x02 : (byte)0x03;
            if (!enabled)
            {
                BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
            }

            key.SetValue(ValueName(entry), data, RegistryValueKind.Binary);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    public static void OpenLocation(StartupEntry entry)
    {
        try
        {
            if (entry.Location == StartupLocation.Task)
            {
                Process.Start(new ProcessStartInfo("taskschd.msc") { UseShellExecute = true });
                return;
            }

            var file = entry.Location is StartupLocation.FolderUser or StartupLocation.FolderCommon ? entry.Key : entry.TargetPath;
            ProcessActions.OpenFileLocation(file);
        }
        catch (Exception)
        {
            // Ignore.
        }
    }

    /// <summary>Publisher (signature, otherwise version info) and missing target.</summary>
    private static StartupEntry Describe(StartupEntry entry)
    {
        var target = entry.TargetPath;
        if (string.IsNullOrEmpty(target))
        {
            return entry;
        }

        if (!File.Exists(target))
        {
            return entry with { TargetMissing = true };
        }

        var (trusted, signer) = Authenticode.GetSignature(target);
        var publisher = signer;
        if (publisher is null)
        {
            try
            {
                publisher = FileVersionInfo.GetVersionInfo(target).CompanyName;
            }
            catch (Exception)
            {
                // No version info.
            }
        }

        return entry with { Publisher = string.IsNullOrWhiteSpace(publisher) ? null : publisher, Signed = trusted };
    }

    private static bool IsApproved(RegistryKey hive, string approvedName, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey($@"{ApprovedPath}\{approvedName}");
            return key?.GetValue(valueName) is not byte[] { Length: > 0 } data || (data[0] & 0x01) == 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static (RegistryKey Hive, string Name) ApprovedTarget(StartupEntry entry) => entry.Location switch
    {
        StartupLocation.RunUser => (Registry.CurrentUser, "Run"),
        StartupLocation.RunMachine => (Registry.LocalMachine, "Run"),
        StartupLocation.RunMachine32 => (Registry.LocalMachine, "Run32"),
        StartupLocation.FolderUser => (Registry.CurrentUser, "StartupFolder"),
        _ => (Registry.LocalMachine, "StartupFolder")
    };

    private static string ValueName(StartupEntry entry) =>
        entry.Location is StartupLocation.FolderUser or StartupLocation.FolderCommon ? Path.GetFileName(entry.Key) : entry.Key;

    /// <summary>Executable of a command line ("C:\x\a.exe" -min, C:\x\a.exe /b, rundll32 …).</summary>
    public static string? ExecutableFromCommand(string command)
    {
        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0)
        {
            return text[..(exe + 4)];
        }

        var space = text.IndexOf(' ');
        return space > 0 ? text[..space] : text;
    }
}
