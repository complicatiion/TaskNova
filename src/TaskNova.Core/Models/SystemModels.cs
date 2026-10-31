namespace TaskNova.Core.Models;

/// <summary>Values that do not change while TaskNova runs (read once at start).</summary>
public sealed class StaticSystemInfo
{
    public string ComputerName { get; init; } = "";

    public string OsName { get; init; } = "";

    public string OsVersion { get; init; } = "";

    public string CpuName { get; init; } = "";

    public int CpuSockets { get; init; }

    public int CpuCores { get; init; }

    public int LogicalProcessors { get; init; }

    public int BaseMhz { get; init; }

    public bool VirtualMachine { get; init; }

    public ulong TotalMemoryBytes { get; init; }

    public IReadOnlyList<string> GpuNames { get; init; } = [];

    public bool IsServer { get; init; }

    public bool IsElevated { get; init; }

    public string UserName { get; init; } = "";
}

public enum ServiceState
{
    Unknown = 0,
    Stopped = 1,
    StartPending = 2,
    StopPending = 3,
    Running = 4,
    ContinuePending = 5,
    PausePending = 6,
    Paused = 7
}

public enum ServiceStartMode
{
    Boot = 0,
    System = 1,
    Automatic = 2,
    Manual = 3,
    Disabled = 4,

    /// <summary>Automatic with delayed start (SERVICE_CONFIG_DELAYED_AUTO_START_INFO).</summary>
    AutomaticDelayed = 100,

    Unknown = -1
}

/// <summary>Windows service from the Service Control Manager.</summary>
public sealed record ServiceInfo
{
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public ServiceState State { get; init; }

    public int ProcessId { get; init; }

    /// <summary>SERVICE_WIN32_OWN_PROCESS (0x10), SERVICE_WIN32_SHARE_PROCESS (0x20) …</summary>
    public int ServiceType { get; init; }

    public ServiceStartMode StartMode { get; init; } = ServiceStartMode.Unknown;

    public string? Description { get; init; }

    public string? BinaryPath { get; init; }

    public string? Account { get; init; }

    public string? LoadOrderGroup { get; init; }

    public IReadOnlyList<string> Dependencies { get; init; } = [];

    public bool CanStop { get; init; }
}

public enum SessionState
{
    Active = 0,
    Connected = 1,
    ConnectQuery = 2,
    Shadow = 3,
    Disconnected = 4,
    Idle = 5,
    Listen = 6,
    Reset = 7,
    Down = 8,
    Init = 9
}

/// <summary>Remote protocol of a session (WTSClientProtocolType).</summary>
public enum SessionProtocol
{
    Console = 0,
    Ica = 1,
    Rdp = 2,
    Unknown = -1
}

/// <summary>Terminal Services session (console, RDP, Citrix ICA).</summary>
public sealed record SessionInfo
{
    public required int SessionId { get; init; }

    public string WinStation { get; init; } = "";

    public string UserName { get; init; } = "";

    public string Domain { get; init; } = "";

    public SessionState State { get; init; }

    public SessionProtocol Protocol { get; init; } = SessionProtocol.Unknown;

    public string? ClientName { get; init; }

    public string? ClientAddress { get; init; }

    public DateTime? LogonTimeUtc { get; init; }

    public DateTime? LastInputUtc { get; init; }

    public DateTime? CurrentTimeUtc { get; init; }

    public string FullUserName => string.IsNullOrEmpty(Domain) ? UserName : $"{Domain}\\{UserName}";
}

public enum ConnectionProtocol
{
    Tcp,
    Tcp6,
    Udp,
    Udp6
}

/// <summary>Open TCP connection or UDP endpoint with owning process.</summary>
public sealed record ConnectionInfo(
    ConnectionProtocol Protocol,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State,
    int ProcessId)
{
    public string Key => $"{Protocol}|{LocalAddress}|{LocalPort}|{RemoteAddress}|{RemotePort}|{ProcessId}";
}
