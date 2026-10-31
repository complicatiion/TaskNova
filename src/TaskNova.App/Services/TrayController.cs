using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using TaskNova.App.Localization;
using TaskNova.App.ViewModels;
using TaskNova.Core.Appearance;

namespace TaskNova.App.Services;

/// <summary>
/// System tray: the TaskNova logo in the accent color, CPU and memory in the tooltip, left click opens the window,
/// right click shows the menu (every entry with an icon).
/// </summary>
public sealed class TrayController : IDisposable
{
    // Native tray menus have no theme: neutral icons, accent for the main entries.
    private static readonly Rgb IconColor = Rgb.FromHex("#8A8DA3");

    private readonly MainViewModel _main;
    private readonly Action _showMainWindow;
    private readonly Action _toggleMiniMonitor;
    private readonly Action _exit;
    private readonly TrayIcon _trayIcon;
    private string _tooltip = "";
    private bool _paused;

    public TrayController(Application app, MainViewModel main, Action showMainWindow, Action toggleMiniMonitor, Action exit)
    {
        _main = main;
        _showMainWindow = showMainWindow;
        _toggleMiniMonitor = toggleMiniMonitor;
        _exit = exit;

        _trayIcon = new TrayIcon { ToolTipText = "TaskNova", IsVisible = true };
        _trayIcon.Clicked += (_, _) => _showMainWindow();
        TrayIcon.SetIcons(app, new TrayIcons { _trayIcon });

        _main.StateChanged += UpdateTooltip;
        AppearanceService.Instance.Changed += Rebuild;
        Loc.LanguageChanged += Rebuild;
        Rebuild();
    }

    private Rgb Accent => AppearanceService.Instance.Accent.Fill;

    private void Rebuild()
    {
        // The logo is drawn in the accent color: a changed accent shows up in the tray as well.
        _trayIcon.Icon = new WindowIcon(IconCache.TintedLogo(Accent, 64));
        _paused = _main.IsPaused;
        _trayIcon.Menu = BuildMenu();
        _tooltip = "";
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        var tip = $"TaskNova · CPU {_main.CpuText} · RAM {_main.MemoryText}";
        if (_main.IsPaused)
        {
            tip += " · " + Loc.T("TrayPaused");
        }

        if (tip != _tooltip)
        {
            _tooltip = tip;
            _trayIcon.ToolTipText = tip;
        }

        if (_paused != _main.IsPaused)
        {
            _paused = _main.IsPaused;
            _trayIcon.Menu = BuildMenu();
        }
    }

    private NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();
        var header = new NativeMenuItem("TaskNova  v" + AppEnvironment.Version) { Icon = IconCache.TintedLogo(Accent, 32) };
        header.Click += (_, _) => _showMainWindow();
        menu.Add(header);
        menu.Add(new NativeMenuItemSeparator());

        menu.Add(Item(Loc.T("TrayOpen"), "IconWindowRestore", _showMainWindow, Accent));
        menu.Add(Item(Loc.T("TrayMiniMonitor"), "IconMonitorDashboard", _toggleMiniMonitor));
        menu.Add(Item(Loc.T("TrayRunTask"), "IconPlaylistPlus", () => _main.RunTaskCommand.Execute(null)));
        menu.Add(new NativeMenuItemSeparator());

        menu.Add(Item(Loc.T("NavOverview"), "IconViewDashboard", () => Navigate(_main.Overview)));
        menu.Add(Item(Loc.T("NavProcesses"), "IconApplicationOutline", () => Navigate(_main.ProcessesPage)));
        menu.Add(Item(Loc.T("NavPerformance"), "IconPulse", () => Navigate(_main.Performance)));
        menu.Add(Item(Loc.T("NavNetwork"), "IconLan", () => Navigate(_main.Network)));
        menu.Add(Item(Loc.T("NavDetails"), "IconFormatListBulleted", () => Navigate(_main.Details)));
        menu.Add(Item(Loc.T("NavStartup"), "IconStartup", () => Navigate(_main.Startup)));
        menu.Add(Item(Loc.T("NavUsers"), "IconAccountMultiple", () => Navigate(_main.Users)));
        menu.Add(Item(Loc.T("NavServices"), "IconCogs", () => Navigate(_main.Services)));
        menu.Add(new NativeMenuItemSeparator());

        menu.Add(_main.IsPaused
            ? Item(Loc.T("TrayResume"), "IconPlay", () => _main.TogglePauseCommand.Execute(null), Accent)
            : Item(Loc.T("TrayPause"), "IconPause", () => _main.TogglePauseCommand.Execute(null)));
        menu.Add(Item(Loc.T("NavSettings"), "IconCog", () => Navigate(_main.SettingsPage)));
        menu.Add(Submenu(Loc.T("LblLanguage"), "IconLanguage", BuildLanguageMenu()));
        menu.Add(Item(Loc.T("NavAbout"), "IconInfo", () => Navigate(_main.About)));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item(Loc.T("TrayExit"), "IconExit", _exit));
        return menu;
    }

    private NativeMenu BuildLanguageMenu()
    {
        var menu = new NativeMenu();
        foreach (var language in LanguageOption.All)
        {
            var code = language.Code;
            var header = language.Code == Loc.Language ? $"{language.Name}  ✓" : language.Name;
            var item = new NativeMenuItem(header) { Icon = language.Flag ?? IconCache.Render("IconLanguage", IconColor) };
            item.Click += (_, _) => _main.SetLanguage(code);
            menu.Add(item);
        }

        return menu;
    }

    private void Navigate(PageViewModel page)
    {
        _showMainWindow();
        _main.NavigateTo(page);
    }

    private static NativeMenuItem Item(string header, string iconKey, Action action, Rgb? color = null)
    {
        var item = new NativeMenuItem(header) { Icon = IconCache.Render(iconKey, color ?? IconColor) };
        item.Click += (_, _) => action();
        return item;
    }

    private static NativeMenuItem Submenu(string header, string iconKey, NativeMenu menu) =>
        new(header) { Icon = IconCache.Render(iconKey, IconColor), Menu = menu };

    public void Dispose()
    {
        _main.StateChanged -= UpdateTooltip;
        AppearanceService.Instance.Changed -= Rebuild;
        Loc.LanguageChanged -= Rebuild;
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }
}
