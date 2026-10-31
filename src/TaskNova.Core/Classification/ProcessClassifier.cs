using TaskNova.Core.Models;

namespace TaskNova.Core.Classification;

/// <summary>Grouping (Apps / Background / Windows) and the security hints of a process.</summary>
public static class ProcessClassifier
{
    /// <summary>Core Windows processes – ending them crashes or logs off the system.</summary>
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "smss.exe", "csrss.exe", "wininit.exe", "services.exe", "lsass.exe", "winlogon.exe",
        "Memory Compression", "Secure System", "LsaIso.exe", "fontdrvhost.exe"
    };

    /// <summary>Names that belong to Windows; outside the Windows folder they are suspicious.</summary>
    private static readonly HashSet<string> WindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost.exe", "lsass.exe", "csrss.exe", "smss.exe", "wininit.exe", "winlogon.exe", "services.exe", "explorer.exe",
        "dwm.exe", "spoolsv.exe", "taskhostw.exe", "conhost.exe", "rundll32.exe", "dllhost.exe", "sihost.exe",
        "ctfmon.exe", "lsm.exe", "wuauclt.exe", "searchindexer.exe", "runtimebroker.exe"
    };

    /// <summary>Kernel pseudo processes without an image file.</summary>
    private static readonly HashSet<string> Pseudo = new(StringComparer.OrdinalIgnoreCase)
    {
        "System Idle Process", "System", "Registry", "Memory Compression", "Secure System", "Interrupts"
    };

    public static bool IsCritical(string name) => Critical.Contains(name);

    public static bool IsPseudo(string name) => Pseudo.Contains(name);

    /// <summary>
    /// Apps own a visible window; Windows processes are kernel pseudo processes or run from the Windows folder;
    /// everything else is a background process.
    /// </summary>
    public static ProcessGroup Classify(string name, string? path, bool hasWindow, string windowsDirectory)
    {
        if (hasWindow && !IsCritical(name))
        {
            return ProcessGroup.App;
        }

        if (IsPseudo(name) || IsCritical(name))
        {
            return ProcessGroup.Windows;
        }

        if (!string.IsNullOrEmpty(path) && IsUnder(path, windowsDirectory))
        {
            return ProcessGroup.Windows;
        }

        return ProcessGroup.Background;
    }

    public static SecurityFlags Inspect(string name, string? path, bool fileExists, string windowsDirectory, string? tempDirectory,
        string? userProfile)
    {
        if (string.IsNullOrEmpty(path))
        {
            return SecurityFlags.None;
        }

        var flags = SecurityFlags.None;
        if (!fileExists)
        {
            flags |= SecurityFlags.FileMissing;
        }

        if ((!string.IsNullOrEmpty(tempDirectory) && IsUnder(path, tempDirectory))
            || (!string.IsNullOrEmpty(userProfile) && (IsUnder(path, Path.Combine(userProfile, "Downloads"))
                                                       || IsUnder(path, Path.Combine(userProfile, "AppData", "Local", "Temp"))
                                                       || IsUnder(path, Path.Combine(userProfile, "AppData", "Roaming")))))
        {
            flags |= SecurityFlags.UserWritableLocation;
        }

        if (WindowsNames.Contains(name) && !IsUnder(path, windowsDirectory))
        {
            flags |= SecurityFlags.NameMismatch;
        }

        return flags;
    }

    public static bool IsUnder(string path, string folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return false;
        }

        var normalized = folder.Replace('/', '\\').TrimEnd('\\') + "\\";
        return path.Replace('/', '\\').StartsWith(normalized, StringComparison.OrdinalIgnoreCase);
    }
}
