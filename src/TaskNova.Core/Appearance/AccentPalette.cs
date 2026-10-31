namespace TaskNova.Core.Appearance;

/// <summary>
/// All accent shades derived from one base color for one theme. Text and icons get at least 4.5:1 contrast on card and
/// raised surfaces; the text color on an accent fill is white or #0B1026, whichever reads better.
/// </summary>
public sealed record AccentPalette(
    Rgb Base,
    Rgb Fill,
    Rgb Hover,
    Rgb Pressed,
    Rgb Text,
    Rgb Soft,
    Rgb MenuHover,
    Rgb OnFill,
    Rgb Secondary,
    Rgb GradientEnd)
{
    public static readonly Rgb Ink = Rgb.FromHex("#0B1026");
    public static readonly Rgb Paper = Rgb.FromHex("#FFFFFF");

    public static AccentPalette Compute(Rgb accent, ThemePalette theme)
    {
        var dark = theme.IsDark;

        // Fill (buttons, switches, check marks): the chosen color; on light surfaces very light accents get darker so
        // the fill stays visible (≥ 1.6:1 against the card).
        var fill = dark ? accent : ColorMath.EnsureContrast(accent, 1.6, theme.SurfaceCard);
        var hover = ColorMath.ShiftLightness(fill, dark ? 0.05 : -0.05);
        var pressed = ColorMath.ShiftLightness(fill, dark ? -0.06 : -0.10);

        // Text and icons in accent color: readable on every surface they are used on.
        var text = ColorMath.EnsureContrast(accent, 4.5, theme.SurfaceCard, theme.SurfaceRaised, theme.BgApp);

        var soft = ColorMath.Mix(theme.SurfaceCard, fill, dark ? 0.20 : 0.12);
        var menuHover = ColorMath.Mix(theme.SurfaceCard, fill, dark ? 0.32 : 0.22);
        var onFill = ColorMath.ReadableOn(fill, Ink, Paper);

        // Second chart series (write vs. read, send vs. receive): same lightness, hue turned, still ≥ 3:1.
        var secondary = ColorMath.EnsureContrast(ColorMath.RotateHue(text, 150), 3.0, theme.SurfaceCard);

        // Brand gradient: accent towards a slightly shifted hue (logo glow, active marker, progress).
        var gradientEnd = ColorMath.RotateHue(fill, -35);

        return new AccentPalette(accent, fill, hover, pressed, text, soft, menuHover, onFill, secondary, gradientEnd);
    }
}

/// <summary>An accent the user can pick with one click.</summary>
public sealed record AccentPreset(string Id, string Hex, string NameKey)
{
    public Rgb Color => Rgb.FromHex(Hex);
}

public static class AccentPresets
{
    /// <summary>TaskNova brand color from the palette.</summary>
    public const string BrandId = "brand";

    public const string WindowsId = "windows";

    public const string CustomId = "custom";

    public static readonly AccentPreset Brand = new(BrandId, "#EF476F", "AccentBrand");

    /// <summary>The brand color first, then ten presets around the color wheel.</summary>
    public static IReadOnlyList<AccentPreset> All { get; } =
    [
        Brand,
        new("red", "#E53935", "AccentRed"),
        new("orange", "#FB8C00", "AccentOrange"),
        new("amber", "#FFB300", "AccentAmber"),
        new("green", "#43A047", "AccentGreen"),
        new("teal", "#00897B", "AccentTeal"),
        new("cyan", "#00ACC1", "AccentCyan"),
        new("blue", "#1E88E5", "AccentBlue"),
        new("indigo", "#5C6BC0", "AccentIndigo"),
        new("violet", "#8E24AA", "AccentViolet"),
        new("pink", "#D81B60", "AccentPink")
    ];

    public static AccentPreset? Find(string? id) => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
