using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using TaskNova.App.Services;
using TaskNova.App.ViewModels;
using TaskNova.Core.Layout;

namespace TaskNova.App.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;
    private WindowState _beforeDashboard = WindowState.Normal;
    private double _scale = 1;
    private Screen? _scaleScreen;
    private readonly DispatcherTimer _cursorTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _applyingDashboard;

    public MainWindow()
    {
        InitializeComponent();
        WindowChrome.Apply(this);
        Closing += OnClosing;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                if (WindowState == WindowState.Minimized && _vm?.Settings.MinimizeToTray == true)
                {
                    SaveWindowState();
                    TrayRequested?.Invoke();
                }
                else if (!_applyingDashboard && _vm is { IsDashboard: true } vm && WindowState is WindowState.Normal or WindowState.Maximized)
                {
                    // Left full screen by other means (Win+↓, title bar): leave dashboard mode too.
                    vm.IsDashboard = false;
                }

                RememberSoon();
            }
        };

        // Size and position are kept up to date while the window lives (written debounced): a log-off or kill keeps them.
        PositionChanged += (_, _) =>
        {
            RememberSoon();
            if (Screens.ScreenFromWindow(this) is { } screen && !Equals(screen, _scaleScreen))
            {
                ApplyScale();
            }
        };
        Resized += (_, _) => RememberSoon();
        ScalingChanged += (_, _) => ApplyScale();

        // Mouse side buttons: back / forward like in a browser (also when a child already handled the press).
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
            if (kind == PointerUpdateKind.XButton1Pressed && _vm?.GoBackCommand.CanExecute(null) == true)
            {
                _vm.GoBackCommand.Execute(null);
                e.Handled = true;
            }
            else if (kind == PointerUpdateKind.XButton2Pressed && _vm?.GoForwardCommand.CanExecute(null) == true)
            {
                _vm.GoForwardCommand.Execute(null);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);

        // Dashboard: hide the mouse pointer after a few seconds without movement (setting).
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorTimer.Stop();
            if (_vm is { IsDashboard: true, Settings.DashboardHideCursor: true })
            {
                Cursor = new Cursor(StandardCursorType.None);
            }
        };
        AddHandler(PointerMovedEvent, (_, _) =>
        {
            if (_vm?.IsDashboard != true)
            {
                return;
            }

            Cursor = Cursor.Default;
            _cursorTimer.Stop();
            _cursorTimer.Start();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        Opened += (_, _) => ApplyScale();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm || ReferenceEquals(vm, _vm))
            {
                return;
            }

            _vm = vm;
            RestoreWindowState(vm);
            // The view model outlives this window (it may be released in the tray): unsubscribe on close.
            Action focus = () =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            };
            Action scale = ApplyScale;
            PropertyChangedEventHandler changed = (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.Toast) && vm.HasToast)
                {
                    Controls.Motion.FadeIn(ToastHost, offsetX: 24, offsetY: 0, milliseconds: 220);
                }
                else if (e.PropertyName == nameof(MainViewModel.IsDashboard))
                {
                    ApplyDashboard(vm.IsDashboard);
                }
            };
            vm.FocusSearchRequested += focus;
            vm.UiScaleChanged += scale;
            vm.PropertyChanged += changed;
            Closed += (_, _) =>
            {
                vm.FocusSearchRequested -= focus;
                vm.UiScaleChanged -= scale;
                vm.PropertyChanged -= changed;
            };
        };
    }

    /// <summary>Set on exit or when the window is released in the tray; otherwise closing follows the tray setting.</summary>
    public bool AllowClose { get; set; }

    /// <summary>Close button with "close to tray" or minimize with "minimize to tray".</summary>
    public event Action? TrayRequested;

    public event Action? ExitRequested;

    /// <summary>Screens ordered like the monitor list in the settings: left to right, then top to bottom.</summary>
    public static IReadOnlyList<Screen> OrderedScreens(Screens screens) =>
        screens.All.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToList();

    /// <summary>Monitor setting 0 = automatic (last position, else primary), 1..n = that monitor.</summary>
    public static Screen? ScreenFor(Screens screens, int index)
    {
        var ordered = OrderedScreens(screens);
        return index >= 1 && index <= ordered.Count ? ordered[index - 1] : screens.Primary ?? ordered.FirstOrDefault();
    }

    /// <summary>Normal window by default (never full screen on the first start); size and position as last time.</summary>
    private void RestoreWindowState(MainViewModel vm)
    {
        var settings = vm.Settings;
        Topmost = settings.AlwaysOnTop;

        var target = settings.StartMonitor > 0 ? ScreenFor(Screens, settings.StartMonitor) : null;
        var point = settings is { WindowX: { } x, WindowY: { } y } ? new PixelPoint(x, y) : (PixelPoint?)null;
        var savedScreen = point is { } p ? Screens.All.FirstOrDefault(s => s.WorkingArea.Contains(p + new PixelPoint(40, 20))) : null;

        // A fixed start monitor wins over the last position when the last position is on another monitor.
        var screen = target ?? savedScreen ?? Screens.Primary;
        var area = screen?.WorkingArea;
        var scaling = screen?.Scaling ?? 1;

        // Default 1180 × 760, never larger than the working area of the monitor (small laptop screens, 1366 × 768).
        var maxWidth = area is { } a1 ? a1.Width / scaling - 24 : double.MaxValue;
        var maxHeight = area is { } a2 ? a2.Height / scaling - 24 : double.MaxValue;
        Width = Math.Clamp(settings.WindowWidth, MinWidth, Math.Max(MinWidth, maxWidth));
        Height = Math.Clamp(settings.WindowHeight, MinHeight, Math.Max(MinHeight, maxHeight));

        if (savedScreen is not null && (target is null || ReferenceEquals(savedScreen, target)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = point!.Value;
        }
        else if (target is { } t)
        {
            // Centered on the chosen monitor.
            var w = (int)(Width * t.Scaling);
            var h = (int)(Height * t.Scaling);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(t.WorkingArea.X + (t.WorkingArea.Width - w) / 2, t.WorkingArea.Y + (t.WorkingArea.Height - h) / 2);
        }

        if (settings.WindowMaximized)
        {
            SetStateAfterOpen(WindowState.Maximized);
        }
    }

    /// <summary>Start mode of the first window: maximized, full screen or dashboard (window and tray are handled by the app).</summary>
    public void ApplyStartMode(string mode)
    {
        switch (mode)
        {
            case "maximized":
                SetStateAfterOpen(WindowState.Maximized);
                break;
            case "fullscreen":
                SetStateAfterOpen(WindowState.FullScreen);
                break;
            case "dashboard":
                Opened += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    if (_vm is { } vm && !vm.IsDashboard)
                    {
                        vm.ToggleDashboardCommand.Execute(null);
                    }
                }, DispatcherPriority.Loaded);
                break;
        }
    }

    private void SetStateAfterOpen(WindowState state)
    {
        // After the first layout pass: maximizing earlier can push the title bar off screen.
        Opened += (_, _) => Dispatcher.UIThread.Post(() => WindowState = state, DispatcherPriority.Loaded);
    }

    private void ApplyDashboard(bool on)
    {
        Cursor = Cursor.Default;
        _cursorTimer.Stop();
        if (on)
        {
            _cursorTimer.Start();
        }

        // Win32 may report an intermediate Normal state while switching Maximized -> FullScreen: not a "left full screen".
        _applyingDashboard = true;
        Dispatcher.UIThread.Post(() => _applyingDashboard = false, DispatcherPriority.Background);
        if (on)
        {
            if (WindowState != WindowState.FullScreen)
            {
                _beforeDashboard = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
                WindowState = WindowState.FullScreen;
            }
        }
        else if (WindowState == WindowState.FullScreen)
        {
            WindowState = _beforeDashboard == WindowState.FullScreen ? WindowState.Normal : _beforeDashboard;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && _vm is { IsDashboard: true } vm)
        {
            vm.IsDashboard = false;
            e.Handled = true;
        }
    }

    /// <summary>
    /// UI size: XS–XL as fixed factors, "auto" from the logical working area of the monitor the window is on
    /// (e.g. S on 1366 × 768 laptops, XL on large 4K screens at 100 %).
    /// </summary>
    private void ApplyScale()
    {
        if (_vm is null)
        {
            return;
        }

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        _scaleScreen = screen;
        var scaling = screen?.Scaling ?? 1;
        var area = screen?.WorkingArea ?? default;
        var factor = UiScale.Resolve(_vm.Settings.UiScale, area.Width / scaling, area.Height / scaling);
        if (Math.Abs(factor - _scale) < 0.001 && ScaleHost.LayoutTransform is not null)
        {
            return;
        }

        _scale = factor;
        ScaleHost.LayoutTransform = Math.Abs(factor - 1) < 0.001 ? null : new ScaleTransform(factor, factor);
        MinWidth = 860 * Math.Min(1, factor);
        MinHeight = 540 * Math.Min(1, factor);
    }

    private void RememberSoon()
    {
        if (_vm is null || !IsVisible)
        {
            return;
        }

        if (Remember())
        {
            _vm.SaveSettingsSoon();
        }
    }

    /// <summary>Copies size, position and state into the settings; true when something changed.</summary>
    private bool Remember()
    {
        if (_vm is null || WindowState is WindowState.Minimized or WindowState.FullScreen)
        {
            return false;
        }

        var settings = _vm.Settings;
        var changed = false;
        var maximized = WindowState == WindowState.Maximized;
        if (settings.WindowMaximized != maximized)
        {
            settings.WindowMaximized = maximized;
            changed = true;
        }

        if (WindowState == WindowState.Normal)
        {
            var width = Math.Round(Bounds.Width);
            var height = Math.Round(Bounds.Height);
            if (width > 0 && height > 0 && (settings.WindowWidth != width || settings.WindowHeight != height ||
                                            settings.WindowX != Position.X || settings.WindowY != Position.Y))
            {
                settings.WindowWidth = width;
                settings.WindowHeight = height;
                settings.WindowX = Position.X;
                settings.WindowY = Position.Y;
                changed = true;
            }
        }

        return changed;
    }

    public void SaveWindowState()
    {
        if (_vm is null)
        {
            return;
        }

        Remember();
        _vm.SaveSettings();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        SaveWindowState();
        if (AllowClose || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
        {
            return;
        }

        // Act after the close message has been handled: hiding or closing inside Closing is fragile.
        e.Cancel = true;
        var toTray = _vm?.Settings.CloseToTray == true;
        Dispatcher.UIThread.Post(() =>
        {
            if (toTray)
            {
                TrayRequested?.Invoke();
            }
            else
            {
                ExitRequested?.Invoke();
            }
        });
    }
}
