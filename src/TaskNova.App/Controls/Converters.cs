using System.Globalization;
using Avalonia.Data.Converters;

namespace TaskNova.App.Controls;

/// <summary>Small value converters for the views.</summary>
public static class Converters
{
    /// <summary>True when the value's text equals the converter parameter (active sort column, selected filter).</summary>
    public static readonly IValueConverter EqualsParameter = new EqualsParameterConverter();

    /// <summary>Dims rows (stopped services, missing startup targets).</summary>
    public static readonly IValueConverter Dim = new FuncValueConverter<bool, double>(dim => dim ? 0.55 : 1.0);

    private sealed class EqualsParameterConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
