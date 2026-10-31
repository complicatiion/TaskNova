using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TaskNova.App.ViewModels;

namespace TaskNova.App.Views;

/// <summary>Threshold alert: bottom right above the taskbar of the primary screen, not activated, closes after 10 s.</summary>
public partial class AlertWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };

    public AlertWindow()
    {
        InitializeComponent();
    }

    public AlertWindow(string title, string text, string iconKey) : this()
    {
        HeaderText.Text = title;
        BodyText.Text = text;
        Glyph.Data = Res.Icon(iconKey);
        _timer.Tick += (_, _) => Close();
        Frame.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual source && Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Button>(source, includeSelf: true) is not null)
            {
                return;
            }

            OpenRequested?.Invoke();
            Close();
        };
        // Hover keeps it open.
        Frame.PointerEntered += (_, _) => _timer.Stop();
        Frame.PointerExited += (_, _) => _timer.Start();
        Opened += (_, _) =>
        {
            Place();
            // Shown only after it sits in the corner (no flash at the default position).
            Opacity = 1;
            _timer.Start();
        };
        SizeChanged += (_, _) => Place();
        Closed += (_, _) => _timer.Stop();
    }

    public event Action? OpenRequested;

    private void Place()
    {
        if (Screens.Primary is not { } screen || Bounds.Width <= 0)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        Position = new PixelPoint(area.Right - (int)(Bounds.Width * scale) - (int)(16 * scale),
            area.Bottom - (int)(Bounds.Height * scale) - (int)(16 * scale));
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
