using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using TaskNova.App.Localization;
using TaskNova.App.Services;
using TaskNova.App.ViewModels;
using TaskNova.App.Views;
using TaskNova.Core.Formatting;
using TaskNova.Core.History;
using TaskNova.Core.Storage;
using TaskNova.Platform.Windows;

namespace TaskNova.App;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainViewModel? _vm;
    private MainWindow? _mainWindow;
    private MiniMonitorWindow? _miniMonitor;
    private TrayController? _tray;
    private SamplingService? _sampling;
    private ProcessEnricher? _enricher;
    private bool _exiting;
    private string? _pendingStartMode;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var settings = Program.InitialSettings ?? SettingsStore.Load();
            settings.FirstRunDone = true;

            // No language saved yet: German if Windows shows German (display language, region or keyboard), else English.
            Loc.Apply(settings.Language ?? (OperatingSystem.IsWindows() ? SystemLanguage.Detect() : Loc.DefaultLanguage));
            Fmt.Culture = System.Globalization.CultureInfo.CurrentCulture;
            Controls.Motion.Enabled = settings.Animations;

            // Colors before the first window exists: no flash of default colors.
            AppearanceService.Instance.Attach(this, settings);

            var system = StaticInfoReader.Read();
            var history = new AppHistoryStore();
            history.Load();
            _enricher = new ProcessEnricher();
            _sampling = new SamplingService(history, system.BaseMhz)
            {
                IntervalMs = settings.IntervalMs,
                UseProcessorUtility = settings.UseProcessorUtility,
                GpuEnabled = settings.GpuMonitoring,
                RecordHistory = settings.RecordAppHistory
            };

            var host = new UiHost
            {
                ShowMainWindow = ShowMainWindow,
                ExitApplication = Exit
            };
            _vm = new MainViewModel(host, settings, system, _sampling, _enricher, history)
            {
                IsSidebarCollapsed = settings.SidebarCollapsed
            };
            _vm.MiniMonitorRequested += ToggleMiniMonitor;

            _tray = new TrayController(this, _vm, ShowMainWindow, ToggleMiniMonitor, Exit);
            desktop.Exit += (_, _) => Shutdown();

            StartShowSignalListener();
            _sampling.Start();

            // Start mode: window, maximized, full screen, dashboard, only the tray or only the mini monitor.
            // "--tray" (autostart) always starts in the tray.
            var mode = Program.StartInTray ? "tray" : settings.StartMode;
            if (mode is "tray" or "mini")
            {
                _vm.OnWindowVisibilityChanged(false);
            }
            else
            {
                _pendingStartMode = mode;
                ShowMainWindow();
            }

            if (mode == "mini" || settings.StartMiniMonitor)
            {
                Dispatcher.UIThread.Post(ToggleMiniMonitor, DispatcherPriority.Background);
            }

            SettingsStore.Save(settings);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Creates the window on demand (it may have been released in the tray) and brings it to the front.</summary>
    private void ShowMainWindow()
    {
        if (_vm is null || _exiting)
        {
            return;
        }

        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow { DataContext = _vm };
            _mainWindow.TrayRequested += HideToTray;
            _mainWindow.ExitRequested += Exit;
            _vm.Host.MainWindow = _mainWindow;
            if (_pendingStartMode is { } mode)
            {
                _pendingStartMode = null;
                _mainWindow.ApplyStartMode(mode);
            }
        }

        var window = _mainWindow;
        if (!window.IsVisible)
        {
            window.Show();
            _vm.OnWindowVisibilityChanged(true);
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = _vm.Settings.WindowMaximized ? WindowState.Maximized : WindowState.Normal;
        }

        window.Activate();
    }

    /// <summary>
    /// Close/minimize to the tray. With "keep window" the window is only hidden (opens instantly); otherwise it is closed
    /// and re-created on demand (lowest memory in the tray).
    /// </summary>
    private void HideToTray()
    {
        if (_mainWindow is null || _vm is null)
        {
            return;
        }

        _mainWindow.SaveWindowState();
        if (_vm.Settings.KeepWindowInTray)
        {
            _mainWindow.Hide();
        }
        else
        {
            var window = _mainWindow;
            _mainWindow = null;
            _vm.Host.MainWindow = null;
            window.AllowClose = true;
            window.Close();
        }

        _vm.OnWindowVisibilityChanged(false);
        Dispatcher.UIThread.Post(_vm.ReleaseMemory, DispatcherPriority.ContextIdle);
    }

    private void ToggleMiniMonitor()
    {
        if (_vm is null)
        {
            return;
        }

        if (_miniMonitor is not null)
        {
            _miniMonitor.Close();
            return;
        }

        _miniMonitor = new MiniMonitorWindow { DataContext = new MiniMonitorViewModel(_vm) };
        _miniMonitor.OpenMainRequested += ShowMainWindow;
        _miniMonitor.Closed += (_, _) =>
        {
            _miniMonitor = null;
            _vm.SetMiniMonitorOpen(false);
        };
        _vm.SetMiniMonitorOpen(true);
        _miniMonitor.Show();
    }

    private void Exit()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _miniMonitor?.Close();
        if (_mainWindow is not null)
        {
            _mainWindow.SaveWindowState();
            _mainWindow.AllowClose = true;
            _mainWindow.Close();
        }

        _desktop?.Shutdown();
    }

    private void Shutdown()
    {
        if (_vm is not null)
        {
            _vm.SaveSettings();
            _vm.Processes.Dispose();
        }

        _tray?.Dispose();
        _sampling?.Dispose();
        _enricher?.Dispose();
    }

    /// <summary>A second start (desktop menu, Ctrl+Shift+Esc redirect, shortcut) brings this window to the front.</summary>
    private void StartShowSignalListener()
    {
        var signal = Program.ShowSignal;
        if (signal is null)
        {
            return;
        }

        var thread = new Thread(() =>
        {
            try
            {
                while (signal.WaitOne())
                {
                    Dispatcher.UIThread.Post(ShowMainWindow);
                }
            }
            catch (ObjectDisposedException)
            {
                // App is exiting.
            }
        })
        {
            IsBackground = true,
            Name = "TaskNova.ShowSignal"
        };
        thread.Start();
    }
}
