using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using TaskNova.App.ViewModels;

namespace TaskNova.App.Controls;

/// <summary>
/// Applies a <see cref="ColumnLayout"/> (widths, hidden columns) to the column definitions of a grid – header and every row
/// use it, so resizing a header resizes the whole list. Column i of the layout is grid column i + Offset.
/// The grid listens only while it is in the visual tree (recycled rows do not leak).
/// </summary>
public static class GridColumns
{
    public static readonly AttachedProperty<ColumnLayout?> LayoutProperty =
        AvaloniaProperty.RegisterAttached<Grid, ColumnLayout?>("Layout", typeof(GridColumns));

    public static readonly AttachedProperty<int> OffsetProperty =
        AvaloniaProperty.RegisterAttached<Grid, int>("Offset", typeof(GridColumns));

    private static readonly AttachedProperty<Subscription?> SubscriptionProperty =
        AvaloniaProperty.RegisterAttached<Grid, Subscription?>("Subscription", typeof(GridColumns));

    static GridColumns()
    {
        LayoutProperty.Changed.AddClassHandler<Grid>((grid, _) => Rebind(grid));
        OffsetProperty.Changed.AddClassHandler<Grid>((grid, _) => Apply(grid));
    }

    public static ColumnLayout? GetLayout(Grid grid) => grid.GetValue(LayoutProperty);

    public static void SetLayout(Grid grid, ColumnLayout? value) => grid.SetValue(LayoutProperty, value);

    public static int GetOffset(Grid grid) => grid.GetValue(OffsetProperty);

    public static void SetOffset(Grid grid, int value) => grid.SetValue(OffsetProperty, value);

    private static void Rebind(Grid grid)
    {
        grid.GetValue(SubscriptionProperty)?.Dispose();
        grid.SetValue(SubscriptionProperty, GetLayout(grid) is { } layout ? new Subscription(grid, layout) : null);
        Apply(grid);
    }

    private static void Apply(Grid grid)
    {
        if (GetLayout(grid) is not { } layout)
        {
            return;
        }

        var offset = GetOffset(grid);
        for (var i = 0; i < layout.All.Count; i++)
        {
            var index = i + offset;
            if (index < grid.ColumnDefinitions.Count)
            {
                var length = layout.All[i].Length;
                if (grid.ColumnDefinitions[index].Width != length)
                {
                    grid.ColumnDefinitions[index].Width = length;
                }
            }
        }

        // A width-0 column does not clip its cells (fixed-size grips, borders, text still draw and take clicks):
        // hide them without touching IsVisible, which some cells bind.
        foreach (var child in grid.Children)
        {
            var i = Grid.GetColumn(child) - offset;
            if (i >= 0 && i < layout.All.Count)
            {
                var visible = layout.All[i].Visible;
                child.Opacity = visible ? 1 : 0;
                child.IsHitTestVisible = visible;
            }
        }
    }

    /// <summary>Listens to the column specs while the grid is attached to the visual tree.</summary>
    private sealed class Subscription : IDisposable
    {
        private readonly Grid _grid;
        private readonly ColumnLayout _layout;
        private bool _listening;

        public Subscription(Grid grid, ColumnLayout layout)
        {
            _grid = grid;
            _layout = layout;
            grid.AttachedToVisualTree += OnAttached;
            grid.DetachedFromVisualTree += OnDetached;
            if (Avalonia.VisualTree.VisualExtensions.GetVisualRoot(grid) is not null)
            {
                Listen(true);
            }
        }

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            Listen(true);
            Apply(_grid);
        }

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Listen(false);

        private void Listen(bool on)
        {
            if (_listening == on)
            {
                return;
            }

            _listening = on;
            foreach (var spec in _layout.All)
            {
                if (on)
                {
                    spec.PropertyChanged += OnSpecChanged;
                }
                else
                {
                    spec.PropertyChanged -= OnSpecChanged;
                }
            }
        }

        private void OnSpecChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ColumnSpec.Length) or null or "")
            {
                Apply(_grid);
            }
        }

        public void Dispose()
        {
            Listen(false);
            _grid.AttachedToVisualTree -= OnAttached;
            _grid.DetachedFromVisualTree -= OnDetached;
        }
    }
}
