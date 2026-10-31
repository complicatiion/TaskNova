using Avalonia;
using Avalonia.Controls;
using TaskNova.App.ViewModels;
using TaskNova.App.Views;

namespace TaskNova.App.Controls;

/// <summary>
/// Hosts the page views and only toggles their visibility: switching pages neither re-creates nor re-styles them.
/// Each view is created on its first visit; the least recently used ones are released to keep memory low.
/// </summary>
public sealed class PageHost : Grid
{
    public static readonly StyledProperty<PageViewModel?> PageProperty =
        AvaloniaProperty.Register<PageHost, PageViewModel?>(nameof(Page));

    private const int MaxViews = 2;

    private readonly Dictionary<PageViewModel, Control> _views = new();
    private readonly List<PageViewModel> _recent = [];

    static PageHost()
    {
        PageProperty.Changed.AddClassHandler<PageHost>((host, _) => host.ShowPage());
    }

    public PageViewModel? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    private void ShowPage()
    {
        var page = Page;
        if (page is not null && !_views.ContainsKey(page))
        {
            var view = Create(page);
            view.DataContext = page;
            view.IsVisible = false;
            _views[page] = view;
            Children.Add(view);
        }

        if (page is not null)
        {
            _recent.Remove(page);
            _recent.Add(page);
            while (_recent.Count > MaxViews)
            {
                var oldest = _recent[0];
                _recent.RemoveAt(0);
                if (_views.Remove(oldest, out var old))
                {
                    Children.Remove(old);
                    old.DataContext = null;
                }
            }
        }

        foreach (var (model, view) in _views)
        {
            var visible = ReferenceEquals(model, page);
            if (visible && !view.IsVisible)
            {
                view.IsVisible = true;
                Motion.FadeIn(view, offsetY: 12, milliseconds: 220);
            }
            else
            {
                view.IsVisible = visible;
            }
        }
    }

    private static Control Create(PageViewModel page) => page switch
    {
        OverviewViewModel => new OverviewView(),
        ProcessesViewModel => new ProcessesView(),
        PerformanceViewModel => new PerformanceView(),
        AppHistoryViewModel => new AppHistoryView(),
        NetworkViewModel => new NetworkView(),
        DetailsViewModel => new DetailsView(),
        StartupViewModel => new StartupView(),
        UsersViewModel => new UsersView(),
        ServicesViewModel => new ServicesView(),
        FileLocksViewModel => new FileLocksView(),
        PortsViewModel => new PortsView(),
        PowerPlansViewModel => new PowerPlansView(),
        SystemReportViewModel => new SystemReportView(),
        SettingsViewModel => new SettingsView(),
        AboutViewModel => new AboutView(),
        _ => new TextBlock { Text = page.GetType().Name }
    };
}
