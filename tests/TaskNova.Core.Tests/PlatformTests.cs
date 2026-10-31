using System.Runtime.CompilerServices;
using TaskNova.Platform.Windows;
using TaskNova.Platform.Windows.Native;
using TaskNova.Platform.Windows.Sampling;
using Xunit;

namespace TaskNova.Core.Tests;

/// <summary>Native structure sizes must match the 64-bit Windows SDK – a wrong size silently corrupts every sample.</summary>
public class NativeLayoutTests
{
    [Fact]
    public void StructSizesMatchWindowsSdk()
    {
        Assert.Equal(256, Unsafe.SizeOf<SYSTEM_PROCESS_INFORMATION>());
        Assert.Equal(80, Unsafe.SizeOf<SYSTEM_THREAD_INFORMATION>());
        Assert.Equal(48, Unsafe.SizeOf<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>());
        Assert.Equal(64, Unsafe.SizeOf<MEMORYSTATUSEX>());
        Assert.Equal(104, Unsafe.SizeOf<PERFORMANCE_INFORMATION>());
        Assert.Equal(16, Unsafe.SizeOf<TOKEN_PRIVILEGES>());
        Assert.Equal(56, Unsafe.SizeOf<ENUM_SERVICE_STATUS_PROCESSW>());
        Assert.Equal(48, Unsafe.SizeOf<ENUM_SERVICE_STATUSW>());
        Assert.Equal(24, Unsafe.SizeOf<WTS_SESSION_INFOW>());
        Assert.Equal(216, Unsafe.SizeOf<WTSINFOW>());
        Assert.Equal(56, Unsafe.SizeOf<MIB_TCP6ROW_OWNER_PID>());
        Assert.Equal(28, Unsafe.SizeOf<MIB_UDP6ROW_OWNER_PID>());
        Assert.Equal(24, Unsafe.SizeOf<PDH_FMT_COUNTERVALUE_ITEM_W>());
        Assert.Equal(312, Unsafe.SizeOf<DXGI_ADAPTER_DESC1>());
        Assert.Equal(72, Unsafe.SizeOf<CHOOSECOLORW>());
        Assert.Equal(840, Unsafe.SizeOf<DISPLAY_DEVICEW>());
        Assert.Equal(696, Unsafe.SizeOf<SHFILEINFOW>());
        Assert.Equal(668, Unsafe.SizeOf<RM_PROCESS_INFO>());
        Assert.Equal(16, Unsafe.SizeOf<RM_UNIQUE_PROCESS>());
        Assert.Equal(12, Unsafe.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        Assert.Equal(28, Unsafe.SizeOf<THREADENTRY32>());
        Assert.Equal(280, Unsafe.SizeOf<WAITCHAIN_NODE_INFO>());
        Assert.Equal(24, Unsafe.SizeOf<MODULEINFO>());
        Assert.Equal(112, Unsafe.SizeOf<SHELLEXECUTEINFOW>());
        Assert.Equal(40, Unsafe.SizeOf<BITMAPINFOHEADER>());
        Assert.Equal(88, Unsafe.SizeOf<WINTRUST_DATA>());
        Assert.Equal(72, Unsafe.SizeOf<WINTRUST_CATALOG_INFO>());
    }
}

public class GpuInstanceTests
{
    [Fact]
    public void ParsesEngineInstance()
    {
        Assert.True(GpuCollector.TryParseEngine("pid_1234_luid_0x00000000_0x0000D1A5_phys_0_eng_0_engtype_3D", out var pid, out var luid, out var type));
        Assert.Equal(1234, pid);
        Assert.Equal("0x00000000_0x0000D1A5", luid);
        Assert.Equal("3D", type);
        Assert.True(GpuCollector.TryParseAdapter("luid_0x00000000_0x0000D1A5_phys_0", out var adapter));
        Assert.Equal(luid, adapter);
        Assert.False(GpuCollector.TryParseEngine("_Total", out _, out _, out _));
    }
}

public class ProcessToolsTests
{
    [Fact]
    public void SplitCommandLine_QuotedAndPlain()
    {
        Assert.Equal(("C:\\Program Files\\App\\app.exe", "-x \"y z\""), ProcessTools.SplitCommandLine("\"C:\\Program Files\\App\\app.exe\" -x \"y z\""));
        Assert.Equal(("notepad", "file.txt"), ProcessTools.SplitCommandLine("notepad file.txt"));
        Assert.Equal(("cmd", ""), ProcessTools.SplitCommandLine("  cmd  "));
        Assert.Equal(("", ""), ProcessTools.SplitCommandLine(""));
    }
}
