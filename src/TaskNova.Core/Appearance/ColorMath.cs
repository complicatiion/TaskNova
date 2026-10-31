using System.Globalization;

namespace TaskNova.Core.Appearance;

/// <summary>8-bit sRGB color.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(string hex)
    {
        if (!TryParse(hex, out var color))
        {
            throw new FormatException($"Invalid color '{hex}'.");
        }

        return color;
    }

    /// <summary>Accepts #RRGGBB, RRGGBB and #AARRGGBB (alpha ignored).</summary>
    public static bool TryParse(string? text, out Rgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 8)
        {
            hex = hex[2..];
        }

        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        color = new Rgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    /// <summary>From a Win32 COLORREF (0x00BBGGRR).</summary>
    public static Rgb FromColorRef(uint colorRef) => new((byte)colorRef, (byte)(colorRef >> 8), (byte)(colorRef >> 16));

    public uint ToColorRef() => R | ((uint)G << 8) | ((uint)B << 16);

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public override string ToString() => ToHex();
}

/// <summary>
/// Color math for the accent system: WCAG contrast, OKLab/OKLCH lightness changes (perceptually even, keeps the hue),
/// mixing and automatic contrast correction.
/// </summary>
public static class ColorMath
{
    public static double Linear(byte channel)
    {
        var c = channel / 255d;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static byte Encode(double linear)
    {
        linear = Math.Clamp(linear, 0, 1);
        var c = linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
        return (byte)Math.Round(Math.Clamp(c, 0, 1) * 255);
    }

    /// <summary>WCAG 2.x relative luminance.</summary>
    public static double Luminance(Rgb c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    /// <summary>WCAG contrast ratio 1–21.</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Linear interpolation in sRGB: t = 0 → a, t = 1 → b.</summary>
    public static Rgb Mix(Rgb a, Rgb b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    public readonly record struct Oklch(double L, double C, double H);

    public static Oklch ToOklch(Rgb color)
    {
        var (l, a, b) = ToOklab(Linear(color.R), Linear(color.G), Linear(color.B));
        var c = Math.Sqrt(a * a + b * b);
        var h = Math.Atan2(b, a) * 180 / Math.PI;
        if (h < 0)
        {
            h += 360;
        }

        return new Oklch(l, c, h);
    }

    /// <summary>Converts back to sRGB; out-of-gamut colors keep lightness and hue and lose chroma.</summary>
    public static Rgb FromOklch(Oklch color)
    {
        var l = Math.Clamp(color.L, 0, 1);
        var c = Math.Max(0, color.C);
        var hr = color.H * Math.PI / 180;
        for (var i = 0; i < 24; i++)
        {
            var (r, g, b) = FromOklab(l, c * Math.Cos(hr), c * Math.Sin(hr));
            if (InGamut(r) && InGamut(g) && InGamut(b))
            {
                return new Rgb(Encode(r), Encode(g), Encode(b));
            }

            c *= 0.85;
        }

        var (r0, g0, b0) = FromOklab(l, 0, 0);
        return new Rgb(Encode(r0), Encode(g0), Encode(b0));
    }

    private static bool InGamut(double v) => v is >= -0.0005 and <= 1.0005;

    /// <summary>Shifts the OKLCH lightness by <paramref name="delta"/> (-1 … 1).</summary>
    public static Rgb ShiftLightness(Rgb color, double delta)
    {
        var lch = ToOklch(color);
        return FromOklch(lch with { L = Math.Clamp(lch.L + delta, 0, 1) });
    }

    /// <summary>Rotates the hue (degrees) and keeps lightness and chroma.</summary>
    public static Rgb RotateHue(Rgb color, double degrees)
    {
        var lch = ToOklch(color);
        return FromOklch(lch with { H = (lch.H + degrees + 360) % 360 });
    }

    /// <summary>
    /// Lightens (on dark backgrounds) or darkens (on light backgrounds) a color in small OKLCH steps until it reaches
    /// <paramref name="target"/> contrast against every given background. The hue stays the same.
    /// </summary>
    public static Rgb EnsureContrast(Rgb color, double target, params Rgb[] backgrounds)
    {
        if (backgrounds.Length == 0 || backgrounds.All(b => Contrast(color, b) >= target))
        {
            return color;
        }

        var darkBackground = backgrounds.Average(Luminance) < 0.18;
        var lch = ToOklch(color);
        var step = darkBackground ? 0.01 : -0.01;
        var current = color;
        for (var i = 0; i < 100; i++)
        {
            lch = lch with { L = Math.Clamp(lch.L + step, 0, 1) };
            current = FromOklch(lch);
            if (backgrounds.All(b => Contrast(current, b) >= target) || lch.L is <= 0 or >= 1)
            {
                break;
            }
        }

        return current;
    }

    /// <summary>White or the darkest brand color – whichever reads better on <paramref name="fill"/>.</summary>
    public static Rgb ReadableOn(Rgb fill, Rgb dark, Rgb light) => Contrast(fill, light) >= Contrast(fill, dark) ? light : dark;

    private static (double L, double A, double B) ToOklab(double r, double g, double b)
    {
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (
            0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    private static (double R, double G, double B) FromOklab(double l, double a, double b)
    {
        var l1 = l + 0.3963377774 * a + 0.2158037573 * b;
        var m1 = l - 0.1055613458 * a - 0.0638541728 * b;
        var s1 = l - 0.0894841775 * a - 1.2914855480 * b;
        l1 = l1 * l1 * l1;
        m1 = m1 * m1 * m1;
        s1 = s1 * s1 * s1;
        return (
            4.0767416621 * l1 - 3.3077115913 * m1 + 0.2309699292 * s1,
            -1.2684380046 * l1 + 2.6097574011 * m1 - 0.3413193965 * s1,
            -0.0041960863 * l1 - 0.7034186147 * m1 + 1.7076147010 * s1);
    }
}
