using System.Globalization;

namespace TaskNova.Core.Formatting;

/// <summary>Display formats for sizes, rates, percentages and durations (current culture).</summary>
public static class Fmt
{
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB", "PB"];
    private static readonly string[] BitUnits = ["bit/s", "Kbit/s", "Mbit/s", "Gbit/s", "Tbit/s"];

    public static CultureInfo Culture { get; set; } = CultureInfo.CurrentCulture;

    /// <summary>1.5 GB, 820 MB, 12 KB (binary units, labelled like Windows).</summary>
    public static string Bytes(double bytes)
    {
        if (bytes < 0 || double.IsNaN(bytes))
        {
            bytes = 0;
        }

        var unit = 0;
        while (bytes >= 1024 && unit < ByteUnits.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        var format = unit == 0 ? "0" : bytes >= 100 ? "0" : bytes >= 10 ? "0.0" : "0.0#";
        return bytes.ToString(format, Culture) + " " + ByteUnits[unit];
    }

    /// <summary>Memory column like the Task Manager: always MB below 1 GB.</summary>
    public static string MemoryMb(double bytes)
    {
        var mb = bytes / (1024d * 1024d);
        return mb >= 1024 ? (mb / 1024).ToString("0.0", Culture) + " GB" : mb.ToString("0.0", Culture) + " MB";
    }

    public static string BytesPerSecond(double bytesPerSecond) => bytesPerSecond < 0.5 ? "0 B/s" : Bytes(bytesPerSecond) + "/s";

    /// <summary>Network throughput in bits per second (decimal units, like adapters are rated).</summary>
    public static string BitsPerSecond(double bytesPerSecond)
    {
        var bits = Math.Max(0, bytesPerSecond * 8);
        var unit = 0;
        while (bits >= 1000 && unit < BitUnits.Length - 1)
        {
            bits /= 1000;
            unit++;
        }

        return bits.ToString(unit == 0 || bits >= 100 ? "0" : "0.0", Culture) + " " + BitUnits[unit];
    }

    public static string LinkSpeed(long bitsPerSecond) =>
        bitsPerSecond <= 0 ? "–" : BitsPerSecond(bitsPerSecond / 8d);

    public static string Percent(double value, int decimals = 0) =>
        Math.Clamp(value, 0, 100).ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), Culture) + " %";

    /// <summary>"0 %" stays visible but values below 0.05 show as "0 %" (like the Task Manager).</summary>
    public static string CpuPercent(double value) => value < 0.05 ? "0 %" : Percent(value, 1);

    public static string Mhz(double mhz) => mhz <= 0 ? "–" : mhz >= 1000 ? (mhz / 1000).ToString("0.00", Culture) + " GHz" : mhz.ToString("0", Culture) + " MHz";

    /// <summary>Uptime: "3:04:15:09" (days:hours:minutes:seconds).</summary>
    public static string Uptime(TimeSpan span) =>
        $"{(int)span.TotalDays}:{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";

    /// <summary>CPU time: "0:12:34" or "1.02:03:04" for long runs.</summary>
    public static string CpuTime(long time100Ns)
    {
        var span = TimeSpan.FromTicks(Math.Max(0, time100Ns));
        return span.TotalDays >= 1
            ? $"{(int)span.TotalDays}.{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
    }

    /// <summary>Idle time of a session: "3 min", "2 h 5 min", "–".</summary>
    public static string Idle(TimeSpan? span)
    {
        if (span is not { } value || value < TimeSpan.Zero)
        {
            return "–";
        }

        if (value.TotalMinutes < 1)
        {
            return "< 1 min";
        }

        return value.TotalHours < 1
            ? $"{(int)value.TotalMinutes} min"
            : value.TotalDays < 1 ? $"{(int)value.TotalHours} h {value.Minutes} min" : $"{(int)value.TotalDays} d {value.Hours} h";
    }

    public static string Number(long value) => value.ToString("N0", Culture);

    public static string Date(DateTime? utc) => utc is { } value ? value.ToLocalTime().ToString("g", Culture) : "–";
}
