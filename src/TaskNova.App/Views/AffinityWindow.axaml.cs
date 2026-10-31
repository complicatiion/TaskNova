using Avalonia.Controls;
using Avalonia.Interactivity;
using TaskNova.App.Localization;

namespace TaskNova.App.Views;

/// <summary>CPU affinity of a process: one check box per logical processor of the current processor group.</summary>
public partial class AffinityWindow : Window
{
    private readonly List<(CheckBox Box, int Index)> _boxes = [];
    private bool _syncing;

    public AffinityWindow()
    {
        InitializeComponent();
        Services.WindowChrome.Apply(this);
    }

    public AffinityWindow(string processName, ulong current, ulong system, int processors) : this()
    {
        Title = Loc.T("ActAffinity");
        HeaderText.Text = Loc.F("AffinityTitle", processName);

        for (var i = 0; i < processors && i < 64; i++)
        {
            var bit = 1UL << i;
            if ((system & bit) == 0)
            {
                continue;
            }

            var box = new CheckBox { Content = Loc.F("AffinityCpu", i), IsChecked = (current & bit) != 0, Width = 110, Margin = new Avalonia.Thickness(0, 0, 8, 4) };
            box.IsCheckedChanged += (_, _) => Sync();
            _boxes.Add((box, i));
            CpuPanel.Children.Add(box);
        }

        AllBox.IsCheckedChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            _syncing = true;
            foreach (var (box, _) in _boxes)
            {
                box.IsChecked = AllBox.IsChecked == true;
            }

            _syncing = false;
            Sync();
        };
        Sync();
    }

    /// <summary>The selected mask (never 0: at least one processor must stay).</summary>
    public ulong Mask => _boxes.Where(b => b.Box.IsChecked == true).Aggregate(0UL, (mask, b) => mask | (1UL << b.Index));

    private void Sync()
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        AllBox.IsChecked = _boxes.All(b => b.Box.IsChecked == true);
        _syncing = false;
        ConfirmButton.IsEnabled = Mask != 0;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(Mask != 0);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
