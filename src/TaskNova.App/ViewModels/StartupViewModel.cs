using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNova.App.Localization;
using TaskNova.App.Services;
using TaskNova.Platform.Windows;

namespace TaskNova.App.ViewModels;

/// <summary>Startup entry with a switch (like the Windows Task Manager: reversible via StartupApproved).</summary>
public sealed partial class StartupRow : ObservableObject
{
    private readonly StartupViewModel _owner;
    private bool _enabled;

    public StartupRow(StartupViewModel owner, StartupEntry entry, Bitmap? icon)
    {
        _owner = owner;
        Entry = entry;
        Icon = icon;
        _enabled = entry.Enabled;
    }

    public StartupEntry Entry { get; private set; }

    public Bitmap? Icon { get; }

    public string Name => Entry.Name;

    public string Publisher => Entry.Publisher ?? "–";

    public string Command => Entry.Command;

    public bool Signed => Entry.Signed;

    public bool Missing => Entry.TargetMissing;

    public string LocationText => Loc.T("StartupLoc" + Entry.Location);

    public string StatusText => Loc.T(Enabled ? "StatusEnabled" : "StatusDisabled");

    /// <summary>Not measured: the boot-time impact would need an ETW boot trace.</summary>
    public string ImpactText => Loc.T("ImpactNotMeasured");

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            if (StartupManager.SetEnabled(Entry, value))
            {
                _enabled = value;
                Entry = Entry with { Enabled = value };
                _owner.Main.ShowToast(Loc.F(value ? "ToastStartupEnabled" : "ToastStartupDisabled", Name));
                _owner.UpdateSummary();
                _owner.OnRowToggled(this);
            }
            else
            {
                _owner.Main.ShowToast(Loc.F("ToastStartupFailed", Name), error: true);
            }

            // After the switch has finished writing: shows the real state (also when the change failed).
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(Enabled));
                OnPropertyChanged(nameof(StatusText));
            });
        }
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(LocationText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ImpactText));
    }
}

/// <summary>Startup apps: Run keys, startup folders and logon/boot tasks with publisher, signature and switch.</summary>
public sealed partial class StartupViewModel(MainViewModel main) : PageViewModel(main)
{
    private List<StartupRow> _all = [];
    private bool _loaded;

    public override string TitleKey => "NavStartup";

    public override string IconKey => "IconStartup";

    public override string Id => "startup";

    public override string SearchHintKey => "SearchStartup";

    public ObservableCollection<StartupRow> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedEnabled), nameof(SelectedStatusText))]
    private StartupRow? _selected;

    public bool HasSelection => Selected is not null;

    /// <summary>Toolbar switch: on/off of the selected entry.</summary>
    public bool SelectedEnabled
    {
        get => Selected?.Enabled == true;
        set
        {
            if (Selected is { } row && row.Enabled != value)
            {
                row.Enabled = value;
            }

            // Shows the real state, also when the change failed.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(SelectedEnabled));
                OnPropertyChanged(nameof(SelectedStatusText));
            });
        }
    }

    public string SelectedStatusText => Selected is { } row ? row.StatusText : Loc.T("StatusNoSelection");

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _summary = "";

    private readonly StatPill _pillEntries = new("PillEntries");
    private readonly StatPill _pillEnabled = new("PillEnabled");

    /// <summary>Key figures of the toolbar, each in its own pill.</summary>
    public IReadOnlyList<StatPill> Stats => [_pillEntries, _pillEnabled];


    public bool NeedsElevation => !Main.SystemInfo.IsElevated;

    protected override void OnActivated()
    {
        if (!_loaded)
        {
            _ = LoadAsync();
        }
    }

    public override void Refresh() => _ = LoadAsync();

    public override void OnSearchChanged() => Apply();

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var row in _all)
        {
            row.RefreshTexts();
        }

        OnPropertyChanged(nameof(SelectedStatusText));
        Apply();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            // STA thread: shell icons of the targets need COM.
            var loaded = await Sta.Run(() => StartupManager.ReadAll()
                .Select(e => (Entry: e, Pixels: e.TargetPath is { } path && File.Exists(path) ? IconExtractor.Extract(path) : null))
                .ToList());
            _all = loaded.Select(l => new StartupRow(this, l.Entry, IconCache.FromPixels(l.Entry.TargetPath, l.Pixels, IconExtractor.Size))).ToList();
            _loaded = true;
            Apply();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Apply()
    {
        var rows = _all.Where(r => Search.IsEmpty || Search.Matches(r.Name, r.Publisher, r.Command, r.LocationText)).ToList();
        ListSync.Sync(Items, rows, () => Selected, v => Selected = v);
        UpdateSummary();
    }

    internal void OnRowToggled(StartupRow row)
    {
        if (ReferenceEquals(row, Selected))
        {
            OnPropertyChanged(nameof(SelectedEnabled));
            OnPropertyChanged(nameof(SelectedStatusText));
        }
    }

    internal void UpdateSummary()
    {
        Summary = Loc.F("StartupSummary", _all.Count, _all.Count(r => r.Enabled));
        _pillEntries.Set(_all.Count);
        _pillEnabled.Set(_all.Count(r => r.Enabled));
    }

    [RelayCommand]
    private void OpenLocation()
    {
        if (Selected is { } row)
        {
            StartupManager.OpenLocation(row.Entry);
        }
    }

    [RelayCommand]
    private async Task CopyCommand()
    {
        if (Selected is { } row)
        {
            await Host.CopyAsync(row.Command);
            Main.ShowToast(Loc.T("ToastCopied"));
        }
    }

    [RelayCommand]
    private void Toggle()
    {
        if (Selected is { } row)
        {
            row.Enabled = !row.Enabled;
        }
    }
}
