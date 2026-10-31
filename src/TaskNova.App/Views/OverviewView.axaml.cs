using Avalonia.Controls;

namespace TaskNova.App.Views;

public partial class OverviewView : UserControl
{
    // Below this list width (normal window) the usage bars of the top lists get narrow, so more of the name is visible.
    private const double CompactWidth = 420;

    public OverviewView()
    {
        InitializeComponent();
        // The layout fills the visible height: the last row (top lists, system) takes the remaining space. On very small
        // windows the content is taller than the viewport and the scroll viewer scrolls as before.
        Scroller.SizeChanged += (_, e) =>
        {
            Layout.MinHeight = Math.Max(0, e.NewSize.Height - 4);
            UpdateTopCount();
        };
        BottomRow.SizeChanged += (_, _) => UpdateTopCount();
        TopCpuList.SizeChanged += (_, e) => TopCpuList.Classes.Set("compact", e.NewSize.Width < CompactWidth);
        TopMemoryList.SizeChanged += (_, e) => TopMemoryList.Classes.Set("compact", e.NewSize.Width < CompactWidth);
    }

    /// <summary>
    /// More top entries when the cards are taller (maximized, dashboard, large screens): one per 28 px row, 5 to 20.
    /// Measured from the height the viewport leaves for the last row – not from the row's own height, which grows with the
    /// entries and would keep a once-large list large (or let it grow) after the window gets smaller.
    /// </summary>
    private void UpdateTopCount()
    {
        if (DataContext is ViewModels.OverviewViewModel vm)
        {
            var available = Layout.MinHeight - BottomRow.Bounds.Y;
            vm.TopCount = Math.Clamp((int)((available - 70) / 28), 5, 20);
        }
    }
}
