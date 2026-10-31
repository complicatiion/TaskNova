using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskNova.App.Localization;

namespace TaskNova.App.ViewModels;

/// <summary>Sidebar entry.</summary>
public sealed partial class NavItem : ObservableObject
{
    public NavItem(PageViewModel page)
    {
        Page = page;
        Icon = Res.Icon(page.IconKey);
    }

    public PageViewModel Page { get; }

    public Geometry? Icon { get; }

    public string Title => Page.Title;

    /// <summary>Small counter on the icon (e.g. number of processes).</summary>
    [ObservableProperty]
    private string? _badge;

    public bool HasBadge => !string.IsNullOrEmpty(Badge);

    partial void OnBadgeChanged(string? value) => OnPropertyChanged(nameof(HasBadge));

    public void RefreshText() => OnPropertyChanged(nameof(Title));
}

/// <summary>Sidebar section with its own list; only one section has a selection at a time.</summary>
public sealed partial class NavGroup : ObservableObject
{
    public NavGroup(string? titleKey, IEnumerable<NavItem> items)
    {
        TitleKey = titleKey;
        Items = new ObservableCollection<NavItem>(items);
    }

    public string? TitleKey { get; }

    public string Title => TitleKey is null ? "" : Loc.T(TitleKey);

    public bool HasTitle => TitleKey is not null;

    public ObservableCollection<NavItem> Items { get; }

    [ObservableProperty]
    private NavItem? _selected;

    public event Action<NavGroup, NavItem>? Activated;

    partial void OnSelectedChanged(NavItem? value)
    {
        if (value is not null)
        {
            Activated?.Invoke(this, value);
        }
    }

    public void RefreshText()
    {
        OnPropertyChanged(nameof(Title));
        foreach (var item in Items)
        {
            item.RefreshText();
        }
    }
}
