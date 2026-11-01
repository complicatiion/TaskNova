using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNova.App.Localization;
using TaskNova.App.Services;
using TaskNova.Core.Appearance;
using TaskNova.Core.Storage;
using TaskNova.Platform.Windows;

namespace TaskNova.App.ViewModels;

/// <summary>Accent swatch in the settings (presets, Windows accent, custom).</summary>
public sealed partial class AccentOption : ObservableObject
{
    private readonly SettingsViewModel _owner;

    public AccentOption(SettingsViewModel owner, string id, string nameKey, Rgb color)
    {
        _owner = owner;
        Id = id;
        NameKey = nameKey;
        SetColor(color);
    }

    public string Id { get; }

    public string NameKey { get; }

    public string Name => Loc.T(NameKey);

    public string Tip => $"{Name} · {Hex}";

    [ObservableProperty]
    private IBrush _brush = Brushes.Transparent;

    [ObservableProperty]
    private IBrush _checkBrush = Brushes.White;

    [ObservableProperty]
    private string _hex = "";

    public bool IsSelected
    {
        get => _owner.AccentId == Id;
        set
        {
            if (value)
            {
                _owner.AccentId = Id;
            }
        }
    }

    public void SetColor(Rgb color)
    {
        Brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        var check = ColorMath.ReadableOn(color, AccentPalette.Ink, AccentPalette.Paper);
        CheckBrush = new SolidColorBrush(Color.FromRgb(check.R, check.G, check.B));
        Hex = color.ToHex();
        OnPropertyChanged(nameof(Tip));
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Tip));
    }
}

public sealed record Choice(string Value, string Title);

/// <summary>A page that the dashboard rotation may show (check box).</summary>
public sealed class DashboardPageOption(SettingsViewModel owner, string id, string title) : ObservableObject
{
    public string Id { get; } = id;

    public string Title { get; } = title;

    public bool IsChecked
    {
        get => owner.Settings.DashboardPages.Contains(Id);
        set => owner.SetDashboardPage(Id, value);
    }

    public void Refresh() => OnPropertyChanged(nameof(IsChecked));
}

/// <summary>One of the 3 × 3 anchor points of the mini monitor position picker.</summary>
public sealed class AnchorOption(SettingsViewModel owner, string id, string nameKey) : ObservableObject
{
    public string Id { get; } = id;

    public string Tip => Loc.T(nameKey);

    public bool IsSelected
    {
        get => owner.MiniPosition == Id;
        set
        {
            if (value)
            {
                owner.MiniPosition = Id;
            }
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(Tip));
    }
}

/// <summary>Settings: appearance (theme, accent), language, monitoring, window/tray, integration, administrator, reset.</summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private bool _loading;

    public SettingsViewModel(MainViewModel main) : base(main)
    {
        Accents = new ObservableCollection<AccentOption>(AccentPresets.All.Select(p => new AccentOption(this, p.Id, p.NameKey, p.Color)));
        WindowsAccent = new AccentOption(this, AccentPresets.WindowsId, "AccentWindows", AppearanceService.WindowsAccent() ?? AccentPresets.Brand.Color);
        CustomAccent = new AccentOption(this, AccentPresets.CustomId, "AccentCustom",
            Rgb.TryParse(main.Settings.CustomAccent, out var custom) ? custom : AccentPresets.Brand.Color);
        Anchors = new ObservableCollection<AnchorOption>(Core.Layout.Placement.Anchors.Select(a => new AnchorOption(this, a, "Anchor_" + a)));
        BuildChoices();
    }

    public override string TitleKey => "NavSettings";

    public override string IconKey => "IconCog";

    public override string Id => "settings";

    public override bool SupportsSearch => false;

    // ------------------------------------------------------------------------------------------- appearance

    public string Theme
    {
        get => Settings.Theme;
        set
        {
            if (Settings.Theme == value)
            {
                return;
            }

            Settings.Theme = value;
            OnPropertyChanged(nameof(ThemeSystem));
            OnPropertyChanged(nameof(ThemeDark));
            OnPropertyChanged(nameof(ThemeShadow));
            OnPropertyChanged(nameof(ThemeLight));
            AppearanceService.Instance.Apply();
            Main.SaveSettings();
        }
    }

    public bool ThemeSystem
    {
        get => Theme == "system";
        set
        {
            if (value)
            {
                Theme = "system";
            }
        }
    }

    public bool ThemeDark
    {
        get => Theme == "dark";
        set
        {
            if (value)
            {
                Theme = "dark";
            }
        }
    }

    public bool ThemeShadow
    {
        get => Theme == "shadow";
        set
        {
            if (value)
            {
                Theme = "shadow";
            }
        }
    }

    public bool ThemeLight
    {
        get => Theme == "light";
        set
        {
            if (value)
            {
                Theme = "light";
            }
        }
    }

    public ObservableCollection<AccentOption> Accents { get; }

    public AccentOption WindowsAccent { get; }

    public AccentOption CustomAccent { get; }

    public string AccentId
    {
        get => Settings.AccentId;
        set
        {
            if (Settings.AccentId == value)
            {
                return;
            }

            Settings.AccentId = value;
            if (value == AccentPresets.WindowsId && AppearanceService.WindowsAccent() is { } windows)
            {
                WindowsAccent.SetColor(windows);
            }

            RefreshAccentSelection();
            AppearanceService.Instance.Apply();
            Main.SaveSettings();
        }
    }

    private void RefreshAccentSelection()
    {
        foreach (var option in Accents.Append(WindowsAccent).Append(CustomAccent))
        {
            option.Refresh();
        }

        OnPropertyChanged(nameof(AccentId));
    }

    /// <summary>The classic Windows color dialog with the 16 custom color slots.</summary>
    [RelayCommand]
    private void PickCustomColor()
    {
        var slots = new Rgb[16];
        for (var i = 0; i < 16; i++)
        {
            slots[i] = i < Settings.CustomColors.Count && Rgb.TryParse(Settings.CustomColors[i], out var c) ? c : new Rgb(255, 255, 255);
        }

        var initial = Rgb.TryParse(Settings.CustomAccent, out var current) ? current : AccentPresets.Brand.Color;
        var picked = ColorDialog.Pick(Host.MainHandle, initial, slots);
        Settings.CustomColors = slots.Select(s => s.ToHex()).ToList();
        if (picked is not { } color)
        {
            Main.SaveSettings();
            return;
        }

        Settings.CustomAccent = color.ToHex();
        CustomAccent.SetColor(color);
        if (Settings.AccentId == AccentPresets.CustomId)
        {
            RefreshAccentSelection();
            AppearanceService.Instance.Apply();
            Main.SaveSettings();
        }
        else
        {
            AccentId = AccentPresets.CustomId;
        }
    }

    public bool ChartFill
    {
        get => Settings.ChartFill;
        set => Update(() => Settings.ChartFill = value, monitoring: true);
    }

    public bool Animations
    {
        get => Settings.Animations;
        set => Update(() =>
        {
            Settings.Animations = value;
            Controls.Motion.Enabled = value;
        });
    }

    // UI size: Auto (by screen size) or a fixed preset; applies immediately.
    public ObservableCollection<Choice> UiScales { get; } = [];

    public Choice? SelectedUiScale
    {
        get => UiScales.FirstOrDefault(c => c.Value == Settings.UiScale);
        set
        {
            if (!_loading && value is not null && value.Value != Settings.UiScale)
            {
                Update(() => Settings.UiScale = value.Value);
                Main.NotifyUiScaleChanged();
            }
        }
    }

    // Rendering: GPU (default) or software; applies after a restart.
    public ObservableCollection<Choice> RenderModes { get; } = [];

    public Choice? SelectedRenderMode
    {
        get => RenderModes.FirstOrDefault(c => c.Value == Settings.RenderMode);
        set
        {
            if (!_loading && value is not null && value.Value != Settings.RenderMode)
            {
                Update(() => Settings.RenderMode = value.Value);
                OnPropertyChanged(nameof(RenderRestartNeeded));
                Main.ShowToast(Loc.T("ToastRestartToApply"));
            }
        }
    }

    public bool RenderRestartNeeded => Settings.RenderMode != (Program.InitialSettings?.RenderMode ?? "gpu");

    [RelayCommand]
    private void RestartApp()
    {
        Main.SaveSettings();
        try
        {
            // The new instance waits for this one's single-instance lock (same as "restart as administrator").
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppEnvironment.ExecutablePath, "--elevated-restart")
            {
                UseShellExecute = false
            });
            Host.ExitApplication?.Invoke();
        }
        catch (Exception ex)
        {
            Main.ShowToast(Loc.F("ToastActionFailed", ex.Message), error: true);
        }
    }

    // ------------------------------------------------------------------------------------------- start

    public ObservableCollection<Choice> StartPages { get; } = [];

    public Choice? SelectedStartPage
    {
        get => StartPages.FirstOrDefault(c => c.Value == Settings.StartPage);
        set
        {
            if (!_loading && value is not null && value.Value != Settings.StartPage)
            {
                Update(() => Settings.StartPage = value.Value);
            }
        }
    }

    public ObservableCollection<Choice> StartModes { get; } = [];

    public Choice? SelectedStartMode
    {
        get => StartModes.FirstOrDefault(c => c.Value == Settings.StartMode);
        set
        {
            if (!_loading && value is not null && value.Value != Settings.StartMode)
            {
                Update(() => Settings.StartMode = value.Value);
                OnPropertyChanged(nameof(StartModeHint));
            }
        }
    }

    public string StartModeHint => Loc.T("StartModeHint_" + Settings.StartMode);

    /// <summary>"Automatic" plus one entry per connected monitor (left to right).</summary>
    public ObservableCollection<Choice> Monitors { get; } = [];

    public ObservableCollection<Choice> MiniMonitors { get; } = [];

    public Choice? SelectedStartMonitor
    {
        get => Monitors.FirstOrDefault(c => c.Value == Settings.StartMonitor.ToString()) ?? Monitors.FirstOrDefault();
        set
        {
            if (!_loading && value is not null && int.TryParse(value.Value, out var index) && index != Settings.StartMonitor)
            {
                Update(() => Settings.StartMonitor = index);
            }
        }
    }

    public bool StartMiniMonitor
    {
        get => Settings.StartMiniMonitor;
        set => Update(() => Settings.StartMiniMonitor = value);
    }

    private void BuildMonitors()
    {
        _loading = true;
        Monitors.Clear();
        MiniMonitors.Clear();
        Monitors.Add(new Choice("0", Loc.T("MonitorAuto")));
        MiniMonitors.Add(new Choice("0", Loc.T("MonitorPrimary")));
        if (Host.MainWindow?.Screens is { } screens)
        {
            var ordered = Views.MainWindow.OrderedScreens(screens);
            for (var i = 0; i < ordered.Count; i++)
            {
                var screen = ordered[i];
                var title = Loc.F("MonitorN", i + 1, screen.Bounds.Width, screen.Bounds.Height) +
                            (screen.IsPrimary ? " · " + Loc.T("MonitorIsPrimary") : "");
                Monitors.Add(new Choice((i + 1).ToString(), title));
                MiniMonitors.Add(new Choice((i + 1).ToString(), title));
            }
        }

        _loading = false;
        OnPropertyChanged(nameof(SelectedStartMonitor));
        OnPropertyChanged(nameof(SelectedMiniMonitorScreen));
    }

    // ------------------------------------------------------------------------------------------- mini monitor

    public ObservableCollection<AnchorOption> Anchors { get; }

    public string MiniPosition
    {
        get => Settings.MiniPosition;
        set
        {
            if (Settings.MiniPosition == value)
            {
                return;
            }

            UpdateMini(() => Settings.MiniPosition = value);
            foreach (var anchor in Anchors)
            {
                anchor.Refresh();
            }

            OnPropertyChanged(nameof(MiniPositionCustom));
        }
    }

    /// <summary>The mini monitor was dragged: no anchor is selected until one is picked again.</summary>
    public bool MiniPositionCustom => Settings.MiniPosition == "custom";

    public Choice? SelectedMiniMonitorScreen
    {
        get => MiniMonitors.FirstOrDefault(c => c.Value == Settings.MiniMonitorScreen.ToString()) ?? MiniMonitors.FirstOrDefault();
        set
        {
            if (!_loading && value is not null && int.TryParse(value.Value, out var index) && index != Settings.MiniMonitorScreen)
            {
                UpdateMini(() =>
                {
                    Settings.MiniMonitorScreen = index;
                    // A monitor choice re-anchors a dragged mini monitor.
                    if (Settings.MiniPosition == "custom")
                    {
                        Settings.MiniPosition = "br";
                    }
                });
                foreach (var anchor in Anchors)
                {
                    anchor.Refresh();
                }
            }
        }
    }

    public ObservableCollection<Choice> MiniThemes { get; } = [];

    public Choice? SelectedMiniTheme
    {
        get => MiniThemes.FirstOrDefault(c => c.Value == Settings.MiniTheme);
        set
        {
            if (!_loading && value is not null && value.Value != Settings.MiniTheme)
            {
                UpdateMini(() => Settings.MiniTheme = value.Value);
            }
        }
    }

    public double MiniOpacity
    {
        get => Settings.MiniOpacity;
        set
        {
            var rounded = (int)Math.Round(value);
            if (rounded != Settings.MiniOpacity)
            {
                UpdateMini(() => Settings.MiniOpacity = rounded);
                OnPropertyChanged(nameof(MiniOpacityText));
            }
        }
    }

    public string MiniOpacityText => $"{Settings.MiniOpacity} %";

    public bool MiniTopmost
    {
        get => Settings.MiniTopmost;
        set => UpdateMini(() => Settings.MiniTopmost = value);
    }

    [RelayCommand]
    private void ToggleMiniMonitor() => Main.ToggleMiniMonitorCommand.Execute(null);

    private void UpdateMini(Action change)
    {
        Update(change);
        Main.NotifyMiniMonitorSettingsChanged();
    }

    // ------------------------------------------------------------------------------------------- dashboard

    public new Core.Storage.AppSettings Settings => base.Settings;

    /// <summary>Scroll the page to a card ("dashboard": right-click on the dashboard button).</summary>
    public event Action<string>? ScrollRequested;

    public void RequestScrollTo(string section) => Avalonia.Threading.Dispatcher.UIThread.Post(() => ScrollRequested?.Invoke(section),
        Avalonia.Threading.DispatcherPriority.Background);

    public ObservableCollection<DashboardPageOption> DashboardPages { get; } = [];

    public ObservableCollection<Choice> RotationIntervals { get; } = [];

    public bool DashboardRotate
    {
        get => Settings.DashboardRotate;
        set
        {
            Update(() => Settings.DashboardRotate = value);
            Main.ApplyDashboardSettings();
            OnPropertyChanged();
        }
    }

    public Choice? SelectedRotationInterval
    {
        get => RotationIntervals.FirstOrDefault(c => c.Value == Settings.DashboardRotateSeconds.ToString());
        set
        {
            if (!_loading && value is not null && int.TryParse(value.Value, out var seconds) && seconds != Settings.DashboardRotateSeconds)
            {
                Update(() => Settings.DashboardRotateSeconds = seconds);
                Main.ApplyDashboardSettings();
            }
        }
    }

    public bool DashboardShowClock
    {
        get => Settings.DashboardShowClock;
        set
        {
            Update(() => Settings.DashboardShowClock = value);
            Main.ApplyDashboardSettings();
        }
    }

    public bool DashboardHideCursor
    {
        get => Settings.DashboardHideCursor;
        set => Update(() => Settings.DashboardHideCursor = value);
    }

    internal void SetDashboardPage(string id, bool on)
    {
        var pages = Settings.DashboardPages;
        if (on && !pages.Contains(id))
        {
            // Keep the order of the sidebar.
            pages.Add(id);
            var order = Main.AllPages().Select(p => p.Id).ToList();
            pages.Sort((a, b) => order.IndexOf(a).CompareTo(order.IndexOf(b)));
        }
        else if (!on && pages.Count > 1)
        {
            pages.Remove(id);
        }

        Main.SaveSettingsSoon();
        Main.ApplyDashboardSettings();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (var option in DashboardPages)
            {
                option.Refresh();
            }
        });
    }

    // ------------------------------------------------------------------------------------------- alerts

    public bool AlertsEnabled
    {
        get => Settings.AlertsEnabled;
        set
        {
            Update(() => Settings.AlertsEnabled = value);
            OnPropertyChanged();
        }
    }

    public double AlertCpuPercent
    {
        get => Settings.AlertCpuPercent;
        set
        {
            var v = (int)Math.Round(value);
            if (v != Settings.AlertCpuPercent)
            {
                Update(() => Settings.AlertCpuPercent = v);
                OnPropertyChanged(nameof(AlertCpuText));
            }
        }
    }

    public string AlertCpuText => $"{Settings.AlertCpuPercent} %";

    public double AlertMemoryPercent
    {
        get => Settings.AlertMemoryPercent;
        set
        {
            var v = (int)Math.Round(value);
            if (v != Settings.AlertMemoryPercent)
            {
                Update(() => Settings.AlertMemoryPercent = v);
                OnPropertyChanged(nameof(AlertMemoryText));
            }
        }
    }

    public string AlertMemoryText => $"{Settings.AlertMemoryPercent} %";

    public ObservableCollection<Choice> AlertDurations { get; } = [];

    public Choice? SelectedAlertDuration
    {
        get => AlertDurations.FirstOrDefault(c => c.Value == Settings.AlertSeconds.ToString());
        set
        {
            if (!_loading && value is not null && int.TryParse(value.Value, out var seconds) && seconds != Settings.AlertSeconds)
            {
                Update(() => Settings.AlertSeconds = seconds);
            }
        }
    }

    public ObservableCollection<Choice> AlertCooldowns { get; } = [];

    public Choice? SelectedAlertCooldown
    {
        get => AlertCooldowns.FirstOrDefault(c => c.Value == Settings.AlertCooldownMinutes.ToString());
        set
        {
            if (!_loading && value is not null && int.TryParse(value.Value, out var minutes) && minutes != Settings.AlertCooldownMinutes)
            {
                Update(() => Settings.AlertCooldownMinutes = minutes);
            }
        }
    }

    [RelayCommand]
    private void TestAlert() => Main.Alerts.ShowTest();

    // ------------------------------------------------------------------------------------------- expert mode

    public bool ExpertMode
    {
        get => Settings.ExpertMode;
        set
        {
            if (Settings.ExpertMode == value)
            {
                return;
            }

            Update(() => Settings.ExpertMode = value);
            Main.Users.OnSettingsChanged();
            Main.ShowToast(Loc.T(value ? "ToastExpertOn" : "ToastExpertOff"));
        }
    }

    public bool TrimMemoryInTray
    {
        get => Settings.TrimMemoryInTray;
        set => Update(() => Settings.TrimMemoryInTray = value);
    }

    // ------------------------------------------------------------------------------------------- language

    public IReadOnlyList<LanguageOption> Languages => LanguageOption.All;

    public LanguageOption? SelectedLanguage
    {
        get => LanguageOption.Current;
        set
        {
            if (value is not null && value.Code != Loc.Language)
            {
                Main.SetLanguage(value.Code);
                BuildChoices();
                ShellIntegration.RefreshContextMenu(AppEnvironment.ExecutablePath, Loc.T("ContextMenuLabel"));
            }
        }
    }

    // ------------------------------------------------------------------------------------------- monitoring

    public ObservableCollection<Choice> Intervals { get; } = [];

    public ObservableCollection<Choice> HistoryLengths { get; } = [];

    private void BuildChoices()
    {
        _loading = true;
        Intervals.Clear();
        foreach (var (value, key) in new[] { ("500", "Interval500"), ("1000", "Interval1000"), ("2000", "Interval2000"), ("5000", "Interval5000"), ("0", "IntervalPaused") })
        {
            Intervals.Add(new Choice(value, Loc.T(key)));
        }

        HistoryLengths.Clear();
        foreach (var seconds in new[] { 60, 120, 300 })
        {
            HistoryLengths.Add(new Choice(seconds.ToString(), Loc.F("HistorySecondsOption", seconds)));
        }

        UiScales.Clear();
        foreach (var preset in Core.Layout.UiScale.Presets)
        {
            UiScales.Add(new Choice(preset, Loc.T("UiScale_" + preset)));
        }

        RenderModes.Clear();
        RenderModes.Add(new Choice("gpu", Loc.T("RenderGpu")));
        RenderModes.Add(new Choice("software", Loc.T("RenderSoftware")));

        StartPages.Clear();
        StartPages.Add(new Choice("last", Loc.T("StartPageLast")));
        foreach (var page in Main.AllPages().Where(p => p is not null and not SettingsViewModel and not AboutViewModel))
        {
            StartPages.Add(new Choice(page.Id, page.Title));
        }

        StartModes.Clear();
        foreach (var mode in new[] { "window", "maximized", "fullscreen", "dashboard", "tray", "mini" })
        {
            StartModes.Add(new Choice(mode, Loc.T("StartMode_" + mode)));
        }

        RotationIntervals.Clear();
        foreach (var seconds in new[] { 10, 30, 60, 120, 300 })
        {
            RotationIntervals.Add(new Choice(seconds.ToString(), Loc.F("AlertSecondsOption", seconds)));
        }

        DashboardPages.Clear();
        foreach (var page in Main.AllPages().Where(p => p is not null and not SettingsViewModel and not AboutViewModel))
        {
            DashboardPages.Add(new DashboardPageOption(this, page.Id, page.Title));
        }

        AlertDurations.Clear();
        foreach (var seconds in new[] { 10, 30, 60, 120, 300 })
        {
            AlertDurations.Add(new Choice(seconds.ToString(), Loc.F("AlertSecondsOption", seconds)));
        }

        AlertCooldowns.Clear();
        foreach (var minutes in new[] { 5, 10, 30, 60 })
        {
            AlertCooldowns.Add(new Choice(minutes.ToString(), Loc.F("AlertMinutesOption", minutes)));
        }

        MiniThemes.Clear();
        foreach (var theme in new[] { "default", "system", "dark", "shadow", "light" })
        {
            MiniThemes.Add(new Choice(theme, Loc.T("MiniTheme_" + theme)));
        }

        _loading = false;
        OnPropertyChanged(nameof(SelectedInterval));
        OnPropertyChanged(nameof(SelectedHistory));
        OnPropertyChanged(nameof(SelectedUiScale));
        OnPropertyChanged(nameof(SelectedRenderMode));
        OnPropertyChanged(nameof(SelectedStartPage));
        OnPropertyChanged(nameof(SelectedStartMode));
        OnPropertyChanged(nameof(StartModeHint));
        OnPropertyChanged(nameof(SelectedMiniTheme));
        OnPropertyChanged(nameof(SelectedAlertDuration));
        OnPropertyChanged(nameof(SelectedRotationInterval));
        OnPropertyChanged(nameof(SelectedAlertCooldown));
        foreach (var anchor in Anchors)
        {
            anchor.Refresh();
        }
        foreach (var option in Accents.Append(WindowsAccent).Append(CustomAccent))
        {
            option.Refresh();
        }
    }

    public Choice? SelectedInterval
    {
        get => Intervals.FirstOrDefault(c => c.Value == Settings.IntervalMs.ToString());
        set
        {
            if (!_loading && value is not null && int.TryParse(value.Value, out var ms) && ms != Settings.IntervalMs)
            {
                Update(() => Settings.IntervalMs = ms, monitoring: true);
            }
        }
    }

    public Choice? SelectedHistory
    {
        get => HistoryLengths.FirstOrDefault(c => c.Value == Settings.HistorySeconds.ToString());
        set
        {
            if (!_loading && value is not null && int.TryParse(value.Value, out var seconds) && seconds != Settings.HistorySeconds)
            {
                Update(() => Settings.HistorySeconds = seconds, monitoring: true);
            }
        }
    }

    public bool UseProcessorUtility
    {
        get => Settings.UseProcessorUtility;
        set => Update(() => Settings.UseProcessorUtility = value, monitoring: true);
    }

    public bool GpuMonitoring
    {
        get => Settings.GpuMonitoring;
        set => Update(() => Settings.GpuMonitoring = value, monitoring: true);
    }

    public bool RecordAppHistory
    {
        get => Settings.RecordAppHistory;
        set => Update(() => Settings.RecordAppHistory = value, monitoring: true);
    }

    public bool ResolveHostNames
    {
        get => Settings.ResolveHostNames;
        set => Update(() => Settings.ResolveHostNames = value, monitoring: true);
    }

    public bool ConfirmEndTask
    {
        get => Settings.ConfirmEndTask;
        set => Update(() => Settings.ConfirmEndTask = value);
    }

    // ------------------------------------------------------------------------------------------- window & tray

    [ObservableProperty]
    private bool _autostart;

    private bool _autostartKnown;

    partial void OnAutostartChanged(bool value)
    {
        if (!_autostartKnown)
        {
            return;
        }

        var user = Privileges.CurrentUser();
        var ok = value ? SelfAutostart.Register(user, AppEnvironment.ExecutablePath) : SelfAutostart.Unregister(user);
        if (!ok)
        {
            Main.ShowToast(Loc.T("ToastAutostartFailed"), error: true);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _autostartKnown = false;
                Autostart = !value;
                _autostartKnown = true;
            });
        }
    }

    public bool CloseToTray
    {
        get => Settings.CloseToTray;
        set => Update(() => Settings.CloseToTray = value);
    }

    public bool MinimizeToTray
    {
        get => Settings.MinimizeToTray;
        set => Update(() => Settings.MinimizeToTray = value);
    }

    public bool KeepWindowInTray
    {
        get => Settings.KeepWindowInTray;
        set => Update(() => Settings.KeepWindowInTray = value);
    }

    public bool AlwaysOnTop
    {
        get => Settings.AlwaysOnTop;
        set => Update(() =>
        {
            Settings.AlwaysOnTop = value;
            if (Host.MainWindow is { } window)
            {
                window.Topmost = value;
            }
        });
    }

    // ------------------------------------------------------------------------------------------- integration

    [ObservableProperty]
    private bool _contextMenu;

    [ObservableProperty]
    private bool _replaceTaskManager;

    private bool _integrationKnown;

    public bool IsElevated => Main.SystemInfo.IsElevated;

    partial void OnContextMenuChanged(bool value)
    {
        if (!_integrationKnown)
        {
            return;
        }

        var ok = value
            ? ShellIntegration.RegisterContextMenu(AppEnvironment.ExecutablePath, Loc.T("ContextMenuLabel"))
            : ShellIntegration.UnregisterContextMenu();
        Main.ShowToast(ok ? Loc.T(value ? "ToastContextMenuOn" : "ToastContextMenuOff") : Loc.T("ToastIntegrationFailed"), error: !ok);
    }

    partial void OnReplaceTaskManagerChanged(bool value) => _ = ApplyReplaceAsync(value);

    private async Task ApplyReplaceAsync(bool value)
    {
        if (!_integrationKnown)
        {
            return;
        }

        if (value)
        {
            var answer = await Host.ConfirmAsync(Loc.T("DlgReplaceTitle"), Loc.T("DlgReplaceText"), Loc.T("ActReplace"), danger: false,
                confirmIcon: "IconSwapVertical");
            if (!answer.Confirmed)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _integrationKnown = false;
                    ReplaceTaskManager = false;
                    _integrationKnown = true;
                });
                return;
            }
        }

        var ok = value ? ShellIntegration.ReplaceTaskManager(AppEnvironment.ExecutablePath) : ShellIntegration.RestoreTaskManager();
        Main.ShowToast(ok ? Loc.T(value ? "ToastReplaced" : "ToastRestored") : Loc.T("ToastIntegrationFailed"), error: !ok);
        if (!ok)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _integrationKnown = false;
                ReplaceTaskManager = !value;
                _integrationKnown = true;
            });
        }
    }

    // ------------------------------------------------------------------------------------------- page

    public override void OnSettingsChanged()
    {
        OnPropertyChanged(nameof(MiniPositionCustom));
        foreach (var anchor in Anchors)
        {
            anchor.Refresh();
        }
    }

    protected override void OnActivated()
    {
        _integrationKnown = false;
        ContextMenu = ShellIntegration.IsContextMenuRegistered();
        ReplaceTaskManager = ShellIntegration.IsTaskManagerReplaced();
        _integrationKnown = true;

        if (AppearanceService.WindowsAccent() is { } windows)
        {
            WindowsAccent.SetColor(windows);
        }

        BuildMonitors();
        // The mini monitor may have been dragged since the page was built.
        OnPropertyChanged(nameof(MiniPositionCustom));
        foreach (var anchor in Anchors)
        {
            anchor.Refresh();
        }

        _ = LoadAutostartAsync();
    }

    private async Task LoadAutostartAsync()
    {
        var user = Privileges.CurrentUser();
        var registered = await Task.Run(() => SelfAutostart.IsRegistered(user));
        _autostartKnown = false;
        Autostart = registered;
        _autostartKnown = true;
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        BuildChoices();
        BuildMonitors();
        OnPropertyChanged(nameof(SelectedLanguage));
    }

    [RelayCommand]
    private void RestartElevated() => Main.RestartElevatedCommand.Execute(null);

    [RelayCommand]
    private void OpenDataFolder() => AppEnvironment.Open(AppPaths.SettingsDirectory);

    [RelayCommand]
    private async Task Reset()
    {
        var answer = await Host.ConfirmAsync(Loc.T("DlgResetTitle"), Loc.T("DlgResetText"), Loc.T("ActReset"), danger: true,
            confirmIcon: "IconRestart");
        if (!answer.Confirmed)
        {
            return;
        }

        var defaults = new AppSettings { Language = Settings.Language, FirstRunDone = true };
        Settings.CopyFrom(defaults);
        Main.SaveSettings();
        Controls.Motion.Enabled = Settings.Animations;
        AppearanceService.Instance.Apply();
        Main.ApplyMonitoringSettings();
        BuildChoices();
        foreach (var property in new[]
                 {
                     nameof(ThemeSystem), nameof(ThemeDark), nameof(ThemeShadow), nameof(ThemeLight), nameof(ChartFill), nameof(Animations),
                     nameof(UseProcessorUtility), nameof(GpuMonitoring), nameof(RecordAppHistory), nameof(ResolveHostNames), nameof(ConfirmEndTask),
                     nameof(CloseToTray), nameof(MinimizeToTray), nameof(KeepWindowInTray), nameof(AlwaysOnTop),
                     nameof(StartMiniMonitor), nameof(MiniOpacity), nameof(MiniOpacityText), nameof(MiniTopmost), nameof(MiniPositionCustom),
                     nameof(TrimMemoryInTray), nameof(RenderRestartNeeded), nameof(AlertsEnabled), nameof(AlertCpuPercent), nameof(AlertCpuText),
                     nameof(AlertMemoryPercent), nameof(AlertMemoryText), nameof(ExpertMode), nameof(DashboardRotate), nameof(DashboardShowClock),
                     nameof(DashboardHideCursor), nameof(SelectedRotationInterval), nameof(SelectedAlertDuration), nameof(SelectedAlertCooldown), nameof(SelectedStartMonitor), nameof(SelectedMiniMonitorScreen)
                 })
        {
            OnPropertyChanged(property);
        }

        if (Rgb.TryParse(Settings.CustomAccent, out var custom))
        {
            CustomAccent.SetColor(custom);
        }

        Main.IsSidebarCollapsed = Settings.SidebarCollapsed;
        Main.NotifyUiScaleChanged();
        Main.NotifyMiniMonitorSettingsChanged();
        Main.Users.OnSettingsChanged();
        Main.ApplyDashboardSettings();
        foreach (var option in DashboardPages)
        {
            option.Refresh();
        }

        if (Host.MainWindow is { } window)
        {
            window.Topmost = Settings.AlwaysOnTop;
        }
        RefreshAccentSelection();
        Main.ShowToast(Loc.T("ToastReset"));
    }

    private void Update(Action change, bool monitoring = false)
    {
        change();
        Main.SaveSettingsSoon();
        if (monitoring)
        {
            Main.ApplyMonitoringSettings();
        }
    }
}
