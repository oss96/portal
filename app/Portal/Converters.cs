using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Portal;

/// <summary>True becomes Collapsed, false becomes Visible.</summary>
public sealed class InverseVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is Visibility.Collapsed;
}

/// <summary>Null, empty text or an empty collection becomes Collapsed, anything else Visible; the parameter "invert" flips it.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var empty = value is null || value is string { Length: 0 } || value is System.Collections.ICollection { Count: 0 };
        if (parameter is "invert") empty = !empty;
        return empty ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Two-way match between an enum value and the converter parameter, for radio buttons.</summary>
public sealed class EnumMatch : IValueConverter
{
    /// <summary>Shared instance.</summary>
    public static EnumMatch Instance { get; } = new();

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Equals(value, parameter);

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? parameter : Binding.DoNothing;
}
