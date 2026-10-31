using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using TaskNova.Platform.Windows;

namespace TaskNova.App.Services;

/// <summary>Native title bar in the theme colors (sidebar color of the active theme) and a soft fade-in.</summary>
public static class WindowChrome
{
    private static readonly List<WeakReference<Window>> Windows = [];

    public static void Apply(Window window)
    {
        Windows.Add(new WeakReference<Window>(window));
        window.Closed += (_, _) => Windows.RemoveAll(w => !w.TryGetTarget(out var t) || ReferenceEquals(t, window));
        window.Opened += (_, _) =>
        {
            Color(window);
            Controls.Motion.FadeIn(window.Content as Visual, offsetY: 8, milliseconds: 200);
        };
    }

    /// <summary>After a theme or accent change.</summary>
    public static void RefreshAll()
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var reference in Windows.ToList())
            {
                if (reference.TryGetTarget(out var window) && window.IsVisible)
                {
                    Color(window);
                }
            }
        }, DispatcherPriority.Background);
    }

    private static void Color(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle()?.Handle is not { } hwnd)
        {
            return;
        }

        var theme = AppearanceService.Instance.Theme;
        DwmTitleBar.Apply(hwnd, theme.BgSidebar, theme.TextPrimary, theme.IsDark);
    }

    /// <summary>Native handle of a window (owner of Win32 dialogs).</summary>
    public static nint Handle(Window? window) => window?.TryGetPlatformHandle()?.Handle ?? 0;
}
