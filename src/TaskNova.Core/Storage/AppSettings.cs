using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskNova.Core.Storage;

/// <summary>Per-user settings (JSON). Defaults are the values of a fresh installation.</summary>
public sealed class AppSettings
{
    public const int CurrentVersion = 1;

    public int SettingsVersion { get; set; } = CurrentVersion;

    /// <summary>"en" or "de"; null = detect from Windows on the first start.</summary>
    public string? Language { get; set; }

    // ---------------------------------------------------------------- Appearance

    /// <summary>system, dark, light or shadow. Dark is the default.</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>Preset id (brand, red …), "windows" (Windows accent color) or "custom".</summary>
    public string AccentId { get; set; } = "brand";

    /// <summary>Color of the "custom" accent (#RRGGBB).</summary>
    public string CustomAccent { get; set; } = "#EF476F";

    /// <summary>The 16 custom color slots of the Windows color dialog (#RRGGBB).</summary>
    public List<string> CustomColors { get; set; } = [];

    /// <summary>Soft fill under the chart lines.</summary>
    public bool ChartFill { get; set; } = true;

    /// <summary>Page and list animations.</summary>
    public bool Animations { get; set; } = true;

    public bool SidebarCollapsed { get; set; }

    /// <summary>UI size: auto (by screen), xs, s, m, l, xl.</summary>
    public string UiScale { get; set; } = "auto";

    /// <summary>gpu (default) or software rendering – software usually needs less memory, a little more CPU. Restart needed.</summary>
    public string RenderMode { get; set; } = "gpu";

    // ---------------------------------------------------------------- Window

    public double WindowWidth { get; set; } = 1180;

    public double WindowHeight { get; set; } = 760;

    /// <summary>Last position (physical pixels); null = centered.</summary>
    public int? WindowX { get; set; }

    public int? WindowY { get; set; }

    /// <summary>Starts in a normal window by default – never full screen on the first start.</summary>
    public bool WindowMaximized { get; set; }

    public bool AlwaysOnTop { get; set; }

    public bool CloseToTray { get; set; }

    public bool MinimizeToTray { get; set; }

    /// <summary>Keep the hidden window alive in the tray (opens instantly, uses more memory).</summary>
    public bool KeepWindowInTray { get; set; } = true;

    /// <summary>Page id of the last session (only used with <see cref="StartPage"/> = "last").</summary>
    public string? LastPage { get; set; }

    /// <summary>"last" (last used) or a page id: overview, processes, performance … Default: overview.</summary>
    public string StartPage { get; set; } = "overview";

    /// <summary>window, maximized, fullscreen, dashboard, tray, mini.</summary>
    public string StartMode { get; set; } = "window";

    /// <summary>0 = primary monitor, 1…n = monitor number.</summary>
    public int StartMonitor { get; set; }

    /// <summary>Open the mini monitor together with TaskNova.</summary>
    public bool StartMiniMonitor { get; set; }

    /// <summary>Give the working set back to Windows while hidden in the tray (lower number, pages come back on use).</summary>
    public bool TrimMemoryInTray { get; set; }

    // ---------------------------------------------------------------- Mini monitor

    /// <summary>tl, tc, tr, ml, mc, mr, bl, bc, br or custom (dragged). Default: bottom right.</summary>
    public string MiniPosition { get; set; } = "br";

    /// <summary>0 = primary monitor, 1…n = monitor number.</summary>
    public int MiniMonitorScreen { get; set; }

    /// <summary>default (like the app), system, dark, shadow, light.</summary>
    public string MiniTheme { get; set; } = "default";

    /// <summary>40–100 %.</summary>
    public int MiniOpacity { get; set; } = 100;

    public bool MiniTopmost { get; set; } = true;

    /// <summary>Position after dragging (physical pixels), used with MiniPosition = custom.</summary>
    public int? MiniMonitorX { get; set; }

    public int? MiniMonitorY { get; set; }

    // ---------------------------------------------------------------- Monitoring

    /// <summary>Update interval in milliseconds: 500, 1000, 2000 or 5000; 0 = paused.</summary>
    public int IntervalMs { get; set; } = 1000;

    /// <summary>Seconds of history in the live charts.</summary>
    public int HistorySeconds { get; set; } = 60;

    /// <summary>Optional: CPU as "% Processor Utility" (PDH) like the Windows Task Manager instead of processor time. Off by default.</summary>
    public bool UseProcessorUtility { get; set; }

    /// <summary>GPU engines and per-process GPU (PDH "GPU Engine").</summary>
    public bool GpuMonitoring { get; set; } = true;

    /// <summary>Own app history: CPU time and I/O per program, recorded while TaskNova runs.</summary>
    public bool RecordAppHistory { get; set; } = true;

    /// <summary>Reverse DNS for remote addresses on the network page. Off by default (sends DNS queries).</summary>
    public bool ResolveHostNames { get; set; }

    // ---------------------------------------------------------------- Processes

    public bool ConfirmEndTask { get; set; } = true;

    /// <summary>Processes page: tree (parent → child) instead of groups.</summary>
    public bool ProcessTree { get; set; }

    public string ProcessSort { get; set; } = "cpu";

    public bool ProcessSortDescending { get; set; } = true;

    public string DetailsSort { get; set; } = "name";

    public bool DetailsSortDescending { get; set; }

    public List<string> CollapsedGroups { get; set; } = [];

    /// <summary>Last commands of "Run new task" (newest first, at most 12).</summary>
    public List<string> RunHistory { get; set; } = [];

    // ---------------------------------------------------------------- Alerts (small notification window, off by default)

    public bool AlertsEnabled { get; set; }

    /// <summary>CPU above this value (%) for <see cref="AlertSeconds"/> raises an alert.</summary>
    public int AlertCpuPercent { get; set; } = 90;

    /// <summary>Memory in use above this value (%) for <see cref="AlertSeconds"/> raises an alert.</summary>
    public int AlertMemoryPercent { get; set; } = 90;

    public int AlertSeconds { get; set; } = 60;

    /// <summary>Minimum minutes between two alerts of the same kind.</summary>
    public int AlertCooldownMinutes { get; set; } = 10;

    // ---------------------------------------------------------------- Expert mode (terminal server / Citrix administration)

    /// <summary>Unlocks session administration: end all processes of a user, RDP shadowing. Off by default.</summary>
    public bool ExpertMode { get; set; }

    // ---------------------------------------------------------------- Dashboard mode

    /// <summary>Dashboard: turn through <see cref="DashboardPages"/> automatically. Off by default.</summary>
    public bool DashboardRotate { get; set; }

    public int DashboardRotateSeconds { get; set; } = 30;

    /// <summary>Page ids shown in turn by the dashboard rotation.</summary>
    public List<string> DashboardPages { get; set; } = ["overview", "performance"];

    /// <summary>Computer name and clock in the dashboard corner (monitor walls with several servers).</summary>
    public bool DashboardShowClock { get; set; } = true;

    /// <summary>Hides the mouse pointer in the dashboard after a few seconds without movement.</summary>
    public bool DashboardHideCursor { get; set; } = true;

    // ---------------------------------------------------------------- Lists

    /// <summary>Sort per list: list id → "column:asc" / "column:desc".</summary>
    public Dictionary<string, string> ListSorts { get; set; } = new();

    /// <summary>Column widths per list: "list.column" → width in px; negative = hidden (the width is kept for showing it again).</summary>
    public Dictionary<string, double> ColumnLayouts { get; set; } = new();

    /// <summary>Filter chips: processes (all, suspended, efficiency), startup (all, enabled, disabled), users (all, active, disconnected).</summary>
    public string ProcessFilter { get; set; } = "all";

    public string StartupFilter { get; set; } = "all";

    public string UsersFilter { get; set; } = "all";

    /// <summary>Users page: one row per user (sessions added up) instead of one per session.</summary>
    public bool UsersPerUser { get; set; }

    public bool FirstRunDone { get; set; }

    /// <summary>Copies every value of <paramref name="other"/> into this instance (reset keeps the shared object).</summary>
    public void CopyFrom(AppSettings other)
    {
        SettingsVersion = other.SettingsVersion;
        Language = other.Language;
        Theme = other.Theme;
        AccentId = other.AccentId;
        CustomAccent = other.CustomAccent;
        CustomColors = [.. other.CustomColors];
        ChartFill = other.ChartFill;
        Animations = other.Animations;
        SidebarCollapsed = other.SidebarCollapsed;
        UiScale = other.UiScale;
        RenderMode = other.RenderMode;
        WindowWidth = other.WindowWidth;
        WindowHeight = other.WindowHeight;
        WindowX = other.WindowX;
        WindowY = other.WindowY;
        WindowMaximized = other.WindowMaximized;
        AlwaysOnTop = other.AlwaysOnTop;
        CloseToTray = other.CloseToTray;
        MinimizeToTray = other.MinimizeToTray;
        KeepWindowInTray = other.KeepWindowInTray;
        LastPage = other.LastPage;
        StartPage = other.StartPage;
        StartMode = other.StartMode;
        StartMonitor = other.StartMonitor;
        StartMiniMonitor = other.StartMiniMonitor;
        TrimMemoryInTray = other.TrimMemoryInTray;
        MiniPosition = other.MiniPosition;
        MiniMonitorScreen = other.MiniMonitorScreen;
        MiniTheme = other.MiniTheme;
        MiniOpacity = other.MiniOpacity;
        MiniTopmost = other.MiniTopmost;
        MiniMonitorX = other.MiniMonitorX;
        MiniMonitorY = other.MiniMonitorY;
        IntervalMs = other.IntervalMs;
        HistorySeconds = other.HistorySeconds;
        UseProcessorUtility = other.UseProcessorUtility;
        GpuMonitoring = other.GpuMonitoring;
        RecordAppHistory = other.RecordAppHistory;
        ResolveHostNames = other.ResolveHostNames;
        ConfirmEndTask = other.ConfirmEndTask;
        ProcessTree = other.ProcessTree;
        ProcessSort = other.ProcessSort;
        ProcessSortDescending = other.ProcessSortDescending;
        DetailsSort = other.DetailsSort;
        DetailsSortDescending = other.DetailsSortDescending;
        CollapsedGroups = [.. other.CollapsedGroups];
        RunHistory = [.. other.RunHistory];
        DashboardPages = [.. other.DashboardPages];
        ListSorts = new Dictionary<string, string>(other.ListSorts);
        ColumnLayouts = new Dictionary<string, double>(other.ColumnLayouts);
        AlertsEnabled = other.AlertsEnabled;
        AlertCpuPercent = other.AlertCpuPercent;
        AlertMemoryPercent = other.AlertMemoryPercent;
        AlertSeconds = other.AlertSeconds;
        AlertCooldownMinutes = other.AlertCooldownMinutes;
        ExpertMode = other.ExpertMode;
        DashboardRotate = other.DashboardRotate;
        DashboardRotateSeconds = other.DashboardRotateSeconds;
        DashboardShowClock = other.DashboardShowClock;
        DashboardHideCursor = other.DashboardHideCursor;
        ProcessFilter = other.ProcessFilter;
        StartupFilter = other.StartupFilter;
        UsersFilter = other.UsersFilter;
        UsersPerUser = other.UsersPerUser;
        FirstRunDone = other.FirstRunDone;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(History.AppHistoryFile))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;

/// <summary>Loads and saves <see cref="AppSettings"/> atomically (temp file + replace).</summary>
public static class SettingsStore
{
    /// <summary>Valid values of <see cref="AppSettings.StartPage"/> ("last" = page of the previous session).</summary>
    public static IReadOnlyList<string> StartPages { get; } =
    [
        "last", "overview", "processes", "performance", "apphistory", "network", "details", "startup", "users", "services",
        "filelocks", "ports", "power", "report"
    ];

    /// <summary>Set before a reset restart: nothing is written anymore.</summary>
    public static bool Frozen { get; set; }

    public static AppSettings Load(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize(json, StorageJsonContext.Default.AppSettings);
                if (settings is not null)
                {
                    return Normalize(settings);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Broken or locked file: start with defaults, the next save repairs it.
        }

        return new AppSettings();
    }

    public static bool Save(AppSettings settings, string? path = null)
    {
        if (Frozen)
        {
            return false;
        }

        path ??= AppPaths.SettingsFile;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, StorageJsonContext.Default.AppSettings));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Clamps values a hand-edited file may contain.</summary>
    internal static AppSettings Normalize(AppSettings s)
    {
        s.Theme = s.Theme is "system" or "dark" or "light" or "shadow" ? s.Theme : "dark";
        s.IntervalMs = s.IntervalMs is 0 or 500 or 1000 or 2000 or 5000 ? s.IntervalMs : 1000;
        s.HistorySeconds = s.HistorySeconds is 60 or 120 or 300 ? s.HistorySeconds : 60;
        s.UiScale = s.UiScale is "auto" or "xs" or "s" or "m" or "l" or "xl" ? s.UiScale : "auto";
        s.RenderMode = s.RenderMode is "gpu" or "software" ? s.RenderMode : "gpu";
        s.StartMode = s.StartMode is "window" or "maximized" or "fullscreen" or "dashboard" or "tray" or "mini" ? s.StartMode : "window";
        s.StartPage = StartPages.Contains(s.StartPage) ? s.StartPage : "overview";
        s.StartMonitor = Math.Clamp(s.StartMonitor, 0, 16);
        s.MiniMonitorScreen = Math.Clamp(s.MiniMonitorScreen, 0, 16);
        s.MiniPosition = s.MiniPosition is "tl" or "tc" or "tr" or "ml" or "mc" or "mr" or "bl" or "bc" or "br" or "custom" ? s.MiniPosition : "br";
        s.MiniTheme = s.MiniTheme is "default" or "system" or "dark" or "shadow" or "light" ? s.MiniTheme : "default";
        s.MiniOpacity = Math.Clamp(s.MiniOpacity, 40, 100);
        s.WindowWidth = Math.Clamp(s.WindowWidth, 760, 8000);
        s.WindowHeight = Math.Clamp(s.WindowHeight, 500, 6000);
        s.AlertCpuPercent = Math.Clamp(s.AlertCpuPercent, 50, 100);
        s.AlertMemoryPercent = Math.Clamp(s.AlertMemoryPercent, 50, 100);
        s.AlertSeconds = Math.Clamp(s.AlertSeconds, 10, 3600);
        s.AlertCooldownMinutes = Math.Clamp(s.AlertCooldownMinutes, 1, 1440);
        s.CustomColors ??= [];
        s.CollapsedGroups ??= [];
        s.RunHistory ??= [];
        s.DashboardPages ??= [];
        s.DashboardPages = s.DashboardPages.Where(p => StartPages.Contains(p) && p != "last").Distinct().ToList();
        if (s.DashboardPages.Count == 0)
        {
            s.DashboardPages = ["overview", "performance"];
        }

        s.DashboardRotateSeconds = Math.Clamp(s.DashboardRotateSeconds, 5, 3600);
        s.ListSorts ??= new();
        s.ColumnLayouts ??= new();
        s.ProcessFilter = s.ProcessFilter is "all" or "suspended" or "efficiency" ? s.ProcessFilter : "all";
        s.StartupFilter = s.StartupFilter is "all" or "enabled" or "disabled" ? s.StartupFilter : "all";
        s.UsersFilter = s.UsersFilter is "all" or "active" or "disconnected" ? s.UsersFilter : "all";
        if (s.RunHistory.Count > 12)
        {
            s.RunHistory = s.RunHistory.Take(12).ToList();
        }
        if (!Appearance.Rgb.TryParse(s.CustomAccent, out _))
        {
            s.CustomAccent = Appearance.AccentPresets.Brand.Hex;
        }

        return s;
    }
}
