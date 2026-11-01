using Avalonia.Controls;
using TaskNova.App.ViewModels;

namespace TaskNova.App.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _vm;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
            {
                _vm.ScrollRequested -= OnScrollRequested;
            }

            _vm = DataContext as SettingsViewModel;
            if (_vm is not null)
            {
                _vm.ScrollRequested += OnScrollRequested;
            }
        };
    }

    /// <summary>Jump to a card (e.g. dashboard settings from the right-click menu of the dashboard button).</summary>
    private void OnScrollRequested(string section)
    {
        if (section == "dashboard")
        {
            DashboardCard.BringIntoView();
            Controls.Motion.FadeIn(DashboardCard, offsetY: 8, milliseconds: 260);
        }
    }
}
