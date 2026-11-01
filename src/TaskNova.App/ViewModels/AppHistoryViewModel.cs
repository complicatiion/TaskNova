using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNova.App.Localization;
using TaskNova.Core.Formatting;
using TaskNova.Core.History;

namespace TaskNova.App.ViewModels;

public sealed record AppHistoryRow(string Name, string Title, string? Path, long CpuTime, string CpuTimeText, ulong Io, string IoText,
    double RunSeconds, string RunText, DateTime LastSeenUtc, string LastSeenText, double CpuShare) : Controls.ITypeAheadItem
{
    string Controls.ITypeAheadItem.TypeAheadText => Title;
}

/// <summary>
/// App history: CPU time and I/O per program, recorded by TaskNova itself while it runs (the Windows SRUM database is
/// locked; TaskNova keeps its own small JSON file per user).
/// </summary>
public sealed partial class AppHistoryViewModel : PageViewModel
{
    private readonly DispatcherTimer _timer;
    private string _sort = "cpu";
    private bool _descending = true;

    public AppHistoryViewModel(MainViewModel main) : base(main)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) => Load();
    }

    public override string TitleKey => "NavAppHistory";

    public override string IconKey => "IconHistory";

    public override string Id => "apphistory";

    public override string SearchHintKey => "SearchApps";

    public ObservableCollection<AppHistoryRow> Items { get; } = [];

    [ObservableProperty]
    private AppHistoryRow? _selected;

    [ObservableProperty]
    private string _since = "";

    public bool Recording => Settings.RecordAppHistory;

    public string SortColumn => _sort;

    public bool SortDescending => _descending;

    protected override void OnActivated()
    {
        Load();
        _timer.Start();
    }

    protected override void OnDeactivated() => _timer.Stop();

    public override void OnSearchChanged() => Load();

    public override void Refresh() => Load();

    public override void OnSettingsChanged() => OnPropertyChanged(nameof(Recording));

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        Load();
    }

    [RelayCommand]
    private void Sort(string column)
    {
        _descending = _sort == column ? !_descending : column != "name";
        _sort = column;
        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortDescending));
        Load();
    }

    private void Load()
    {
        var store = Main.AppHistory;
        Since = Fmt.Date(store.StartedUtc);
        var entries = store.Snapshot();
        var totalCpu = Math.Max(1L, entries.Sum(e => e.CpuTime100Ns));
        var rows = entries
            .Where(e => Search.IsEmpty || Search.Matches(e.Name, e.Description, e.Path))
            .Select(e => new AppHistoryRow(
                e.Name,
                string.IsNullOrWhiteSpace(e.Description) ? e.Name : $"{e.Description} ({e.Name})",
                e.Path,
                e.CpuTime100Ns,
                Fmt.CpuTime(e.CpuTime100Ns),
                e.IoBytes,
                Fmt.Bytes(e.IoBytes),
                e.RunSeconds,
                Fmt.Idle(TimeSpan.FromSeconds(e.RunSeconds)),
                e.LastSeenUtc,
                Fmt.Date(e.LastSeenUtc),
                e.CpuTime100Ns * 100d / totalCpu));

        rows = _sort switch
        {
            "name" => _descending ? rows.OrderByDescending(r => r.Title, StringComparer.CurrentCultureIgnoreCase) : rows.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase),
            "io" => _descending ? rows.OrderByDescending(r => r.Io) : rows.OrderBy(r => r.Io),
            "run" => _descending ? rows.OrderByDescending(r => r.RunSeconds) : rows.OrderBy(r => r.RunSeconds),
            "last" => _descending ? rows.OrderByDescending(r => r.LastSeenUtc) : rows.OrderBy(r => r.LastSeenUtc),
            _ => _descending ? rows.OrderByDescending(r => r.CpuTime) : rows.OrderBy(r => r.CpuTime)
        };

        // Replace in place: scroll position and the containers stay while the values refresh.
        var selectedName = Selected?.Name;
        var list = rows.Take(500).ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (i < Items.Count)
            {
                if (Items[i] != list[i])
                {
                    Items[i] = list[i];
                }
            }
            else
            {
                Items.Add(list[i]);
            }
        }

        while (Items.Count > list.Count)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        Selected = selectedName is null ? null : Items.FirstOrDefault(r => r.Name == selectedName);
    }

    [RelayCommand]
    private async Task Clear()
    {
        var answer = await Host.ConfirmAsync(Loc.T("DlgClearHistoryTitle"), Loc.T("DlgClearHistoryText"), Loc.T("ActClearHistory"), danger: true,
            confirmIcon: "IconDelete");
        if (answer.Confirmed)
        {
            Main.AppHistory.Clear();
            Load();
        }
    }
}
