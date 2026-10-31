using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using TaskNova.Core.Appearance;
using TaskNova.Core.Storage;
using TaskNova.Platform.Windows;

namespace TaskNova.App.Services;

/// <summary>
/// Single source of all theme and accent colors. Resolves the theme setting (system/dark/light/shadow) and the accent
/// (preset, Windows accent or custom color), computes the palettes in TaskNova.Core and swaps one resource dictionary –
/// one change notification for the whole UI. Also re-tints the Fluent palettes so check marks, switches, selections and
/// focus rectangles use the accent instead of system blue.
/// </summary>
public sealed class AppearanceService
{
    public static AppearanceService Instance { get; } = new();

    private AppSettings? _settings;
    private ResourceDictionary? _applied;
    private bool _applying;

    private AppearanceService()
    {
    }

    public ThemePalette Theme { get; private set; } = ThemePalette.Dark;

    public AccentPalette Accent { get; private set; } = AccentPalette.Compute(AccentPresets.Brand.Color, ThemePalette.Dark);

    /// <summary>Raised on the UI thread after every change (tray icon, title bars, heat brushes, charts).</summary>
    public event Action? Changed;

    public void Attach(Application app, AppSettings settings)
    {
        _settings = settings;
        app.ActualThemeVariantChanged += (_, _) =>
        {
            if (_settings?.Theme == "system")
            {
                Apply();
            }
        };

        if (app.PlatformSettings is { } platform)
        {
            platform.ColorValuesChanged += (_, _) =>
            {
                if (_settings?.AccentId == AccentPresets.WindowsId || _settings?.Theme == "system")
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(Apply);
                }
            };
        }

        Apply();
    }

    /// <summary>Resolves the accent color of the current settings.</summary>
    public Rgb ResolveAccent()
    {
        var settings = _settings;
        if (settings is null)
        {
            return AccentPresets.Brand.Color;
        }

        if (settings.AccentId == AccentPresets.CustomId && Rgb.TryParse(settings.CustomAccent, out var custom))
        {
            return custom;
        }

        if (settings.AccentId == AccentPresets.WindowsId)
        {
            return WindowsAccent() ?? AccentPresets.Brand.Color;
        }

        return AccentPresets.Find(settings.AccentId)?.Color ?? AccentPresets.Brand.Color;
    }

    /// <summary>The Windows accent color (Avalonia platform settings, registry as fallback).</summary>
    public static Rgb? WindowsAccent()
    {
        var color = Application.Current?.PlatformSettings?.GetColorValues().AccentColor1;
        if (color is { A: > 0 } c)
        {
            return new Rgb(c.R, c.G, c.B);
        }

        return OperatingSystem.IsWindows() ? WindowsTheme.AccentColor() : null;
    }

    public void Apply()
    {
        if (_applying || Application.Current is not { } app || _settings is not { } settings)
        {
            return;
        }

        _applying = true;
        try
        {
            app.RequestedThemeVariant = settings.Theme switch
            {
                "light" => ThemeVariant.Light,
                "system" => ThemeVariant.Default,
                _ => ThemeVariant.Dark
            };

            var kind = settings.Theme switch
            {
                "light" => ThemeKind.Light,
                "shadow" => ThemeKind.Shadow,
                "system" => app.ActualThemeVariant == ThemeVariant.Light ? ThemeKind.Light : ThemeKind.Dark,
                _ => ThemeKind.Dark
            };

            var accent = ResolveAccent();
            Theme = ThemePalette.For(kind);
            Accent = AccentPalette.Compute(accent, Theme);

            SwapResources(app, BuildResources(Theme, Accent));
            UpdateFluentPalettes(app, accent);
            HeatBrushes.Rebuild(Accent.Fill);
            WindowChrome.RefreshAll();
            Changed?.Invoke();
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>
    /// Colors for a window with its own theme (mini monitor). Null for "default": the window then simply uses the app colors.
    /// The theme variant is returned too, so Fluent controls inside the window match.
    /// </summary>
    public ResourceDictionary? BuildWindowResources(string theme, out ThemeVariant variant)
    {
        variant = ThemeVariant.Default;
        if (theme == "default" || Application.Current is not { } app)
        {
            return null;
        }

        var kind = theme switch
        {
            "light" => ThemeKind.Light,
            "shadow" => ThemeKind.Shadow,
            "dark" => ThemeKind.Dark,
            _ => app.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Light ? ThemeKind.Light : ThemeKind.Dark
        };
        variant = kind == ThemeKind.Light ? ThemeVariant.Light : ThemeVariant.Dark;
        var palette = ThemePalette.For(kind);
        return BuildResources(palette, AccentPalette.Compute(ResolveAccent(), palette));
    }

    private void SwapResources(Application app, ResourceDictionary dictionary)
    {
        var merged = app.Resources.MergedDictionaries;
        merged.Add(dictionary);
        if (_applied is not null)
        {
            merged.Remove(_applied);
        }

        _applied = dictionary;
    }

    private static ResourceDictionary BuildResources(ThemePalette theme, AccentPalette accent)
    {
        var d = new ResourceDictionary();

        void Set(string name, Rgb color, byte alpha = 255)
        {
            var c = Color.FromArgb(alpha, color.R, color.G, color.B);
            d["Color" + name] = c;
            d["Brush" + name] = new SolidColorBrush(c);
        }

        Set("BgApp", theme.BgApp);
        Set("BgSidebar", theme.BgSidebar);
        Set("SurfaceCard", theme.SurfaceCard);
        Set("SurfaceRaised", theme.SurfaceRaised);
        Set("SurfaceHover", theme.SurfaceHover);
        Set("Border", theme.Border);
        Set("TextPrimary", theme.TextPrimary);
        Set("TextSecondary", theme.TextSecondary);
        Set("TextDisabled", theme.TextDisabled);
        Set("Safe", theme.Safe);
        Set("SafeSoft", theme.Soft(theme.Safe));
        Set("Moderate", theme.Moderate);
        Set("ModerateSoft", theme.Soft(theme.Moderate));
        Set("Deep", theme.Deep);
        Set("DeepSoft", theme.Soft(theme.Deep));
        Set("Info", theme.Info);
        Set("InfoSoft", theme.Soft(theme.Info));
        Set("Danger", theme.Danger);
        Set("DangerHover", ColorMath.ShiftLightness(theme.Danger, 0.06));
        Set("DangerPressed", ColorMath.ShiftLightness(theme.Danger, -0.06));
        Set("ChartGrid", theme.ChartGrid);
        Set("Shadow", new Rgb(0, 0, 0), theme.IsDark ? (byte)0x66 : (byte)0x22);

        Set("Accent", accent.Fill);
        Set("AccentHover", accent.Hover);
        Set("AccentPressed", accent.Pressed);
        Set("AccentText", accent.Text);
        Set("AccentSoft", accent.Soft);
        Set("MenuHover", accent.MenuHover);
        Set("AccentOnFill", accent.OnFill);
        Set("AccentSecondary", accent.Secondary);
        Set("AccentGlow", accent.Fill, 0x59);

        var start = Color.FromRgb(accent.Fill.R, accent.Fill.G, accent.Fill.B);
        var end = Color.FromRgb(accent.GradientEnd.R, accent.GradientEnd.G, accent.GradientEnd.B);
        d["BrushBrandGradient"] = Gradient(start, end, new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 0, RelativeUnit.Relative));
        d["BrushLogoGradient"] = Gradient(start, end, new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 1, RelativeUnit.Relative));
        d["BrushNavMarker"] = Gradient(start, end, new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative));
        return d;
    }

    private static LinearGradientBrush Gradient(Color start, Color end, RelativePoint from, RelativePoint to) => new()
    {
        StartPoint = from,
        EndPoint = to,
        GradientStops = { new GradientStop(start, 0), new GradientStop(end, 1) }
    };

    /// <summary>Fluent control templates (check boxes, switches, combo boxes, scroll bars) in TaskNova colors.</summary>
    private static void UpdateFluentPalettes(Application app, Rgb accent)
    {
        var fluent = app.Styles.OfType<FluentTheme>().FirstOrDefault();
        if (fluent is null)
        {
            return;
        }

        static Color C(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

        // Dark templates carry the dark and the shadow theme.
        var darkTheme = Instance.Theme.Kind == ThemeKind.Shadow ? ThemePalette.Shadow : ThemePalette.Dark;
        var darkAccent = AccentPalette.Compute(accent, darkTheme);
        if (fluent.Palettes.TryGetValue(ThemeVariant.Dark, out var dark))
        {
            dark.Accent = C(darkAccent.Fill);
            dark.RegionColor = C(darkTheme.SurfaceCard);
            dark.AltHigh = C(darkTheme.SurfaceCard);
            dark.ListLow = C(darkTheme.SurfaceHover);
            dark.ListMedium = C(darkTheme.Border);
            dark.ChromeMediumLow = C(darkTheme.SurfaceRaised);
            dark.ChromeMedium = C(darkTheme.SurfaceHover);
            dark.ChromeLow = C(darkTheme.SurfaceCard);
            dark.ChromeAltLow = C(darkTheme.TextPrimary);
            dark.BaseHigh = C(darkTheme.TextPrimary);
        }

        var lightAccent = AccentPalette.Compute(accent, ThemePalette.Light);
        if (fluent.Palettes.TryGetValue(ThemeVariant.Light, out var light))
        {
            light.Accent = C(lightAccent.Fill);
            light.RegionColor = C(ThemePalette.Light.SurfaceCard);
            light.BaseHigh = C(ThemePalette.Light.TextPrimary);
        }
    }
}

/// <summary>
/// Accent tints for heat cells (CPU, memory, I/O, GPU columns): 11 shared brushes, rebuilt when the accent changes.
/// </summary>
public static class HeatBrushes
{
    private static IBrush[] _levels = Build(AccentPresets.Brand.Color);

    /// <summary>Incremented on every rebuild – rows re-read their heat brushes when it changes.</summary>
    public static int Version { get; private set; }

    public static void Rebuild(Rgb accent)
    {
        _levels = Build(accent);
        Version++;
    }

    /// <summary>0 for values that are not worth a tint; 1–10 for 10 % steps.</summary>
    public static int Level(double percent) => percent < 0.5 ? 0 : Math.Clamp((int)Math.Ceiling(percent / 10), 1, 10);

    public static IBrush For(int level) => _levels[Math.Clamp(level, 0, 10)];

    private static IBrush[] Build(Rgb accent)
    {
        var result = new IBrush[11];
        result[0] = Brushes.Transparent;
        for (var i = 1; i <= 10; i++)
        {
            var alpha = (byte)Math.Round(255 * (0.06 + i * 0.045));
            result[i] = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(alpha, accent.R, accent.G, accent.B));
        }

        return result;
    }
}
