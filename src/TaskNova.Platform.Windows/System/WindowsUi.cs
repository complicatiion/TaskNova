using System.Globalization;
using Microsoft.Win32;
using TaskNova.Core.Appearance;
using TaskNova.Core.Localization;
using TaskNova.Platform.Windows.Native;

namespace TaskNova.Platform.Windows;

/// <summary>Colors the native window title bar (dark mode on Windows 10 20H1+, caption color on Windows 11).</summary>
public static class DwmTitleBar
{
    private const int UseImmersiveDarkModeOld = 19;
    private const int UseImmersiveDarkMode = 20;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    public static void Apply(nint hwnd, Rgb caption, Rgb text, bool dark)
    {
        if (hwnd == 0 || !OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var on = dark ? 1 : 0;
            if (Dwmapi.DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref on, sizeof(int)) != 0)
            {
                Dwmapi.DwmSetWindowAttribute(hwnd, UseImmersiveDarkModeOld, ref on, sizeof(int));
            }

            // Ignored before Windows 11 (error code, no exception).
            var captionRef = (int)caption.ToColorRef();
            var textRef = (int)text.ToColorRef();
            Dwmapi.DwmSetWindowAttribute(hwnd, CaptionColor, ref captionRef, sizeof(int));
            Dwmapi.DwmSetWindowAttribute(hwnd, BorderColor, ref captionRef, sizeof(int));
            Dwmapi.DwmSetWindowAttribute(hwnd, TextColor, ref textRef, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }
}

/// <summary>The classic Windows color dialog (ChooseColor) for the custom accent color, with the 16 custom color slots.</summary>
public static unsafe class ColorDialog
{
    /// <param name="owner">Owner window handle (modal).</param>
    /// <param name="initial">Preselected color.</param>
    /// <param name="customColors">16 slots, updated in place when the user saves custom colors.</param>
    /// <returns>The chosen color, or null when cancelled.</returns>
    public static Rgb? Pick(nint owner, Rgb initial, Rgb[] customColors)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var slots = stackalloc uint[16];
        for (var i = 0; i < 16; i++)
        {
            slots[i] = i < customColors.Length ? customColors[i].ToColorRef() : 0xFFFFFF;
        }

        var dialog = new CHOOSECOLORW
        {
            lStructSize = (uint)sizeof(CHOOSECOLORW),
            hwndOwner = owner,
            rgbResult = initial.ToColorRef(),
            lpCustColors = slots,
            Flags = Comdlg32.CcRgbInit | Comdlg32.CcFullOpen | Comdlg32.CcAnyColor
        };

        if (!Comdlg32.ChooseColor(&dialog))
        {
            return null;
        }

        for (var i = 0; i < Math.Min(16, customColors.Length); i++)
        {
            customColors[i] = Rgb.FromColorRef(slots[i]);
        }

        return Rgb.FromColorRef(dialog.rgbResult);
    }
}

/// <summary>Display language, regional format and keyboard layouts for the first-start language.</summary>
public static partial class SystemLanguage
{
    public static string Detect()
    {
        try
        {
            return LanguageDetection.Choose(CultureInfo.CurrentUICulture.Name, CultureInfo.CurrentCulture.Name, KeyboardLanguageIds());
        }
        catch (Exception)
        {
            return "en";
        }
    }

    private static IReadOnlyList<int> KeyboardLanguageIds()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var count = GetKeyboardLayoutList(0, null);
        if (count <= 0)
        {
            return [];
        }

        var layouts = new nint[count];
        count = GetKeyboardLayoutList(layouts.Length, layouts);
        return layouts.Take(Math.Max(0, count)).Select(h => (int)((long)h & 0xFFFF)).Distinct().ToList();
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial int GetKeyboardLayoutList(int count, [System.Runtime.InteropServices.Out] nint[]? list);
}

/// <summary>Windows "Apps use light theme" and the Windows accent color as fallbacks.</summary>
public static class WindowsTheme
{
    /// <summary>Accent color from HKCU\Software\Microsoft\Windows\DWM\AccentColor (ABGR); null when not set.</summary>
    public static Rgb? AccentColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int value)
            {
                return Rgb.FromColorRef((uint)value & 0xFFFFFF);
            }
        }
        catch (Exception)
        {
            // Not available (Server Core).
        }

        return null;
    }
}
