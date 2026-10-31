using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TaskNova.App.Services;
using TaskNova.App.ViewModels;
using TaskNova.Core.Layout;

namespace TaskNova.App.Views;

/// <summary>
/// Compact always-on-top monitor: placed by a 3 × 3 anchor on a chosen monitor (or where it was dragged to),
/// with its own theme, opacity and topmost setting. Double-click opens the main window.
/// </summary>
public partial class MiniMonitorWindow : Window
{
    private const int EdgeMargin = 16;
    private ResourceDictionary? _themeResources;
    private bool _dragging;

    public MiniMonitorWindow()
    {
        InitializeComponent();
        Frame.PointerPressed += OnPointerPressed;
        Frame.DoubleTapped += (_, e) =>
        {
            // A double click on a metric toggles its details twice – it does not open the main window.
            if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            {
                return;
            }

            OpenMainRequested?.Invoke();
        };
        Opened += (_, _) =>
        {
            ApplySettings();
            Place();
        };
        SizeChanged += (_, _) => Place();
        AppearanceService.Instance.Changed += ApplyTheme;
        Closed += (_, _) =>
        {
            AppearanceService.Instance.Changed -= ApplyTheme;
            if (DataContext is MiniMonitorViewModel vm)
            {
                vm.Main.MiniMonitorSettingsChanged -= OnSettingsChanged;
                vm.LayoutChanged -= OnLayoutChanged;
                vm.Dispose();
            }
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MiniMonitorViewModel vm)
            {
                vm.Main.MiniMonitorSettingsChanged += OnSettingsChanged;
                vm.LayoutChanged += OnLayoutChanged;
            }
        };
    }

    public event Action? OpenMainRequested;

    private MiniMonitorViewModel? Model => DataContext as MiniMonitorViewModel;

    private void OnSettingsChanged()
    {
        ApplySettings();
        Place();
    }

    // Height changes with the expanded detail: keep the anchored edge where it is (e.g. bottom right grows upwards).
    private void OnLayoutChanged() => Dispatcher.UIThread.Post(Place, DispatcherPriority.Render);

    private void ApplySettings()
    {
        if (Model is not { } vm)
        {
            return;
        }

        var settings = vm.Main.Settings;
        Topmost = settings.MiniTopmost;
        Opacity = Math.Clamp(settings.MiniOpacity, 40, 100) / 100d;
        ApplyTheme();
    }

    /// <summary>Own theme of the mini monitor ("default" = same as the app).</summary>
    private void ApplyTheme()
    {
        if (Model is not { } vm)
        {
            return;
        }

        var dictionary = AppearanceService.Instance.BuildWindowResources(vm.Main.Settings.MiniTheme, out var variant);
        if (_themeResources is not null)
        {
            Resources.MergedDictionaries.Remove(_themeResources);
        }

        _themeResources = dictionary;
        if (dictionary is not null)
        {
            Resources.MergedDictionaries.Add(dictionary);
        }

        RequestedThemeVariant = dictionary is null ? null : variant;
    }

    /// <summary>Anchor position on the chosen monitor, or the dragged position ("custom").</summary>
    private void Place()
    {
        if (Model is not { } vm || _dragging || Bounds.Width <= 0)
        {
            return;
        }

        var settings = vm.Main.Settings;
        if (settings is { MiniPosition: "custom", MiniMonitorX: { } x, MiniMonitorY: { } y })
        {
            var point = new PixelPoint(x, y);
            if (Screens.All.Any(s => s.WorkingArea.Contains(point + new PixelPoint(20, 20))))
            {
                Position = point;
                return;
            }
        }

        var screen = MainWindow.ScreenFor(Screens, settings.MiniMonitorScreen);
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var anchor = settings.MiniPosition == "custom" ? "br" : settings.MiniPosition;
        var (px, py) = Placement.Compute(anchor, area.X, area.Y, area.Width, area.Height,
            (int)Math.Ceiling(Bounds.Width * scale), (int)Math.Ceiling(Bounds.Height * scale), (int)(EdgeMargin * scale));
        Position = new PixelPoint(px, py);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.ClickCount != 1)
        {
            return;
        }

        var before = Position;
        _dragging = true;
        BeginMoveDrag(e);
        _dragging = false;

        // Dragged: the position becomes "custom" until an anchor is picked again in the settings.
        if (Position != before && Model is { } vm)
        {
            vm.Main.Settings.MiniPosition = "custom";
            vm.Main.Settings.MiniMonitorX = Position.X;
            vm.Main.Settings.MiniMonitorY = Position.Y;
            vm.Main.SaveSettingsSoon();
            vm.Main.SettingsPage.OnSettingsChanged();
        }
    }

    private void OnOpenMain(object? sender, RoutedEventArgs e) => OpenMainRequested?.Invoke();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
