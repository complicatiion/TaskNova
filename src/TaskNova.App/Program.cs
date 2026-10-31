using Avalonia;
using TaskNova.Core.Storage;
using TaskNova.Platform.Windows;

namespace TaskNova.App;

internal static class Program
{
    private const string MutexName = @"Local\TaskNova.App";
    private const string ShowEventName = @"Local\TaskNova.App.Show";

    /// <summary>Signal to the running instance of this session: show the window.</summary>
    internal static EventWaitHandle? ShowSignal { get; private set; }

    internal static bool StartInTray { get; private set; }

    /// <summary>Settings read once before Avalonia starts (the renderer choice is needed that early).</summary>
    internal static AppSettings? InitialSettings { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // Started by Windows instead of taskmgr.exe (optional "replace Task Manager"): its arguments are not ours.
        if (OperatingSystem.IsWindows() && ShellIntegration.IsTaskManagerRedirect(args))
        {
            args = [];
        }

        bool Has(string flag) => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

        // Command line for the installer (no window).
        if (OperatingSystem.IsWindows())
        {
            if (Has("--register-autostart"))
            {
                return SelfAutostart.Register(Privileges.CurrentUser(), Environment.ProcessPath ?? "") ? 0 : 1;
            }

            if (Has("--unregister-autostart"))
            {
                return SelfAutostart.Unregister(Privileges.CurrentUser()) ? 0 : 1;
            }

            if (Has("--unregister-autostart-all"))
            {
                SelfAutostart.UnregisterAll();
                return 0;
            }

            if (Has("--register-context-menu"))
            {
                return ShellIntegration.RegisterContextMenu(Environment.ProcessPath ?? "", "TaskNova") ? 0 : 1;
            }

            if (Has("--unregister-integration"))
            {
                ShellIntegration.UnregisterContextMenu();
                ShellIntegration.RestoreTaskManager();
                return 0;
            }
        }

        StartInTray = Has("--tray");

        Mutex? mutex = null;
        if (OperatingSystem.IsWindows())
        {
            if (Has("--elevated-restart"))
            {
                // "Restart as administrator": take over the mutex as soon as the old instance has released it.
                mutex = new Mutex(false, MutexName);
                try
                {
                    if (!mutex.WaitOne(TimeSpan.FromSeconds(15)))
                    {
                        mutex.Dispose();
                        return 0;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // Old instance ended without releasing – owned now.
                }
            }
            else if (!TryCreateSingleInstance(out mutex))
            {
                // One instance per session (terminal servers: one per user); a second start brings the window to the front.
                try
                {
                    using var existing = EventWaitHandle.OpenExisting(ShowEventName);
                    existing.Set();
                }
                catch (Exception)
                {
                    // First instance not ready yet.
                }

                return 0;
            }

            ShowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

            // Elevated: open paths, command lines and users of all processes like the Windows Task Manager.
            if (Privileges.IsElevated())
            {
                Privileges.EnableDebugPrivilege();
            }
        }

        InitialSettings = SettingsStore.Load();
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            ShowSignal?.Dispose();
            try
            {
                mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned (should not happen).
            }

            mutex?.Dispose();
        }
    }

    private static bool TryCreateSingleInstance(out Mutex? mutex)
    {
        mutex = new Mutex(true, MutexName, out var createdNew);
        if (createdNew)
        {
            return true;
        }

        mutex.Dispose();
        mutex = null;
        return false;
    }

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        // "Software" rendering (setting, applies after a restart): no GPU context and no ANGLE/D3D buffers – roughly
        // 30–60 MB less memory, at the cost of more CPU while charts animate. Useful on servers and VMs without a GPU.
        if (InitialSettings?.RenderMode == "software")
        {
            builder = builder.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] });
        }

        return builder;
    }
}
