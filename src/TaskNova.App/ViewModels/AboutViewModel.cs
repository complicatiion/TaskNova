using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskNova.App.Localization;
using TaskNova.App.Services;
using TaskNova.Core.Formatting;

namespace TaskNova.App.ViewModels;

public sealed record ComponentInfo(string Name, string License, string Use, string File);

public sealed record PrivacyPoint(Geometry? Icon, string Title, string Text);

/// <summary>About: version, license, links and the third-party components with their licenses.</summary>
public sealed partial class AboutViewModel : PageViewModel
{
    private readonly DispatcherTimer _timer;

    public AboutViewModel(MainViewModel main) : base(main)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => UpdateMemory();
        PrivacyPoints = BuildPrivacy();
    }

    public override string TitleKey => "NavAbout";

    public override string IconKey => "IconInfo";

    public override string Id => "about";

    public override bool SupportsSearch => false;

    public string Version => AppEnvironment.Version;

    public string VersionText => Loc.F("AboutVersion", Version);

    public IReadOnlyList<ComponentInfo> Components { get; } =
    [
        new(".NET 10", "MIT", "Runtime", "DOTNET.md"),
        new("Avalonia UI 11.3", "MIT", "UI framework, Fluent theme, tray", "AVALONIA.md"),
        new("CommunityToolkit.Mvvm 8.4", "MIT", "MVVM source generators", "COMMUNITYTOOLKIT-MVVM.md"),
        new("SkiaSharp / HarfBuzzSharp", "MIT / BSD-3-Clause", "Rendering (via Avalonia)", "SKIASHARP.md"),
        new("Inter", "SIL OFL 1.1", "UI font", "INTER-FONT.md"),
        new("Material Design Icons", "Apache 2.0", "Icons", "MATERIAL-DESIGN-ICONS.md")
    ];

    // ------------------------------------------------------------------ privacy

    public IReadOnlyList<PrivacyPoint> PrivacyPoints { get; private set; }

    private static IReadOnlyList<PrivacyPoint> BuildPrivacy() =>
    [
        new(Res.Icon("IconShieldCheck"), Loc.T("PrivLocalTitle"), Loc.T("PrivLocalText")),
        new(Res.Icon("IconEarth"), Loc.T("PrivNetworkTitle"), Loc.T("PrivNetworkText")),
        new(Res.Icon("IconLan"), Loc.T("PrivDnsTitle"), Loc.T("PrivDnsText")),
        new(Res.Icon("IconFolder"), Loc.T("PrivFilesTitle"), Loc.T("PrivFilesText")),
        new(Res.Icon("IconHistory"), Loc.T("PrivHistoryTitle"), Loc.T("PrivHistoryText"))
    ];

    // ------------------------------------------------------------------ memory diagnostics

    [ObservableProperty]
    private string _workingSetText = "–";

    [ObservableProperty]
    private string _privateText = "–";

    [ObservableProperty]
    private string _managedText = "–";

    [ObservableProperty]
    private string _gcText = "–";

    public string RenderingText => Loc.T(Program.InitialSettings?.RenderMode == "software" ? "RenderSoftware" : "RenderGpu");

    public string BuildText => (RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "Native AOT") +
#if DEBUG
                               " · Debug";
#else
                               " · Release";
#endif

    protected override void OnActivated()
    {
        UpdateMemory();
        _timer.Start();
    }

    protected override void OnDeactivated() => _timer.Stop();

    private void UpdateMemory()
    {
        using var process = Process.GetCurrentProcess();
        WorkingSetText = Fmt.Bytes(process.WorkingSet64);
        PrivateText = Fmt.Bytes(process.PrivateMemorySize64);
        var info = GC.GetGCMemoryInfo();
        ManagedText = $"{Fmt.Bytes(GC.GetTotalMemory(false))} · {Loc.F("DiagCommitted", Fmt.Bytes(info.TotalCommittedBytes))}";
        GcText = $"{GC.CollectionCount(0)} / {GC.CollectionCount(1)} / {GC.CollectionCount(2)}";
    }

    [RelayCommand]
    private void FlushMemory()
    {
        // .NET heap first, then the working set: idle pages go back to Windows, so the value in the Task Manager drops visibly.
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        TaskNova.Platform.Windows.SystemTools.TrimOwnWorkingSet();
        UpdateMemory();
        Main.ShowToast(Loc.T("ToastMemoryReleased"));
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(RenderingText));
        PrivacyPoints = BuildPrivacy();
        OnPropertyChanged(nameof(PrivacyPoints));
    }

    [RelayCommand]
    private void OpenWebsite() => AppEnvironment.OpenUrl(AppEnvironment.WebsiteUrl);

    [RelayCommand]
    private void OpenProject() => AppEnvironment.OpenUrl(AppEnvironment.ProjectUrl);

    [RelayCommand]
    private void OpenLicense() => AppEnvironment.Open(Path.Combine(AppEnvironment.LicensesDirectory, "LICENSE.md"));

    [RelayCommand]
    private void OpenLicenses() => AppEnvironment.Open(AppEnvironment.LicensesDirectory);

    [RelayCommand]
    private void OpenComponent(ComponentInfo? component)
    {
        if (component is not null)
        {
            AppEnvironment.Open(Path.Combine(AppEnvironment.LicensesDirectory, "third-party", component.File));
        }
    }
}
