using System.Runtime.InteropServices;
using TaskNova.Core.Models;
using TaskNova.Platform.Windows.Native;

namespace TaskNova.Platform.Windows;

/// <summary>
/// Windows services through the Service Control Manager API directly (no PowerShell, no sc.exe):
/// list with PID, configuration, start/stop/restart, start type and dependencies.
/// </summary>
public static unsafe class ServiceManager
{
    private const int ErrorDependentServicesRunning = 1051;
    private const int ErrorServiceNotActive = 1062;
    private const int ErrorServiceAlreadyRunning = 1056;

    /// <summary>All Win32 services (including per-user service instances) with state and process id.</summary>
    public static IReadOnlyList<ServiceInfo> ReadAll(bool withConfiguration)
    {
        var result = new List<ServiceInfo>();
        if (!OperatingSystem.IsWindows())
        {
            return result;
        }

        var scm = Advapi32.OpenSCManager(null, null, Advapi32.ScManagerConnect | Advapi32.ScManagerEnumerateService);
        if (scm == 0)
        {
            return result;
        }

        try
        {
            uint resume = 0;
            uint needed;
            uint returned;
            Advapi32.EnumServicesStatusEx(scm, Advapi32.ScEnumProcessInfo, Advapi32.ServiceWin32, Advapi32.ServiceStateAll, null, 0,
                out needed, out returned, ref resume, null);
            if (needed == 0)
            {
                return result;
            }

            var buffer = new byte[needed + 4096];
            while (true)
            {
                bool ok;
                fixed (byte* p = buffer)
                {
                    ok = Advapi32.EnumServicesStatusEx(scm, Advapi32.ScEnumProcessInfo, Advapi32.ServiceWin32, Advapi32.ServiceStateAll, p,
                        (uint)buffer.Length, out needed, out returned, ref resume, null);
                    var error = ok ? 0 : Marshal.GetLastPInvokeError();
                    var items = (ENUM_SERVICE_STATUS_PROCESSW*)p;
                    for (var i = 0; i < returned; i++)
                    {
                        var item = items[i];
                        var name = new string(item.lpServiceName);
                        var status = item.ServiceStatusProcess;
                        var info = new ServiceInfo
                        {
                            Name = name,
                            DisplayName = item.lpDisplayName == null ? name : new string(item.lpDisplayName),
                            State = (ServiceState)status.dwCurrentState,
                            ProcessId = (int)status.dwProcessId,
                            ServiceType = (int)status.dwServiceType,
                            CanStop = (status.dwControlsAccepted & Advapi32.ServiceAcceptStop) != 0
                        };
                        result.Add(withConfiguration ? WithConfiguration(scm, info) : info);
                    }

                    if (ok || error != 234 /* ERROR_MORE_DATA */)
                    {
                        break;
                    }
                }

                if (needed > buffer.Length)
                {
                    buffer = new byte[needed + 4096];
                }
            }
        }
        finally
        {
            Advapi32.CloseServiceHandle(scm);
        }

        return result;
    }

    /// <summary>Start type, binary, account, group, description and dependencies of one service.</summary>
    public static ServiceInfo ReadConfiguration(ServiceInfo service)
    {
        if (!OperatingSystem.IsWindows())
        {
            return service;
        }

        var scm = Advapi32.OpenSCManager(null, null, Advapi32.ScManagerConnect);
        if (scm == 0)
        {
            return service;
        }

        try
        {
            return WithConfiguration(scm, service);
        }
        finally
        {
            Advapi32.CloseServiceHandle(scm);
        }
    }

    private static ServiceInfo WithConfiguration(nint scm, ServiceInfo service)
    {
        var handle = Advapi32.OpenService(scm, service.Name, Advapi32.ServiceQueryConfig);
        if (handle == 0)
        {
            return service;
        }

        try
        {
            var config = QueryConfig(handle);
            if (config is null)
            {
                return service;
            }

            var (startType, binary, group, dependencies, account) = config.Value;
            var mode = (ServiceStartMode)startType;
            if (mode == ServiceStartMode.Automatic && IsDelayed(handle))
            {
                mode = ServiceStartMode.AutomaticDelayed;
            }

            return service with
            {
                StartMode = mode,
                BinaryPath = binary,
                LoadOrderGroup = group,
                Dependencies = dependencies,
                Account = account,
                Description = Description(handle)
            };
        }
        finally
        {
            Advapi32.CloseServiceHandle(handle);
        }
    }

    private static (uint StartType, string? Binary, string? Group, IReadOnlyList<string> Dependencies, string? Account)? QueryConfig(nint handle)
    {
        Advapi32.QueryServiceConfig(handle, null, 0, out var needed);
        if (needed == 0 || needed > 64 * 1024)
        {
            return null;
        }

        var buffer = stackalloc byte[(int)needed];
        if (!Advapi32.QueryServiceConfig(handle, buffer, needed, out _))
        {
            return null;
        }

        var config = (QUERY_SERVICE_CONFIGW*)buffer;
        var dependencies = new List<string>();
        var dep = config->lpDependencies;
        if (dep != null)
        {
            while (*dep != '\0')
            {
                var text = new string(dep);
                // Groups are prefixed with SC_GROUP_IDENTIFIER '+'.
                dependencies.Add(text.TrimStart('+'));
                dep += text.Length + 1;
            }
        }

        return (config->dwStartType,
            config->lpBinaryPathName == null ? null : new string(config->lpBinaryPathName),
            config->lpLoadOrderGroup == null || *config->lpLoadOrderGroup == '\0' ? null : new string(config->lpLoadOrderGroup),
            dependencies,
            config->lpServiceStartName == null ? null : new string(config->lpServiceStartName));
    }

    private static bool IsDelayed(nint handle)
    {
        SERVICE_DELAYED_AUTO_START_INFO info;
        return Advapi32.QueryServiceConfig2(handle, Advapi32.ServiceConfigDelayedAutoStartInfo, &info, (uint)sizeof(SERVICE_DELAYED_AUTO_START_INFO), out _)
               && info.fDelayedAutostart != 0;
    }

    private static string? Description(nint handle)
    {
        Advapi32.QueryServiceConfig2(handle, Advapi32.ServiceConfigDescription, null, 0, out var needed);
        if (needed == 0 || needed > 32 * 1024)
        {
            return null;
        }

        var buffer = stackalloc byte[(int)needed];
        if (!Advapi32.QueryServiceConfig2(handle, Advapi32.ServiceConfigDescription, buffer, needed, out _))
        {
            return null;
        }

        var text = ((SERVICE_DESCRIPTIONW*)buffer)->lpDescription;
        if (text == null)
        {
            return null;
        }

        var description = new string(text);
        return description.StartsWith('@') ? LoadIndirect(description) : description;
    }

    /// <summary>"@%SystemRoot%\system32\x.dll,-100" → localized text.</summary>
    private static string? LoadIndirect(string reference)
    {
        var buffer = stackalloc char[1024];
        return Shell32.SHLoadIndirectString(reference, buffer, 1024, 0) == 0 ? new string(buffer) : null;
    }

    public static ActionResult Start(string name)
    {
        return WithService(name, Advapi32.ServiceStart | Advapi32.ServiceQueryStatus, handle =>
        {
            if (!Advapi32.StartService(handle, 0, 0))
            {
                var error = Marshal.GetLastPInvokeError();
                return error == ErrorServiceAlreadyRunning ? ActionResult.Ok : ActionResult.Fail(error);
            }

            return WaitFor(handle, ServiceState.Running);
        });
    }

    /// <summary>
    /// Stops a service. Running dependents are only stopped with <paramref name="includeDependents"/>; otherwise the
    /// result carries <see cref="DependentsRunning"/> so the UI can ask.
    /// </summary>
    public static ActionResult Stop(string name, bool includeDependents)
    {
        if (includeDependents)
        {
            foreach (var dependent in Dependents(name, activeOnly: true))
            {
                var result = Stop(dependent, includeDependents: true);
                if (!result.Success)
                {
                    return result;
                }
            }
        }

        return WithService(name, Advapi32.ServiceStop | Advapi32.ServiceQueryStatus, handle =>
        {
            if (!Advapi32.ControlService(handle, Advapi32.ServiceControlStop, out _))
            {
                var error = Marshal.GetLastPInvokeError();
                return error switch
                {
                    ErrorServiceNotActive => ActionResult.Ok,
                    ErrorDependentServicesRunning => new ActionResult(false, DependentsRunning),
                    _ => ActionResult.Fail(error)
                };
            }

            return WaitFor(handle, ServiceState.Stopped);
        });
    }

    /// <summary>Marker error of <see cref="Stop"/>.</summary>
    public const string DependentsRunning = "dependents-running";

    public static ActionResult Restart(string name)
    {
        var stop = Stop(name, includeDependents: true);
        return stop.Success ? Start(name) : stop;
    }

    public static ActionResult SetStartMode(string name, ServiceStartMode mode)
    {
        var startType = mode switch
        {
            ServiceStartMode.Automatic or ServiceStartMode.AutomaticDelayed => 2u,
            ServiceStartMode.Manual => 3u,
            ServiceStartMode.Disabled => 4u,
            _ => Advapi32.ServiceNoChange
        };

        return WithService(name, Advapi32.ServiceChangeConfig | Advapi32.ServiceQueryConfig, handle =>
        {
            if (!Advapi32.ChangeServiceConfig(handle, Advapi32.ServiceNoChange, startType, Advapi32.ServiceNoChange, null, null, 0, null, null, null,
                    null))
            {
                return ActionResult.Fail(Marshal.GetLastPInvokeError());
            }

            if (startType == 2)
            {
                var info = new SERVICE_DELAYED_AUTO_START_INFO { fDelayedAutostart = mode == ServiceStartMode.AutomaticDelayed ? 1 : 0 };
                if (!Advapi32.ChangeServiceConfig2(handle, Advapi32.ServiceConfigDelayedAutoStartInfo, &info))
                {
                    return ActionResult.Fail(Marshal.GetLastPInvokeError());
                }
            }

            return ActionResult.Ok;
        });
    }

    /// <summary>Services that depend on <paramref name="name"/> (in the order they must be stopped).</summary>
    public static IReadOnlyList<string> Dependents(string name, bool activeOnly)
    {
        var result = new List<string>();
        WithService(name, Advapi32.ServiceEnumerateDependents, handle =>
        {
            var state = activeOnly ? Advapi32.ServiceActive : Advapi32.ServiceStateAll;
            Advapi32.EnumDependentServices(handle, state, null, 0, out var needed, out _);
            if (needed == 0)
            {
                return ActionResult.Ok;
            }

            var buffer = new byte[needed];
            fixed (byte* p = buffer)
            {
                if (Advapi32.EnumDependentServices(handle, state, p, needed, out _, out var count))
                {
                    var items = (ENUM_SERVICE_STATUSW*)p;
                    for (var i = 0; i < count; i++)
                    {
                        result.Add(new string(items[i].lpServiceName));
                    }
                }
            }

            return ActionResult.Ok;
        });
        return result;
    }

    private static ActionResult WithService(string name, uint access, Func<nint, ActionResult> action)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ActionResult.Fail("Windows only");
        }

        var scm = Advapi32.OpenSCManager(null, null, Advapi32.ScManagerConnect);
        if (scm == 0)
        {
            return ActionResult.Fail(Marshal.GetLastPInvokeError());
        }

        try
        {
            var handle = Advapi32.OpenService(scm, name, access);
            if (handle == 0)
            {
                return ActionResult.Fail(Marshal.GetLastPInvokeError());
            }

            try
            {
                return action(handle);
            }
            finally
            {
                Advapi32.CloseServiceHandle(handle);
            }
        }
        finally
        {
            Advapi32.CloseServiceHandle(scm);
        }
    }

    /// <summary>Waits up to 30 s for the target state (services report pending states meanwhile).</summary>
    private static ActionResult WaitFor(nint handle, ServiceState target)
    {
        var deadline = Environment.TickCount64 + 30_000;
        SERVICE_STATUS_PROCESS status;
        while (Environment.TickCount64 < deadline)
        {
            if (!Advapi32.QueryServiceStatusEx(handle, Advapi32.ScStatusProcessInfo, &status, (uint)sizeof(SERVICE_STATUS_PROCESS), out _))
            {
                return ActionResult.Fail(Marshal.GetLastPInvokeError());
            }

            if ((ServiceState)status.dwCurrentState == target)
            {
                return ActionResult.Ok;
            }

            if (target == ServiceState.Running && (ServiceState)status.dwCurrentState == ServiceState.Stopped && status.dwWin32ExitCode != 0)
            {
                return ActionResult.Fail((int)status.dwWin32ExitCode);
            }

            Thread.Sleep(250);
        }

        return ActionResult.Fail(1053); // ERROR_SERVICE_REQUEST_TIMEOUT
    }
}
