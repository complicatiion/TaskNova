using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using TaskNova.Core.Metrics;

namespace TaskNova.App.Controls;

/// <summary>One redraw signal per sampler tick for all charts (raised by the main view model).</summary>
public static class ChartClock
{
    public static event Action? Tick;

    public static void Raise() => Tick?.Invoke();
}

/// <summary>
/// Base of the live charts: lightweight custom-drawn controls (no chart library). A chart subscribes to
/// <see cref="ChartClock"/> only while it is in the visual tree and redraws only when it is visible and its data changed.
/// </summary>
public abstract class LiveChartBase : Control
{
    private long _drawnVersion = -1;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ChartClock.Tick += OnTick;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ChartClock.Tick -= OnTick;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTick()
    {
        if (!IsEffectivelyVisible)
        {
            return;
        }

        var version = DataVersion();
        if (version != _drawnVersion)
        {
            _drawnVersion = version;
            InvalidateVisual();
        }
    }

    /// <summary>Changes whenever the drawn data changes.</summary>
    protected abstract long DataVersion();

    protected static Color ColorOf(IBrush? brush, Color fallback) => brush is ISolidColorBrush solid ? solid.Color : fallback;

    // Pens and fills are cached per color: no allocations per frame for unchanged colors.
    private static readonly Dictionary<(Color, double, bool), IPen> Pens = new();
    private static readonly Dictionary<Color, IBrush> Fills = new();

    protected static IPen PenFor(Color color, double thickness, bool dashed = false)
    {
        if (!Pens.TryGetValue((color, thickness, dashed), out var pen))
        {
            pen = new ImmutablePen(new ImmutableSolidColorBrush(color), thickness, dashed ? new ImmutableDashStyle(new double[] { 3, 2 }, 0) : null,
                PenLineCap.Round, PenLineJoin.Round);
            Pens[(color, thickness, dashed)] = pen;
        }

        return pen;
    }

    /// <summary>Soft vertical gradient under a line: 35 % of the color at the top, transparent at the bottom.</summary>
    protected static IBrush FillFor(Color color)
    {
        if (!Fills.TryGetValue(color, out var brush))
        {
            brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x5A, color.R, color.G, color.B), 0),
                    new GradientStop(Color.FromArgb(0x05, color.R, color.G, color.B), 1)
                }
            }.ToImmutable();
            Fills[color] = brush;
        }

        return brush;
    }

    /// <summary>Line (and optional fill) of a series inside <paramref name="area"/>; newest value at the right edge.</summary>
    protected static void DrawSeries(DrawingContext context, RingBuffer series, Rect area, double maximum, Color color, double thickness, bool fill,
        bool dashed = false)
    {
        var count = series.Count;
        if (count < 2 || area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        var step = area.Width / Math.Max(1, series.Capacity - 1);
        var x0 = area.Right - (count - 1) * step;
        var max = maximum <= 0 ? 1 : maximum;

        double Y(double value) => area.Bottom - Math.Clamp(value / max, 0, 1) * area.Height;

        if (fill)
        {
            var area2 = new StreamGeometry();
            using (var g = area2.Open())
            {
                g.BeginFigure(new Point(x0, area.Bottom), true);
                for (var i = 0; i < count; i++)
                {
                    g.LineTo(new Point(x0 + i * step, Y(series[i])));
                }

                g.LineTo(new Point(area.Right, area.Bottom));
                g.EndFigure(true);
            }

            context.DrawGeometry(FillFor(color), null, area2);
        }

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(new Point(x0, Y(series[0])), false);
            for (var i = 1; i < count; i++)
            {
                g.LineTo(new Point(x0 + i * step, Y(series[i])));
            }

            g.EndFigure(false);
        }

        context.DrawGeometry(null, PenFor(color, thickness, dashed), line);
    }
}

/// <summary>
/// Area/line chart of one or two ring buffers (like the Windows Task Manager graphs). Maximum = NaN scales to the data.
/// </summary>
public sealed class SparkChart : LiveChartBase
{
    public static readonly StyledProperty<RingBuffer?> SeriesProperty = AvaloniaProperty.Register<SparkChart, RingBuffer?>(nameof(Series));

    public static readonly StyledProperty<RingBuffer?> Series2Property = AvaloniaProperty.Register<SparkChart, RingBuffer?>(nameof(Series2));

    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<SparkChart, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<SparkChart, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> Stroke2Property = AvaloniaProperty.Register<SparkChart, IBrush?>(nameof(Stroke2));

    public static readonly StyledProperty<IBrush?> GridBrushProperty = AvaloniaProperty.Register<SparkChart, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<bool> ShowGridProperty = AvaloniaProperty.Register<SparkChart, bool>(nameof(ShowGrid));

    public static readonly StyledProperty<bool> FillProperty = AvaloniaProperty.Register<SparkChart, bool>(nameof(Fill), true);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<SparkChart, double>(nameof(StrokeThickness), 1.6);

    static SparkChart()
    {
        AffectsRender<SparkChart>(SeriesProperty, Series2Property, MaximumProperty, StrokeProperty, Stroke2Property, GridBrushProperty,
            ShowGridProperty, FillProperty, StrokeThicknessProperty);
    }

    public RingBuffer? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public RingBuffer? Series2
    {
        get => GetValue(Series2Property);
        set => SetValue(Series2Property, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? Stroke2
    {
        get => GetValue(Stroke2Property);
        set => SetValue(Stroke2Property, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    public bool Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>The scale used in the last drawing (for "max" captions with auto scale).</summary>
    public double CurrentMaximum { get; private set; }

    protected override long DataVersion() => (Series?.Version ?? 0) * 7919 + (Series2?.Version ?? 0);

    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size).Deflate(0.5);
        if (area.Width <= 2 || area.Height <= 2)
        {
            return;
        }

        if (ShowGrid)
        {
            var grid = PenFor(ColorOf(GridBrush, Color.FromArgb(0x30, 0x80, 0x80, 0x80)), 1);
            for (var i = 1; i < 4; i++)
            {
                var y = Math.Round(area.Top + area.Height * i / 4) + 0.5;
                context.DrawLine(grid, new Point(area.Left, y), new Point(area.Right, y));
            }

            for (var i = 1; i < 6; i++)
            {
                var x = Math.Round(area.Left + area.Width * i / 6) + 0.5;
                context.DrawLine(grid, new Point(x, area.Top), new Point(x, area.Bottom));
            }
        }

        var maximum = Maximum;
        if (double.IsNaN(maximum))
        {
            var dataMax = Math.Max(Series?.Max() ?? 0, Series2?.Max() ?? 0);
            maximum = NiceCeiling(dataMax * 1.1);
        }

        CurrentMaximum = maximum;
        var color = ColorOf(Stroke, Color.FromRgb(0xEF, 0x47, 0x6F));
        if (Series2 is { } second)
        {
            DrawSeries(context, second, area, maximum, ColorOf(Stroke2, color), Math.Max(1, StrokeThickness - 0.3), false, dashed: true);
        }

        if (Series is { } first)
        {
            DrawSeries(context, first, area, maximum, color, StrokeThickness, Fill);
        }
    }

    /// <summary>1, 2, 5 × 10ⁿ above the value (auto scale for byte rates).</summary>
    internal static double NiceCeiling(double value)
    {
        if (value <= 1)
        {
            return 1;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var step in new[] { 1d, 2, 5, 10 })
        {
            if (step * magnitude >= value)
            {
                return step * magnitude;
            }
        }

        return 10 * magnitude;
    }
}

/// <summary>Grid of small charts, one per logical processor, drawn by one control (cheap with 64 cores).</summary>
public sealed class CoreGrid : LiveChartBase
{
    public static readonly StyledProperty<IReadOnlyList<RingBuffer>?> SeriesProperty =
        AvaloniaProperty.Register<CoreGrid, IReadOnlyList<RingBuffer>?>(nameof(Series));

    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<CoreGrid, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> FrameBrushProperty = AvaloniaProperty.Register<CoreGrid, IBrush?>(nameof(FrameBrush));

    public static readonly StyledProperty<bool> FillProperty = AvaloniaProperty.Register<CoreGrid, bool>(nameof(Fill), true);

    static CoreGrid()
    {
        AffectsRender<CoreGrid>(SeriesProperty, StrokeProperty, FrameBrushProperty, FillProperty);
    }

    public IReadOnlyList<RingBuffer>? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? FrameBrush
    {
        get => GetValue(FrameBrushProperty);
        set => SetValue(FrameBrushProperty, value);
    }

    public bool Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override long DataVersion()
    {
        var series = Series;
        return series is null || series.Count == 0 ? 0 : series[0].Version * 131 + series.Count;
    }

    public override void Render(DrawingContext context)
    {
        var series = Series;
        if (series is null || series.Count == 0 || Bounds.Width < 10 || Bounds.Height < 10)
        {
            return;
        }

        var n = series.Count;
        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(n * Bounds.Width / Bounds.Height)));
        var rows = (int)Math.Ceiling(n / (double)columns);
        const double gap = 4;
        var cellWidth = (Bounds.Width - gap * (columns - 1)) / columns;
        var cellHeight = (Bounds.Height - gap * (rows - 1)) / rows;
        var color = ColorOf(Stroke, Color.FromRgb(0xEF, 0x47, 0x6F));
        var frame = PenFor(ColorOf(FrameBrush, Color.FromArgb(0x40, 0x80, 0x80, 0x80)), 1);

        for (var i = 0; i < n; i++)
        {
            var column = i % columns;
            var row = i / columns;
            var cell = new Rect(column * (cellWidth + gap), row * (cellHeight + gap), cellWidth, cellHeight);
            context.DrawRectangle(null, frame, cell.Deflate(0.5), 3, 3);
            DrawSeries(context, series[i], cell.Deflate(1.5), 100, color, 1.2, Fill);
        }
    }
}

/// <summary>Ring gauge 0–100 % with rounded ends; the value glides to new values.</summary>
public sealed class RingGauge : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<RingGauge, double>(nameof(Value));

    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<RingGauge, double>(nameof(Thickness), 10);

    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<RingGauge, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<RingGauge, IBrush?>(nameof(Track));

    static RingGauge()
    {
        AffectsRender<RingGauge>(ValueProperty, ThicknessProperty, StrokeProperty, TrackProperty);
    }

    public RingGauge()
    {
        if (Motion.Enabled)
        {
            Transitions = new Transitions { new DoubleTransition { Property = ValueProperty, Duration = TimeSpan.FromMilliseconds(350) } };
        }
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var thickness = Thickness;
        var radius = (size - thickness) / 2;
        if (radius <= 1)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        if (Track is { } track)
        {
            context.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);
        }

        var value = Math.Clamp(Value, 0, 100);
        if (value < 0.2 || Stroke is not { } stroke)
        {
            return;
        }

        var pen = new Pen(stroke, thickness, lineCap: PenLineCap.Round);
        if (value >= 99.95)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var angle = value / 100 * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(start, false);
            g.ArcTo(end, new Size(radius, radius), 0, value > 50, SweepDirection.Clockwise);
            g.EndFigure(false);
        }

        context.DrawGeometry(null, pen, arc);
    }
}

/// <summary>Horizontal bar with up to two filled segments (memory: in use + cached) on a track.</summary>
public sealed class StackedBar : Control
{
    public static readonly StyledProperty<double> TotalProperty = AvaloniaProperty.Register<StackedBar, double>(nameof(Total), 100);

    public static readonly StyledProperty<double> FirstProperty = AvaloniaProperty.Register<StackedBar, double>(nameof(First));

    public static readonly StyledProperty<double> SecondProperty = AvaloniaProperty.Register<StackedBar, double>(nameof(Second));

    public static readonly StyledProperty<IBrush?> FirstBrushProperty = AvaloniaProperty.Register<StackedBar, IBrush?>(nameof(FirstBrush));

    public static readonly StyledProperty<IBrush?> SecondBrushProperty = AvaloniaProperty.Register<StackedBar, IBrush?>(nameof(SecondBrush));

    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<StackedBar, IBrush?>(nameof(Track));

    static StackedBar()
    {
        AffectsRender<StackedBar>(TotalProperty, FirstProperty, SecondProperty, FirstBrushProperty, SecondBrushProperty, TrackProperty);
    }

    public double Total
    {
        get => GetValue(TotalProperty);
        set => SetValue(TotalProperty, value);
    }

    public double First
    {
        get => GetValue(FirstProperty);
        set => SetValue(FirstProperty, value);
    }

    public double Second
    {
        get => GetValue(SecondProperty);
        set => SetValue(SecondProperty, value);
    }

    public IBrush? FirstBrush
    {
        get => GetValue(FirstBrushProperty);
        set => SetValue(FirstBrushProperty, value);
    }

    public IBrush? SecondBrush
    {
        get => GetValue(SecondBrushProperty);
        set => SetValue(SecondBrushProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var radius = bounds.Height / 2;
        if (Track is { } track)
        {
            context.DrawRectangle(track, null, bounds, radius, radius);
        }

        var total = Total <= 0 ? 1 : Total;
        var firstWidth = Math.Clamp(First / total, 0, 1) * bounds.Width;
        var secondWidth = Math.Clamp(Second / total, 0, 1 - firstWidth / Math.Max(1, bounds.Width)) * bounds.Width;
        using (context.PushClip(new RoundedRect(bounds, radius, radius)))
        {
            if (SecondBrush is { } second && secondWidth > 0)
            {
                context.FillRectangle(second, new Rect(firstWidth, 0, secondWidth, bounds.Height));
            }

            if (FirstBrush is { } first && firstWidth > 0)
            {
                context.FillRectangle(first, new Rect(0, 0, firstWidth, bounds.Height));
            }
        }
    }
}
