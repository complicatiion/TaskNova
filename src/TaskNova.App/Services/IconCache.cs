using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TaskNova.Core.Appearance;

namespace TaskNova.App.Services;

/// <summary>
/// Bitmaps for program icons (BGRA pixels from the enricher, cached by executable path) and vector icons rendered to
/// bitmaps for the tray menu and the tray icon itself.
/// </summary>
public static class IconCache
{
    private static readonly Dictionary<string, Bitmap> ByPath = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Bitmap> Rendered = new(StringComparer.Ordinal);

    // Bounded: a long-running instance on a busy server sees thousands of different executables. Dropping the cache only
    // drops references – bitmaps still shown by rows stay alive until those rows go.
    private const int MaxIcons = 384;
    private const int MaxRendered = 64;

    /// <summary>Program icon; the same pixel array (enricher cache) always gives the same bitmap.</summary>
    public static unsafe Bitmap? FromPixels(string? path, byte[]? pixels, int size)
    {
        if (pixels is null || size <= 0 || pixels.Length < size * size * 4)
        {
            return null;
        }

        if (path is not null && ByPath.TryGetValue(path, out var cached))
        {
            return cached;
        }

        Bitmap bitmap;
        fixed (byte* p = pixels)
        {
            bitmap = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, (nint)p, new PixelSize(size, size), new Vector(96, 96), size * 4);
        }

        if (path is not null)
        {
            if (ByPath.Count >= MaxIcons)
            {
                ByPath.Clear();
            }

            ByPath[path] = bitmap;
        }

        return bitmap;
    }

    public static Geometry? Geometry(string key) =>
        Application.Current is { } app && app.TryGetResource(key, null, out var value) && value is Geometry geometry ? geometry : null;

    /// <summary>24×24 vector icon as bitmap (tray menu entries).</summary>
    public static Bitmap? Render(string key, Rgb color, int size = 32)
    {
        var cacheKey = $"{key}|{color}|{size}";
        if (Rendered.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var geometry = Geometry(key);
        if (geometry is null)
        {
            return null;
        }

        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            var scale = size / 24d;
            using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            {
                context.DrawGeometry(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)), null, geometry);
            }
        }

        Remember(cacheKey, bitmap);
        return bitmap;
    }

    /// <summary>The TaskNova logo tinted in a color (tray icon, tray menu header), from the white logo mask.</summary>
    public static Bitmap TintedLogo(Rgb color, int size)
    {
        var cacheKey = $"logo|{color}|{size}";
        if (Rendered.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        using var stream = AssetLoader.Open(new Uri("avares://tasknova/Assets/logo-mask.png"));
        using var mask = new Bitmap(stream);
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            var rect = new Rect(0, 0, size, size);
            using (context.PushOpacityMask(new ImageBrush(mask) { Stretch = Stretch.Uniform }, rect))
            {
                context.FillRectangle(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)), rect);
            }
        }

        Remember(cacheKey, bitmap);
        return bitmap;
    }

    private static void Remember(string key, Bitmap bitmap)
    {
        if (Rendered.Count >= MaxRendered)
        {
            Rendered.Clear();
        }

        Rendered[key] = bitmap;
    }
}
