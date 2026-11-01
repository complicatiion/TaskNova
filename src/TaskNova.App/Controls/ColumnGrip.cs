using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TaskNova.App.ViewModels;

namespace TaskNova.App.Controls;

/// <summary>Drag handle on the right edge of a list header cell: resizes its <see cref="ColumnSpec"/>. Double-click: default width.</summary>
public sealed class ColumnGrip : Control
{
    public static readonly StyledProperty<ColumnSpec?> SpecProperty = AvaloniaProperty.Register<ColumnGrip, ColumnSpec?>(nameof(Spec));

    private Point _start;
    private double _startWidth;
    private bool _dragging;

    public ColumnGrip()
    {
        Width = 7;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        Margin = new Thickness(0, 0, -3, 0);
    }

    public ColumnSpec? Spec
    {
        get => GetValue(SpecProperty);
        set => SetValue(SpecProperty, value);
    }

    // Hit-testable but invisible; a thin line shows while hovering or dragging.
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (IsPointerOver || _dragging)
        {
            var brush = Application.Current?.FindResource("BrushAccentText") as IBrush ?? Brushes.Gray;
            context.FillRectangle(brush, new Rect(Bounds.Width / 2 - 1, 4, 2, Math.Max(0, Bounds.Height - 8)));
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Spec is not { } spec || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            spec.Width = spec.DefaultWidth;
            e.Handled = true;
            return;
        }

        _start = e.GetPosition(Parent as Visual ?? this);
        _startWidth = spec.Width;
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging && Spec is { } spec)
        {
            var position = e.GetPosition(Parent as Visual ?? this);
            spec.Width = _startWidth + (position.X - _start.X);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging)
        {
            _dragging = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
        InvalidateVisual();
    }
}
