using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using TaskNova.App.Localization;
using TaskNova.Core.Storage;

namespace TaskNova.App.ViewModels;

/// <summary>One resizable / hideable list column. Hidden = width 0 in the grid; the width is kept for showing it again.</summary>
public sealed partial class ColumnSpec : ObservableObject
{
    private readonly ColumnLayout _owner;
    private double _width;
    private bool _visible = true;

    public ColumnSpec(ColumnLayout owner, string id, string titleKey, double defaultWidth)
    {
        _owner = owner;
        Id = id;
        TitleKey = titleKey;
        DefaultWidth = defaultWidth;
        _width = defaultWidth;
    }

    public string Id { get; }

    public string TitleKey { get; }

    public double DefaultWidth { get; }

    public string Title => TitleKey == "PID" ? "PID" : Loc.T(TitleKey);

    public double Width
    {
        get => _width;
        set
        {
            var width = Math.Clamp(Math.Round(value), 36, 1200);
            if (SetProperty(ref _width, width))
            {
                OnPropertyChanged(nameof(Length));
                _owner.Store(this);
            }
        }
    }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (SetProperty(ref _visible, value))
            {
                OnPropertyChanged(nameof(Length));
                _owner.Store(this);
            }
        }
    }

    /// <summary>Grid column width (0 when hidden).</summary>
    public GridLength Length => new(_visible ? _width : 0);

    internal void Load(double stored)
    {
        _visible = stored > 0;
        _width = Math.Clamp(Math.Abs(stored), 36, 1200);
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(Length));
    }

    internal void Reset()
    {
        _visible = true;
        _width = DefaultWidth;
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(Length));
    }

    public void RefreshTitle() => OnPropertyChanged(nameof(Title));
}

/// <summary>
/// Column widths and visibility of one list, stored in the settings ("details.cpu" → 72, negative = hidden).
/// Drag the right edge of a header to resize; right-click the header to show or hide columns.
/// </summary>
public abstract partial class ColumnLayout : ObservableObject
{
    private readonly string _listId;
    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly List<ColumnSpec> _columns = [];
    private bool _loading;

    protected ColumnLayout(string listId, AppSettings settings, Action save)
    {
        _listId = listId;
        _settings = settings;
        _save = save;
        Loc.LanguageChanged += () =>
        {
            foreach (var column in _columns)
            {
                column.RefreshTitle();
            }
        };
    }

    public IReadOnlyList<ColumnSpec> All => _columns;

    protected ColumnSpec Add(string id, string titleKey, double width)
    {
        var spec = new ColumnSpec(this, id, titleKey, width);
        _columns.Add(spec);
        if (_settings.ColumnLayouts.TryGetValue($"{_listId}.{id}", out var stored) && stored != 0)
        {
            _loading = true;
            spec.Load(stored);
            _loading = false;
        }

        return spec;
    }

    internal void Store(ColumnSpec spec)
    {
        if (_loading)
        {
            return;
        }

        var key = $"{_listId}.{spec.Id}";
        if (spec.Visible && Math.Abs(spec.Width - spec.DefaultWidth) < 0.5)
        {
            _settings.ColumnLayouts.Remove(key);
        }
        else
        {
            _settings.ColumnLayouts[key] = spec.Visible ? spec.Width : -spec.Width;
        }

        _save();
    }

    [RelayCommand]
    private void Reset()
    {
        foreach (var column in _columns)
        {
            column.Reset();
            _settings.ColumnLayouts.Remove($"{_listId}.{column.Id}");
        }

        _save();
    }
}

/// <summary>Columns of the processes list (width, visibility).</summary>
public sealed class ProcessColumns : ColumnLayout
{
    public ProcessColumns(AppSettings settings, Action save) : base("processes", settings, save)
    {
        Status = Add("status", "ColStatus", 96);
        Cpu = Add("cpu", "ColCpu", 80);
        Memory = Add("memory", "ColMemory", 104);
        Io = Add("io", "ColIo", 100);
        Gpu = Add("gpu", "ColGpu", 76);
        Pid = Add("pid", "PID", 74);
    }

    public ColumnSpec Status { get; }

    public ColumnSpec Cpu { get; }

    public ColumnSpec Memory { get; }

    public ColumnSpec Io { get; }

    public ColumnSpec Gpu { get; }

    public ColumnSpec Pid { get; }
}

/// <summary>Columns of the details list (width, visibility).</summary>
public sealed class DetailsColumns : ColumnLayout
{
    public DetailsColumns(AppSettings settings, Action save) : base("details", settings, save)
    {
        Name = Add("name", "ColName", 200);
        Pid = Add("pid", "PID", 70);
        Status = Add("status", "ColStatus", 96);
        User = Add("user", "ColUser", 130);
        Session = Add("session", "ColSession", 70);
        Cpu = Add("cpu", "ColCpu", 72);
        CpuTime = Add("cputime", "ColCpuTime", 90);
        Memory = Add("memory", "ColMemory", 100);
        WorkingSet = Add("workingset", "ColWorkingSet", 104);
        Private = Add("private", "ColPrivateBytes", 104);
        Threads = Add("threads", "ColThreads", 74);
        Handles = Add("handles", "ColHandles", 80);
        Priority = Add("priority", "ColPriority", 110);
        Io = Add("io", "ColIo", 96);
        IoRead = Add("ioread", "ColIoRead", 100);
        IoWrite = Add("iowrite", "ColIoWrite", 100);
        Gpu = Add("gpu", "ColGpu", 70);
        Platform = Add("platform", "ColPlatform", 80);
        Description = Add("description", "ColDescription", 220);
        CommandLine = Add("commandline", "ColCommandLine", 640);
    }

    public ColumnSpec Name { get; }

    public ColumnSpec Pid { get; }

    public ColumnSpec Status { get; }

    public ColumnSpec User { get; }

    public ColumnSpec Session { get; }

    public ColumnSpec Cpu { get; }

    public ColumnSpec CpuTime { get; }

    public ColumnSpec Memory { get; }

    public ColumnSpec WorkingSet { get; }

    public ColumnSpec Private { get; }

    public ColumnSpec Threads { get; }

    public ColumnSpec Handles { get; }

    public ColumnSpec Priority { get; }

    public ColumnSpec Io { get; }

    public ColumnSpec IoRead { get; }

    public ColumnSpec IoWrite { get; }

    public ColumnSpec Gpu { get; }

    public ColumnSpec Platform { get; }

    public ColumnSpec Description { get; }

    public ColumnSpec CommandLine { get; }
}
