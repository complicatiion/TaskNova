using System.Globalization;
using System.Text;
using TaskNova.Core.Classification;
using TaskNova.Core.Formatting;
using TaskNova.Core.History;
using TaskNova.Core.Metrics;
using TaskNova.Core.Models;
using TaskNova.Core.Search;
using TaskNova.Core.Shell;
using TaskNova.Core.Storage;
using Xunit;

namespace TaskNova.Core.Tests;

public class MetricsTests
{
    [Fact]
    public void RingBuffer_KeepsNewestValuesInOrder()
    {
        var buffer = new RingBuffer(3);
        foreach (var v in new[] { 1d, 2, 3, 4, 5 })
        {
            buffer.Add(v);
        }

        Assert.Equal(3, buffer.Count);
        Assert.Equal(3d, buffer[0]);
        Assert.Equal(5d, buffer[2]);
        Assert.Equal(5d, buffer.Last);
        Assert.Equal(5d, buffer.Max());
        Assert.Equal(5L, buffer.Version);
    }

    [Fact]
    public void RingBuffer_ResizeKeepsNewest()
    {
        var buffer = new RingBuffer(5);
        for (var i = 1; i <= 5; i++)
        {
            buffer.Add(i);
        }

        buffer.Resize(2);
        Assert.Equal(2, buffer.Count);
        Assert.Equal(4d, buffer[0]);
        Assert.Equal(5d, buffer[1]);
        buffer.Resize(4);
        buffer.Add(6);
        Assert.Equal(3, buffer.Count);
        Assert.Equal(6d, buffer.Last);
    }

    [Fact]
    public void RingBuffer_IgnoresNaN()
    {
        var buffer = new RingBuffer(2);
        buffer.Add(double.NaN);
        Assert.Equal(0d, buffer.Last);
    }

    [Fact]
    public void SystemUsage_KernelIncludesIdle()
    {
        // 4 s of capacity: 3 s idle, kernel time 2 s (incl. idle 3? no – kernel 2.5 incl. idle 1.5), user 1.5
        var (total, kernel) = CpuMath.SystemUsage(idleDelta: 15, kernelDelta: 25, userDelta: 15);
        Assert.InRange(total, 62.4, 62.6);
        Assert.InRange(kernel, 24.9, 25.1);
        Assert.Equal((0d, 0d), CpuMath.SystemUsage(0, 0, 0));
    }

    [Fact]
    public void ProcessUsage_IsShareOfAllProcessors()
    {
        // 0.5 s CPU in 1 s on 4 processors = 12.5 %
        Assert.InRange(CpuMath.ProcessUsage(5_000_000, 1, 4), 12.49, 12.51);
        Assert.Equal(100d, CpuMath.ProcessUsage(80_000_000, 1, 4));
        Assert.Equal(0d, CpuMath.ProcessUsage(100, 0, 4));
    }

    [Fact]
    public void Rate_HandlesCounterReset()
    {
        Assert.Equal(512d, CpuMath.Rate(1000, 2024, 2));
        Assert.Equal(0d, CpuMath.Rate(2000, 10, 1));
    }
}

public class FormattingTests
{
    public FormattingTests()
    {
        Fmt.Culture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void Bytes_UsesBinaryUnits()
    {
        Assert.Equal("0 B", Fmt.Bytes(0));
        Assert.Equal("1.0 KB", Fmt.Bytes(1024));
        Assert.Equal("1.5 GB", Fmt.Bytes(1.5 * 1024 * 1024 * 1024));
        Assert.Equal("120 MB", Fmt.Bytes(120 * 1024 * 1024));
    }

    [Fact]
    public void Memory_IsMbBelowOneGb()
    {
        Assert.Equal("512.0 MB", Fmt.MemoryMb(512d * 1024 * 1024));
        Assert.Equal("2.0 GB", Fmt.MemoryMb(2048d * 1024 * 1024));
    }

    [Fact]
    public void Bits_UseDecimalUnits()
    {
        Assert.Equal("8.0 Kbit/s", Fmt.BitsPerSecond(1000));
        Assert.Equal("1.0 Gbit/s", Fmt.LinkSpeed(1_000_000_000));
    }

    [Fact]
    public void Times_AreFormatted()
    {
        Assert.Equal("1:02:03:04", Fmt.Uptime(new TimeSpan(1, 2, 3, 4)));
        Assert.Equal("0:01:05", Fmt.CpuTime(TimeSpan.FromSeconds(65).Ticks));
        Assert.Equal("2 h 5 min", Fmt.Idle(TimeSpan.FromMinutes(125)));
        Assert.Equal("–", Fmt.Idle(null));
        Assert.Equal("0 %", Fmt.CpuPercent(0.01));
        Assert.Equal("12.5 %", Fmt.CpuPercent(12.5));
    }
}

public class SearchTests
{
    [Fact]
    public void AllWordsMustMatch()
    {
        var matcher = new SearchMatcher("chrome google");
        Assert.True(matcher.Matches(10, 1, "chrome.exe", "Google Chrome"));
        Assert.False(matcher.Matches(10, 1, "chrome.exe", null));
    }

    [Fact]
    public void NumbersMatchPid_AndFiltersArePrecise()
    {
        Assert.True(new SearchMatcher("1234").Matches(1234, 1, "x.exe"));
        Assert.False(new SearchMatcher("pid:5").Matches(1234, 1, "x.exe"));
        Assert.True(new SearchMatcher("pid:1234").Matches(1234, 1, "x.exe"));
        Assert.True(new SearchMatcher("session:2 svchost").Matches(9, 2, "svchost.exe"));
        Assert.False(new SearchMatcher("session:2").Matches(9, 3, "svchost.exe"));
        Assert.True(new SearchMatcher("  ").IsEmpty);
    }
}

public class ClassifierTests
{
    private const string Windows = @"C:\Windows";

    [Fact]
    public void Groups()
    {
        Assert.Equal(ProcessGroup.App, ProcessClassifier.Classify("notepad.exe", @"C:\Windows\System32\notepad.exe", true, Windows));
        Assert.Equal(ProcessGroup.Windows, ProcessClassifier.Classify("svchost.exe", @"C:\Windows\System32\svchost.exe", false, Windows));
        Assert.Equal(ProcessGroup.Windows, ProcessClassifier.Classify("System", null, false, Windows));
        Assert.Equal(ProcessGroup.Background, ProcessClassifier.Classify("agent.exe", @"C:\Program Files\X\agent.exe", false, Windows));
        Assert.Equal(ProcessGroup.Windows, ProcessClassifier.Classify("csrss.exe", null, true, Windows));
    }

    [Fact]
    public void SecurityHints()
    {
        var flags = ProcessClassifier.Inspect("svchost.exe", @"C:\Users\a\AppData\Local\Temp\svchost.exe", true, Windows, null, @"C:\Users\a");
        Assert.True(flags.HasFlag(SecurityFlags.NameMismatch));
        Assert.True(flags.HasFlag(SecurityFlags.UserWritableLocation));
        Assert.Equal(SecurityFlags.None, ProcessClassifier.Inspect("svchost.exe", @"C:\Windows\System32\svchost.exe", true, Windows, null, @"C:\Users\a"));
        Assert.Equal(SecurityFlags.FileMissing, ProcessClassifier.Inspect("a.exe", @"D:\a.exe", false, Windows, null, null));
        Assert.False(ProcessClassifier.IsUnder(@"C:\WindowsApps\x.exe", Windows));
    }
}

public class LnkTests
{
    /// <summary>Minimal shortcut: header + LinkInfo (local base path) + unicode arguments.</summary>
    private static byte[] BuildLnk(string target, string arguments)
    {
        var data = new List<byte>();
        var header = new byte[0x4C];
        BitConverter.GetBytes(0x4Cu).CopyTo(header, 0);
        BitConverter.GetBytes(0x02u | 0x20u | 0x80u).CopyTo(header, 0x14); // HasLinkInfo | HasArguments | IsUnicode
        data.AddRange(header);

        var path = Encoding.Latin1.GetBytes(target + "\0");
        const int infoHeader = 0x1C;
        var size = infoHeader + path.Length + 1;
        var info = new byte[size];
        BitConverter.GetBytes((uint)size).CopyTo(info, 0);
        BitConverter.GetBytes((uint)infoHeader).CopyTo(info, 4);
        BitConverter.GetBytes(1u).CopyTo(info, 8); // VolumeIDAndLocalBasePath
        BitConverter.GetBytes((uint)infoHeader).CopyTo(info, 0x10); // LocalBasePathOffset
        BitConverter.GetBytes((uint)(infoHeader + path.Length)).CopyTo(info, 0x18); // CommonPathSuffixOffset (empty)
        path.CopyTo(info, infoHeader);
        data.AddRange(info);

        data.AddRange(BitConverter.GetBytes((ushort)arguments.Length));
        data.AddRange(Encoding.Unicode.GetBytes(arguments));
        data.AddRange(BitConverter.GetBytes(0u)); // terminal block
        return data.ToArray();
    }

    [Fact]
    public void ReadsTargetAndArguments()
    {
        var result = LnkParser.Parse(BuildLnk(@"C:\Tools\app.exe", "-min"));
        Assert.NotNull(result);
        Assert.Equal(@"C:\Tools\app.exe", result!.Path);
        Assert.Equal("-min", result.Arguments);
    }

    [Fact]
    public void RejectsGarbage()
    {
        Assert.Null(LnkParser.Parse(new byte[10]));
        Assert.Null(LnkParser.Parse(new byte[0x4C]));
    }
}

public class StorageTests
{
    [Fact]
    public void Settings_RoundTripAndNormalize()
    {
        var file = Path.Combine(Path.GetTempPath(), $"tasknova-test-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings { Theme = "shadow", AccentId = "custom", CustomAccent = "#123456", IntervalMs = 2000 };
            Assert.True(SettingsStore.Save(settings, file));
            var loaded = SettingsStore.Load(file);
            Assert.Equal("shadow", loaded.Theme);
            Assert.Equal("#123456", loaded.CustomAccent);
            Assert.Equal(2000, loaded.IntervalMs);

            File.WriteAllText(file, """{ "theme": "pink", "intervalMs": 7, "customAccent": "nope" }""");
            var fixedUp = SettingsStore.Load(file);
            Assert.Equal("dark", fixedUp.Theme);
            Assert.Equal(1000, fixedUp.IntervalMs);
            Assert.Equal("#EF476F", fixedUp.CustomAccent);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Defaults_MatchTheConcept()
    {
        var s = new AppSettings();
        Assert.Equal("dark", s.Theme);
        Assert.False(s.UseProcessorUtility);
        Assert.False(s.WindowMaximized);
        Assert.False(s.ResolveHostNames);
        Assert.Equal("brand", s.AccentId);
        Assert.Equal("auto", s.UiScale);
        Assert.Equal("overview", s.StartPage);
        Assert.Equal("window", s.StartMode);
        Assert.Equal("br", s.MiniPosition);
        Assert.Equal("default", s.MiniTheme);
        Assert.False(s.TrimMemoryInTray);
        Assert.Equal(1180, s.WindowWidth);
        Assert.Equal(760, s.WindowHeight);
        Assert.False(s.ExpertMode);
        Assert.False(s.AlertsEnabled);
    }

    [Fact]
    public void Settings_CopyFromCopiesEveryProperty()
    {
        var source = new AppSettings { Theme = "light", ExpertMode = true, AlertsEnabled = true, AlertCpuPercent = 77, WindowX = 5, MiniOpacity = 55 };
        source.CollapsedGroups.Add("apps");
        var target = new AppSettings();
        target.CopyFrom(source);
        foreach (var property in typeof(AppSettings).GetProperties())
        {
            var a = property.GetValue(source);
            var b = property.GetValue(target);
            if (a is System.Collections.IEnumerable listA && a is not string)
            {
                Assert.Equal(listA.Cast<object>(), ((System.Collections.IEnumerable)b!).Cast<object>());
                Assert.NotSame(a, b);
            }
            else
            {
                Assert.Equal(a, b);
            }
        }
    }

    [Fact]
    public void Settings_NewValuesAreNormalized()
    {
        var file = Path.Combine(Path.GetTempPath(), $"tasknova-test-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(file, """{ "uiScale": "huge", "startMode": "nope", "startPage": "x", "miniPosition": "zz", "miniTheme": "neon", "miniOpacity": 5, "renderMode": "dx" }""");
            var s = SettingsStore.Load(file);
            Assert.Equal("auto", s.UiScale);
            Assert.Equal("window", s.StartMode);
            Assert.Equal("overview", s.StartPage);
            Assert.Equal("br", s.MiniPosition);
            Assert.Equal("default", s.MiniTheme);
            Assert.Equal(40, s.MiniOpacity);
            Assert.Equal("gpu", s.RenderMode);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void AppHistory_AccumulatesAndPersists()
    {
        var file = Path.Combine(Path.GetTempPath(), $"tasknova-history-{Guid.NewGuid():N}.json");
        try
        {
            var store = new AppHistoryStore(file);
            var now = DateTime.UtcNow;
            store.Add("app.exe", 10_000_000, 100, 1, now);
            store.Add("APP.EXE", 5_000_000, 50, 1, now);
            store.Add("other.exe", 0, 0, 0, now);
            store.Describe("app.exe", @"C:\app.exe", "App");
            store.Save();

            var reloaded = new AppHistoryStore(file);
            reloaded.Load();
            var entry = Assert.Single(reloaded.Snapshot());
            Assert.Equal(15_000_000, entry.CpuTime100Ns);
            Assert.Equal(150UL, entry.IoBytes);
            Assert.Equal("App", entry.Description);
        }
        finally
        {
            File.Delete(file);
        }
    }
}

public class LayoutTests
{
    [Fact]
    public void UiScale_PresetsAndAuto()
    {
        Assert.Equal(0.85, TaskNova.Core.Layout.UiScale.Resolve("xs", 1920, 1040));
        Assert.Equal(1.0, TaskNova.Core.Layout.UiScale.Resolve("auto", 1920, 1040));
        Assert.Equal(0.92, TaskNova.Core.Layout.UiScale.Resolve("auto", 1366, 728));
        Assert.Equal(1.25, TaskNova.Core.Layout.UiScale.Resolve("auto", 3840, 2080));
    }

    [Fact]
    public void Placement_Anchors()
    {
        Assert.Equal((1920 - 300 - 12, 1040 - 200 - 12), TaskNova.Core.Layout.Placement.Compute("br", 0, 0, 1920, 1040, 300, 200, 12));
        Assert.Equal((12, 12), TaskNova.Core.Layout.Placement.Compute("tl", 0, 0, 1920, 1040, 300, 200, 12));
        Assert.Equal((1920 + 810, 420), TaskNova.Core.Layout.Placement.Compute("mc", 1920, 0, 1920, 1040, 300, 200, 12));
    }
}
