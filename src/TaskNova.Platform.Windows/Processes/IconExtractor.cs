using TaskNova.Platform.Windows.Native;

namespace TaskNova.Platform.Windows;

/// <summary>
/// Shell icon of an executable as 32×32 BGRA pixels (premultiplied alpha), drawn into a DIB section – no System.Drawing.
/// Must run on an STA thread (SHGetFileInfo needs COM).
/// </summary>
public static unsafe class IconExtractor
{
    public const int Size = 32;

    public static byte[]? Extract(string path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(path))
        {
            return null;
        }

        SHFILEINFOW info;
        nuint ok;
        fixed (char* p = path)
        {
            ok = Shell32.SHGetFileInfo(p, 0, &info, (uint)sizeof(SHFILEINFOW), Shell32.ShgfiIcon | Shell32.ShgfiLargeIcon);
        }

        if (ok == 0 || info.hIcon == 0)
        {
            return null;
        }

        try
        {
            return Render(info.hIcon);
        }
        finally
        {
            User32.DestroyIcon(info.hIcon);
        }
    }

    private static byte[]? Render(nint icon)
    {
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = Size,
            biHeight = -Size, // top-down
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0 // BI_RGB
        };

        var dc = Gdi32.CreateCompatibleDC(0);
        if (dc == 0)
        {
            return null;
        }

        try
        {
            var bitmap = Gdi32.CreateDIBSection(dc, &header, 0, out var bits, 0, 0);
            if (bitmap == 0 || bits == null)
            {
                return null;
            }

            var old = Gdi32.SelectObject(dc, bitmap);
            try
            {
                var length = Size * Size * 4;
                new Span<byte>(bits, length).Clear();
                if (!User32.DrawIconEx(dc, 0, 0, icon, Size, Size, 0, 0, User32.DiNormal))
                {
                    return null;
                }

                var pixels = new Span<byte>(bits, length).ToArray();

                // Old icons without alpha channel: every drawn pixel is opaque.
                var hasAlpha = false;
                for (var i = 3; i < pixels.Length; i += 4)
                {
                    if (pixels[i] != 0)
                    {
                        hasAlpha = true;
                        break;
                    }
                }

                if (!hasAlpha)
                {
                    for (var i = 0; i < pixels.Length; i += 4)
                    {
                        if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0)
                        {
                            pixels[i + 3] = 255;
                        }
                    }
                }

                return pixels;
            }
            finally
            {
                Gdi32.SelectObject(dc, old);
                Gdi32.DeleteObject(bitmap);
            }
        }
        finally
        {
            Gdi32.DeleteDC(dc);
        }
    }
}
