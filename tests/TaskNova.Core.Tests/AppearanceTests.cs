using TaskNova.Core.Appearance;
using Xunit;

namespace TaskNova.Core.Tests;

public class AppearanceTests
{
    [Fact]
    public void Contrast_MatchesWcagReference()
    {
        Assert.InRange(ColorMath.Contrast(Rgb.FromHex("#000000"), Rgb.FromHex("#FFFFFF")), 20.99, 21.01);
        Assert.InRange(ColorMath.Contrast(Rgb.FromHex("#EF476F"), Rgb.FromHex("#0B1026")), 5.1, 5.3);
        Assert.InRange(ColorMath.Contrast(Rgb.FromHex("#EF476F"), Rgb.FromHex("#F8F9FF")), 3.4, 3.5);
    }

    [Fact]
    public void Oklch_RoundTripKeepsColor()
    {
        foreach (var preset in AccentPresets.All)
        {
            var back = ColorMath.FromOklch(ColorMath.ToOklch(preset.Color));
            Assert.InRange(Math.Abs(back.R - preset.Color.R), 0, 1);
            Assert.InRange(Math.Abs(back.G - preset.Color.G), 0, 1);
            Assert.InRange(Math.Abs(back.B - preset.Color.B), 0, 1);
        }
    }

    [Fact]
    public void Parse_AcceptsCommonFormats()
    {
        Assert.True(Rgb.TryParse("#EF476F", out var a));
        Assert.True(Rgb.TryParse("EF476F", out var b));
        Assert.True(Rgb.TryParse("#FFEF476F", out var c));
        Assert.Equal(a, b);
        Assert.Equal(a, c);
        Assert.False(Rgb.TryParse("#12", out _));
        Assert.Equal(0x6F47EFu, a.ToColorRef());
        Assert.Equal(a, Rgb.FromColorRef(a.ToColorRef()));
    }

    [Fact]
    public void ThemeTexts_ReachAaOnCardsAndRaisedSurfaces()
    {
        foreach (var theme in new[] { ThemePalette.Dark, ThemePalette.Shadow, ThemePalette.Light })
        {
            foreach (var surface in new[] { theme.SurfaceCard, theme.SurfaceRaised, theme.BgApp, theme.BgSidebar })
            {
                Assert.True(ColorMath.Contrast(theme.TextPrimary, surface) >= 4.5, $"{theme.Kind} primary");
                Assert.True(ColorMath.Contrast(theme.TextSecondary, surface) >= 4.5, $"{theme.Kind} secondary on {surface}");
            }

            foreach (var status in new[] { theme.Safe, theme.Moderate, theme.Info })
            {
                Assert.True(ColorMath.Contrast(status, theme.SurfaceCard) >= 4.5, $"{theme.Kind} status {status}");
            }
        }
    }

    [Fact]
    public void AccentText_IsReadableForEveryPresetAndTheme()
    {
        var colors = AccentPresets.All.Select(p => p.Color).Concat([Rgb.FromHex("#FFFF00"), Rgb.FromHex("#000080"), Rgb.FromHex("#FFFFFF")]);
        foreach (var color in colors)
        {
            foreach (var theme in new[] { ThemePalette.Dark, ThemePalette.Shadow, ThemePalette.Light })
            {
                var accent = AccentPalette.Compute(color, theme);
                Assert.True(ColorMath.Contrast(accent.Text, theme.SurfaceCard) >= 4.5, $"{color} text on {theme.Kind}");
                Assert.True(ColorMath.Contrast(accent.Text, theme.SurfaceRaised) >= 4.5, $"{color} text raised on {theme.Kind}");
                Assert.True(ColorMath.Contrast(accent.OnFill, accent.Fill) >= 3.0, $"{color} on-fill on {theme.Kind}");
            }
        }
    }

    [Fact]
    public void BrandAccent_FillStaysUnchangedOnDark()
    {
        var accent = AccentPalette.Compute(AccentPresets.Brand.Color, ThemePalette.Dark);
        Assert.Equal(Rgb.FromHex("#EF476F"), accent.Fill);

        // Text is lifted just enough for 4.5:1 on the raised surface – same hue, barely lighter.
        var shift = ColorMath.ToOklch(accent.Text).L - ColorMath.ToOklch(accent.Fill).L;
        Assert.InRange(shift, 0, 0.06);
    }

    [Fact]
    public void Presets_AreBrandPlusTen()
    {
        Assert.Equal(11, AccentPresets.All.Count);
        Assert.Equal("brand", AccentPresets.All[0].Id);
        Assert.Equal(11, AccentPresets.All.Select(p => p.Id).Distinct().Count());
    }
}
