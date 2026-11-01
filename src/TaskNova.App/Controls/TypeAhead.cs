using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TaskNova.App.Controls;

/// <summary>An item a list can jump to by typing its first letters.</summary>
public interface ITypeAheadItem
{
    string TypeAheadText { get; }
}

/// <summary>
/// Quick jump in lists like in Explorer: type the first letters of a name and the selection moves to the next match.
/// Letters typed within one second form one prefix; the same letter again cycles through the matches.
/// </summary>
public static class TypeAhead
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>("Enabled", typeof(TypeAhead));

    private static readonly AttachedProperty<string> PrefixProperty =
        AvaloniaProperty.RegisterAttached<ListBox, string>("Prefix", typeof(TypeAhead), "");

    private static readonly AttachedProperty<DateTime> LastProperty =
        AvaloniaProperty.RegisterAttached<ListBox, DateTime>("Last", typeof(TypeAhead));

    static TypeAhead()
    {
        EnabledProperty.Changed.AddClassHandler<ListBox>((list, e) =>
        {
            list.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
            if (e.NewValue is true)
            {
                list.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Bubble);
            }
        });
    }

    public static bool GetEnabled(ListBox list) => list.GetValue(EnabledProperty);

    public static void SetEnabled(ListBox list, bool value) => list.SetValue(EnabledProperty, value);

    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (sender is not ListBox list || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]) || e.Source is TextBox)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var prefix = (now - list.GetValue(LastProperty)).TotalMilliseconds < 1000 ? list.GetValue(PrefixProperty) + e.Text : e.Text;
        list.SetValue(LastProperty, now);

        var items = (list.ItemsSource as IList)?.Cast<object>().ToList() ?? list.Items.Cast<object>().ToList();
        if (items.Count == 0)
        {
            return;
        }

        // Same letter repeated ("sss"): cycle through the matches of that letter.
        var cycling = prefix.Length > 1 && prefix.All(c => char.ToUpperInvariant(c) == char.ToUpperInvariant(prefix[0]));
        var search = cycling ? prefix[..1] : prefix;
        list.SetValue(PrefixProperty, prefix);

        var start = list.SelectedIndex;
        var from = cycling || prefix.Length == 1 ? start + 1 : Math.Max(0, start);
        for (var step = 0; step < items.Count; step++)
        {
            var index = (from + step) % items.Count;
            if (items[index] is ITypeAheadItem item && item.TypeAheadText.StartsWith(search, StringComparison.CurrentCultureIgnoreCase))
            {
                list.SelectedIndex = index;
                list.ScrollIntoView(index);
                e.Handled = true;
                return;
            }
        }
    }
}
