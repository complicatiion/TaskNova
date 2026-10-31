using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using TaskNova.App.ViewModels;

namespace TaskNova.App.Views;

public partial class NetworkView : UserControl
{
    private const double CardWidth = 320;

    public NetworkView()
    {
        InitializeComponent();
        AdapterList.SizeChanged += (_, e) => UpdateColumns(e.NewSize.Width);
        AdapterList.Loaded += (_, _) => UpdateColumns(AdapterList.Bounds.Width);
        // Double-click on a connection: its process on the Processes page.
        ConnectionList.DoubleTapped += (_, e) =>
        {
            if (e.Source is Control { DataContext: ConnectionRow } && DataContext is NetworkViewModel vm)
            {
                vm.GoToProcessesCommand.Execute(null);
            }
        };
    }

    /// <summary>2 cards per row in a normal window; one more for every ~320 px of width, at most 5 (maximized / full screen).</summary>
    private void UpdateColumns(double width)
    {
        if (AdapterList.ItemsPanelRoot is UniformGrid grid && width > 0)
        {
            var columns = Math.Clamp((int)(width / CardWidth), 2, 5);
            if (grid.Columns != columns)
            {
                grid.Columns = columns;
            }
        }
    }
}
