using System.Runtime.InteropServices;

namespace TaskNova.Platform.Windows.Native;

// All structures are blittable (LibraryImport / Native AOT). Layouts follow the 64-bit Windows SDK headers;
// TaskNova ships for x64 and ARM64 only.

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct UNICODE_STRING
{
    public ushort Length;
    public ushort MaximumLength;
    public char* Buffer;

    public readonly string? ToStringOrNull() => Buffer == null || Length == 0 ? null : new string(Buffer, 0, Length / 2);
}

/// <summary>SYSTEM_PROCESS_INFORMATION (256 bytes on 64-bit), followed by NumberOfThreads × SYSTEM_THREAD_INFORMATION.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_PROCESS_INFORMATION
{
    public uint NextEntryOffset;
    public uint NumberOfThreads;
    public long WorkingSetPrivateSize;
    public uint HardFaultCount;
    public uint NumberOfThreadsHighWatermark;
    public ulong CycleTime;
    public long CreateTime;
    public long UserTime;
    public long KernelTime;
    public UNICODE_STRING ImageName;
    public int BasePriority;
    public nint UniqueProcessId;
    public nint InheritedFromUniqueProcessId;
    public uint HandleCount;
    public uint SessionId;
    public nuint UniqueProcessKey;
    public nuint PeakVirtualSize;
    public nuint VirtualSize;
    public uint PageFaultCount;
    public nuint PeakWorkingSetSize;
    public nuint WorkingSetSize;
    public nuint QuotaPeakPagedPoolUsage;
    public nuint QuotaPagedPoolUsage;
    public nuint QuotaPeakNonPagedPoolUsage;
    public nuint QuotaNonPagedPoolUsage;
    public nuint PagefileUsage;
    public nuint PeakPagefileUsage;
    public nuint PrivatePageCount;
    public long ReadOperationCount;
    public long WriteOperationCount;
    public long OtherOperationCount;
    public long ReadTransferCount;
    public long WriteTransferCount;
    public long OtherTransferCount;
}

/// <summary>SYSTEM_THREAD_INFORMATION (80 bytes on 64-bit).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_THREAD_INFORMATION
{
    public long KernelTime;
    public long UserTime;
    public long CreateTime;
    public uint WaitTime;
    public nint StartAddress;
    public nint UniqueProcess;
    public nint UniqueThread;
    public int Priority;
    public int BasePriority;
    public uint ContextSwitches;
    public uint ThreadState;
    public uint WaitReason;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
{
    public long IdleTime;
    public long KernelTime;
    public long UserTime;
    public long DpcTime;
    public long InterruptTime;
    public uint InterruptCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MEMORYSTATUSEX
{
    public uint dwLength;
    public uint dwMemoryLoad;
    public ulong ullTotalPhys;
    public ulong ullAvailPhys;
    public ulong ullTotalPageFile;
    public ulong ullAvailPageFile;
    public ulong ullTotalVirtual;
    public ulong ullAvailVirtual;
    public ulong ullAvailExtendedVirtual;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PERFORMANCE_INFORMATION
{
    public uint cb;
    public nuint CommitTotal;
    public nuint CommitLimit;
    public nuint CommitPeak;
    public nuint PhysicalTotal;
    public nuint PhysicalAvailable;
    public nuint SystemCache;
    public nuint KernelTotal;
    public nuint KernelPaged;
    public nuint KernelNonpaged;
    public nuint PageSize;
    public uint HandleCount;
    public uint ProcessCount;
    public uint ThreadCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct LUID
{
    public uint LowPart;
    public int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TOKEN_PRIVILEGES
{
    public uint PrivilegeCount;
    public LUID Luid;
    public uint Attributes;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SHFILEINFOW
{
    public nint hIcon;
    public int iIcon;
    public uint dwAttributes;
    public fixed char szDisplayName[260];
    public fixed char szTypeName[80];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SHELLEXECUTEINFOW
{
    public uint cbSize;
    public uint fMask;
    public nint hwnd;
    public char* lpVerb;
    public char* lpFile;
    public char* lpParameters;
    public char* lpDirectory;
    public int nShow;
    public nint hInstApp;
    public nint lpIDList;
    public char* lpClass;
    public nint hkeyClass;
    public uint dwHotKey;
    public nint hIconOrMonitor;
    public nint hProcess;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth;
    public int biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint biCompression;
    public uint biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public uint biClrUsed;
    public uint biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DISPLAY_DEVICEW
{
    public uint cb;
    public fixed char DeviceName[32];
    public fixed char DeviceString[128];
    public uint StateFlags;
    public fixed char DeviceID[128];
    public fixed char DeviceKey[128];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DXGI_ADAPTER_DESC1
{
    public fixed char Description[128];
    public uint VendorId;
    public uint DeviceId;
    public uint SubSysId;
    public uint Revision;
    public nuint DedicatedVideoMemory;
    public nuint DedicatedSystemMemory;
    public nuint SharedSystemMemory;
    public LUID AdapterLuid;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct CHOOSECOLORW
{
    public uint lStructSize;
    public nint hwndOwner;
    public nint hInstance;
    public uint rgbResult;
    public uint* lpCustColors;
    public uint Flags;
    public nint lCustData;
    public nint lpfnHook;
    public char* lpTemplateName;
}

// ------------------------------------------------------------------ PDH

[StructLayout(LayoutKind.Sequential)]
internal struct PDH_FMT_COUNTERVALUE
{
    public uint CStatus;
    public double DoubleValue;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PDH_FMT_COUNTERVALUE_LARGE
{
    public uint CStatus;
    public long LargeValue;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PDH_FMT_COUNTERVALUE_ITEM_W
{
    public char* szName;
    public PDH_FMT_COUNTERVALUE FmtValue;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PDH_FMT_COUNTERVALUE_ITEM_LARGE_W
{
    public char* szName;
    public PDH_FMT_COUNTERVALUE_LARGE FmtValue;
}

// ------------------------------------------------------------------ Services

[StructLayout(LayoutKind.Sequential)]
internal struct SERVICE_STATUS
{
    public uint dwServiceType;
    public uint dwCurrentState;
    public uint dwControlsAccepted;
    public uint dwWin32ExitCode;
    public uint dwServiceSpecificExitCode;
    public uint dwCheckPoint;
    public uint dwWaitHint;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SERVICE_STATUS_PROCESS
{
    public uint dwServiceType;
    public uint dwCurrentState;
    public uint dwControlsAccepted;
    public uint dwWin32ExitCode;
    public uint dwServiceSpecificExitCode;
    public uint dwCheckPoint;
    public uint dwWaitHint;
    public uint dwProcessId;
    public uint dwServiceFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ENUM_SERVICE_STATUS_PROCESSW
{
    public char* lpServiceName;
    public char* lpDisplayName;
    public SERVICE_STATUS_PROCESS ServiceStatusProcess;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ENUM_SERVICE_STATUSW
{
    public char* lpServiceName;
    public char* lpDisplayName;
    public SERVICE_STATUS ServiceStatus;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct QUERY_SERVICE_CONFIGW
{
    public uint dwServiceType;
    public uint dwStartType;
    public uint dwErrorControl;
    public char* lpBinaryPathName;
    public char* lpLoadOrderGroup;
    public uint dwTagId;
    public char* lpDependencies;
    public char* lpServiceStartName;
    public char* lpDisplayName;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SERVICE_DESCRIPTIONW
{
    public char* lpDescription;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SERVICE_DELAYED_AUTO_START_INFO
{
    public int fDelayedAutostart;
}

// ------------------------------------------------------------------ Terminal Services

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WTS_SESSION_INFOW
{
    public uint SessionId;
    public char* pWinStationName;
    public int State;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WTSINFOW
{
    public int State;
    public uint SessionId;
    public uint IncomingBytes;
    public uint OutgoingBytes;
    public uint IncomingFrames;
    public uint OutgoingFrames;
    public uint IncomingCompressedBytes;
    public uint OutgoingCompressedBytes;
    public fixed char WinStationName[32];
    public fixed char Domain[17];
    public fixed char UserName[21];
    public long ConnectTime;
    public long DisconnectTime;
    public long LastInputTime;
    public long LogonTime;
    public long CurrentTime;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WTS_CLIENT_ADDRESS
{
    public uint AddressFamily;
    public fixed byte Address[20];
}

// ------------------------------------------------------------------ IP Helper

[StructLayout(LayoutKind.Sequential)]
internal struct MIB_TCPROW_OWNER_PID
{
    public uint dwState;
    public uint dwLocalAddr;
    public uint dwLocalPort;
    public uint dwRemoteAddr;
    public uint dwRemotePort;
    public uint dwOwningPid;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MIB_TCP6ROW_OWNER_PID
{
    public fixed byte ucLocalAddr[16];
    public uint dwLocalScopeId;
    public uint dwLocalPort;
    public fixed byte ucRemoteAddr[16];
    public uint dwRemoteScopeId;
    public uint dwRemotePort;
    public uint dwState;
    public uint dwOwningPid;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MIB_UDPROW_OWNER_PID
{
    public uint dwLocalAddr;
    public uint dwLocalPort;
    public uint dwOwningPid;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MIB_UDP6ROW_OWNER_PID
{
    public fixed byte ucLocalAddr[16];
    public uint dwLocalScopeId;
    public uint dwLocalPort;
    public uint dwOwningPid;
}

// ------------------------------------------------------------------ WinTrust / catalogs

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WINTRUST_FILE_INFO
{
    public uint cbStruct;
    public char* pcwszFilePath;
    public nint hFile;
    public nint pgKnownSubject;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WINTRUST_CATALOG_INFO
{
    public uint cbStruct;
    public uint dwCatalogVersion;
    public char* pcwszCatalogFilePath;
    public char* pcwszMemberTag;
    public char* pcwszMemberFilePath;
    public nint hMemberFile;
    public byte* pbCalculatedFileHash;
    public uint cbCalculatedFileHash;
    public nint pcCatalogContext;
    public nint hCatAdmin;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WINTRUST_DATA
{
    public uint cbStruct;
    public nint pPolicyCallbackData;
    public nint pSIPClientData;
    public uint dwUIChoice;
    public uint fdwRevocationChecks;
    public uint dwUnionChoice;
    public void* pInfo;
    public uint dwStateAction;
    public nint hWVTStateData;
    public char* pwszURLReference;
    public uint dwProvFlags;
    public uint dwUIContext;
    public nint pSignatureSettings;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct CATALOG_INFO
{
    public uint cbStruct;
    public fixed char wszCatalogFile[260];
}
