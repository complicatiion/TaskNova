using Avalonia.Controls;
using TaskNova.App.Services;
using TaskNova.App.ViewModels;

namespace TaskNova.App.Views;

/// <summary>Process window (one per process): facts, live charts, modules. Releases its sampling demand when closed.</summary>
public partial class ProcessWindow : Window
{
    public ProcessWindow()
    {
        InitializeComponent();
        WindowChrome.Apply(this);
        Closed += (_, _) => (DataContext as ProcessWindowViewModel)?.Dispose();
    }
}
