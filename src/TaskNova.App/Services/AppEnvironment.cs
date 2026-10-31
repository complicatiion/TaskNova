using System.Diagnostics;
using System.Reflection;

namespace TaskNova.App.Services;

/// <summary>Paths, links and shell actions of the app.</summary>
public static class AppEnvironment
{
    public const string ProjectUrl = "https://github.com/complicatiion/TaskNova";
    public const string MitLicenseUrl = "https://opensource.org/license/mit";
    public const string WebsiteUrl = "https://sksdesign.de";

    public static string Version =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppEnvironment).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public static string BaseDirectory => AppContext.BaseDirectory;

    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(BaseDirectory, "tasknova.exe");

    public static string LicensesDirectory => Path.Combine(BaseDirectory, "licenses");

    public static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No default browser.
        }
    }

    /// <summary>Opens a file or folder with its default program (licenses, services.msc …).</summary>
    public static void Open(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Not available.
        }
    }
}

/// <summary>Runs work on a dedicated STA thread (shell icons, COM-based Windows APIs) and returns a task.</summary>
public static class Sta
{
    public static Task<T> Run<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "TaskNova.Sta",
            Priority = ThreadPriority.BelowNormal
        };

        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }

        thread.Start();
        return completion.Task;
    }
}
