namespace TaskNova.Core.Metrics;

/// <summary>Percentages from cumulative counters (all times in 100 ns units).</summary>
public static class CpuMath
{
    /// <summary>
    /// Total CPU usage from two GetSystemTimes readings. Kernel time includes idle time.
    /// </summary>
    public static (double Total, double Kernel) SystemUsage(long idleDelta, long kernelDelta, long userDelta)
    {
        var busyTotal = kernelDelta + userDelta;
        if (busyTotal <= 0)
        {
            return (0, 0);
        }

        var total = Clamp((busyTotal - idleDelta) * 100d / busyTotal);
        var kernel = Clamp((kernelDelta - idleDelta) * 100d / busyTotal);
        return (total, kernel);
    }

    /// <summary>Share of one process of all logical processors over the elapsed wall time.</summary>
    public static double ProcessUsage(long cpuTimeDelta100Ns, double elapsedSeconds, int logicalProcessors)
    {
        if (cpuTimeDelta100Ns <= 0 || elapsedSeconds <= 0 || logicalProcessors <= 0)
        {
            return 0;
        }

        var capacity = elapsedSeconds * 10_000_000d * logicalProcessors;
        return Clamp(cpuTimeDelta100Ns * 100d / capacity);
    }

    /// <summary>Usage of one logical processor from SystemProcessorPerformanceInformation deltas.</summary>
    public static double CoreUsage(long idleDelta, long kernelDelta, long userDelta) => SystemUsage(idleDelta, kernelDelta, userDelta).Total;

    /// <summary>Bytes per second from a cumulative counter; a counter reset (smaller value) gives 0.</summary>
    public static double Rate(ulong previous, ulong current, double elapsedSeconds) =>
        current < previous || elapsedSeconds <= 0 ? 0 : (current - previous) / elapsedSeconds;

    public static double Clamp(double percent) => double.IsNaN(percent) ? 0 : Math.Clamp(percent, 0, 100);
}
