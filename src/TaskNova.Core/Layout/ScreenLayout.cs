namespace TaskNova.Core.Layout;

/// <summary>UI size presets and the automatic choice by screen size.</summary>
public static class UiScale
{
    public static IReadOnlyList<string> Presets { get; } = ["auto", "xs", "s", "m", "l", "xl"];

    public static double Factor(string preset) => preset switch
    {
        "xs" => 0.85,
        "s" => 0.92,
        "l" => 1.12,
        "xl" => 1.25,
        _ => 1.0
    };

    /// <summary>
    /// "auto": picks a preset from the logical working area (pixels ÷ Windows scaling) of the window's monitor.
    /// Small laptops (≤ 768 logical lines) get XS, 1366 × 768-class screens S, Full HD M, large 1440p+/4K screens L/XL.
    /// </summary>
    public static double Resolve(string preset, double logicalWidth, double logicalHeight)
    {
        if (preset != "auto")
        {
            return Factor(preset);
        }

        if (logicalHeight <= 0)
        {
            return 1.0;
        }

        return logicalHeight switch
        {
            < 700 => Factor("xs"),
            < 860 => Factor("s"),
            < 1300 => Factor("m"),
            < 1700 => logicalWidth >= 2400 ? Factor("l") : Factor("m"),
            _ => Factor("xl")
        };
    }
}

/// <summary>Where a small window (mini monitor) goes on a screen: 3 × 3 anchor points.</summary>
public static class Placement
{
    public static IReadOnlyList<string> Anchors { get; } = ["tl", "tc", "tr", "ml", "mc", "mr", "bl", "bc", "br"];

    /// <summary>Top-left corner for a window of <paramref name="width"/> × <paramref name="height"/> (same units as the area).</summary>
    public static (int X, int Y) Compute(string anchor, int areaX, int areaY, int areaWidth, int areaHeight, int width, int height, int margin)
    {
        var column = anchor.Length == 2 ? anchor[1] : 'r';
        var row = anchor.Length == 2 ? anchor[0] : 'b';
        var x = column switch
        {
            'l' => areaX + margin,
            'c' => areaX + (areaWidth - width) / 2,
            _ => areaX + areaWidth - width - margin
        };
        var y = row switch
        {
            't' => areaY + margin,
            'm' => areaY + (areaHeight - height) / 2,
            _ => areaY + areaHeight - height - margin
        };
        return (Math.Max(areaX, x), Math.Max(areaY, y));
    }
}
