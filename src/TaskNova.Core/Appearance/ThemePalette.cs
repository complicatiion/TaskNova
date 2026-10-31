namespace TaskNova.Core.Appearance;

/// <summary>Effective theme after resolving "system".</summary>
public enum ThemeKind
{
    Dark,
    Shadow,
    Light
}

/// <summary>
/// Neutral colors of one theme. Built from the TaskNova palette #0B1026 · #2B2D42 · #EF476F · #FFD6E0 · #F8F9FF.
/// Text tokens reach at least 4.5:1 against card and raised surfaces (checked by unit tests).
/// </summary>
public sealed record ThemePalette(
    ThemeKind Kind,
    Rgb BgApp,
    Rgb BgSidebar,
    Rgb SurfaceCard,
    Rgb SurfaceRaised,
    Rgb SurfaceHover,
    Rgb Border,
    Rgb TextPrimary,
    Rgb TextSecondary,
    Rgb TextDisabled,
    Rgb Safe,
    Rgb Moderate,
    Rgb Deep,
    Rgb Info,
    Rgb Danger)
{
    public bool IsDark => Kind != ThemeKind.Light;

    /// <summary>Navy: Fluent dark templates, palette #0B1026 / #2B2D42.</summary>
    public static ThemePalette Dark { get; } = new(
        ThemeKind.Dark,
        BgApp: Rgb.FromHex("#0B1026"),
        BgSidebar: Rgb.FromHex("#0F1530"),
        SurfaceCard: Rgb.FromHex("#141A36"),
        SurfaceRaised: Rgb.FromHex("#1C2242"),
        SurfaceHover: Rgb.FromHex("#252B4A"),
        Border: Rgb.FromHex("#2B2D42"),
        TextPrimary: Rgb.FromHex("#F8F9FF"),
        TextSecondary: Rgb.FromHex("#A9AEC8"),
        TextDisabled: Rgb.FromHex("#6E7391"),
        Safe: Rgb.FromHex("#5BD69A"),
        Moderate: Rgb.FromHex("#F5B740"),
        Deep: Rgb.FromHex("#FF8A5B"),
        Info: Rgb.FromHex("#6AB4F5"),
        Danger: Rgb.FromHex("#C62828"));

    /// <summary>"Shadow": neutral dark greys without any blue tint (background #202020). Runs on the Fluent dark templates.</summary>
    public static ThemePalette Shadow { get; } = new(
        ThemeKind.Shadow,
        BgApp: Rgb.FromHex("#202020"),
        BgSidebar: Rgb.FromHex("#1B1B1B"),
        SurfaceCard: Rgb.FromHex("#2A2A2A"),
        SurfaceRaised: Rgb.FromHex("#323232"),
        SurfaceHover: Rgb.FromHex("#3A3A3A"),
        Border: Rgb.FromHex("#3D3D3D"),
        TextPrimary: Rgb.FromHex("#F5F5F5"),
        TextSecondary: Rgb.FromHex("#B4B4B4"),
        TextDisabled: Rgb.FromHex("#7A7A7A"),
        Safe: Rgb.FromHex("#5BD69A"),
        Moderate: Rgb.FromHex("#F5B740"),
        Deep: Rgb.FromHex("#FF8A5B"),
        Info: Rgb.FromHex("#6AB4F5"),
        Danger: Rgb.FromHex("#C62828"));

    /// <summary>Soft light: muted greys instead of pure white (less glare), dark navy text.</summary>
    public static ThemePalette Light { get; } = new(
        ThemeKind.Light,
        BgApp: Rgb.FromHex("#E8EAEF"),
        BgSidebar: Rgb.FromHex("#E1E4EA"),
        SurfaceCard: Rgb.FromHex("#F2F3F6"),
        SurfaceRaised: Rgb.FromHex("#E6E8EE"),
        SurfaceHover: Rgb.FromHex("#DCDFE7"),
        Border: Rgb.FromHex("#CDD1DA"),
        TextPrimary: Rgb.FromHex("#0B1026"),
        TextSecondary: Rgb.FromHex("#454B66"),
        TextDisabled: Rgb.FromHex("#868BA3"),
        Safe: Rgb.FromHex("#1A6E44"),
        Moderate: Rgb.FromHex("#8F5206"),
        Deep: Rgb.FromHex("#A63A0B"),
        Info: Rgb.FromHex("#1B528A"),
        Danger: Rgb.FromHex("#B91C1C"));

    public static ThemePalette For(ThemeKind kind) => kind switch
    {
        ThemeKind.Light => Light,
        ThemeKind.Shadow => Shadow,
        _ => Dark
    };

    /// <summary>Soft background of a status color (badges, notices).</summary>
    public Rgb Soft(Rgb color) => ColorMath.Mix(SurfaceCard, color, IsDark ? 0.18 : 0.12);

    /// <summary>Grid lines in the charts.</summary>
    public Rgb ChartGrid => ColorMath.Mix(SurfaceCard, TextPrimary, IsDark ? 0.08 : 0.07);
}
