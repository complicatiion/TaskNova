namespace TaskNova.Core.Storage;

/// <summary>
/// Data locations: per user under %APPDATA%\TaskNova (settings) and %LOCALAPPDATA%\TaskNova (app history),
/// or .\data next to the executable when the file "tasknova.portable" exists (portable mode).
/// </summary>
public static class AppPaths
{
    public const string PortableMarker = "tasknova.portable";

    public static string BaseDirectory => AppContext.BaseDirectory;

    public static bool IsPortable => File.Exists(Path.Combine(BaseDirectory, PortableMarker));

    private static string PortableData => Path.Combine(BaseDirectory, "data");

    public static string SettingsDirectory => IsPortable
        ? PortableData
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskNova");

    public static string LocalDataDirectory => IsPortable
        ? PortableData
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskNova");

    public static string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");

    public static string AppHistoryFile => Path.Combine(LocalDataDirectory, "app-history.json");
}
