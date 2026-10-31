using CommunityToolkit.Mvvm.ComponentModel;
using TaskNova.App.Localization;
using TaskNova.App.Services;
using TaskNova.Core.Models;
using TaskNova.Core.Search;
using TaskNova.Core.Storage;

namespace TaskNova.App.ViewModels;

/// <summary>Base of all sidebar pages.</summary>
public abstract class PageViewModel(MainViewModel main) : ObservableObject
{
    public MainViewModel Main { get; } = main;

    protected UiHost Host => Main.Host;

    protected AppSettings Settings => Main.Settings;

    public abstract string TitleKey { get; }

    public abstract string IconKey { get; }

    /// <summary>Stable id for "last page" in the settings.</summary>
    public abstract string Id { get; }

    public string Title => Loc.T(TitleKey);

    /// <summary>Data this page needs from the sampler while it is visible.</summary>
    public virtual SampleDemand Demand => SampleDemand.None;

    /// <summary>The header search box applies to this page.</summary>
    public virtual bool SupportsSearch => true;

    /// <summary>Placeholder of the search box on this page.</summary>
    public virtual string SearchHintKey => "SearchHint";

    public bool IsActive { get; private set; }

    protected SearchMatcher Search => Main.Matcher;

    internal void Activate()
    {
        IsActive = true;
        OnActivated();
    }

    internal void Deactivate()
    {
        IsActive = false;
        OnDeactivated();
    }

    /// <summary>The page became visible (start own timers, load data).</summary>
    protected virtual void OnActivated()
    {
    }

    /// <summary>Another page was opened or the window was hidden (stop timers).</summary>
    protected virtual void OnDeactivated()
    {
    }

    /// <summary>A new sampler snapshot – only called while the page is active.</summary>
    public virtual void OnSnapshot(SystemSnapshot snapshot)
    {
    }

    public virtual void OnSearchChanged()
    {
    }

    /// <summary>F5 / refresh button.</summary>
    public virtual void Refresh()
    {
    }

    public virtual void OnLanguageChanged() => OnPropertyChanged(nameof(Title));

    public virtual void OnSettingsChanged()
    {
    }
}
