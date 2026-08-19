using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Patchbay.App.Converters;

/// <summary>
/// The handful of converters the shell needs, as singletons.
///
/// Exposed as static fields and used through <c>{x:Static}</c> rather than
/// declared as resources. A <c>StaticResource</c> in one merged dictionary
/// cannot see resources declared in another that is merged after it, which
/// makes converter lookup quietly dependent on the order of the merge list;
/// <c>x:Static</c> has no such rule.
/// </summary>
public static class ValueConverters
{
    public static readonly IValueConverter BoolToVisibility = new BooleanToVisibilityConverter();

    public static readonly IValueConverter NotBoolToVisibility =
        new DelegateConverter(value => value is true ? Visibility.Collapsed : Visibility.Visible);

    public static readonly IValueConverter NotBool =
        new DelegateConverter(value => value is not true);

    /// <summary>Visible when the value is anything but null.</summary>
    public static readonly IValueConverter PresentToVisibility =
        new DelegateConverter(value => value is null ? Visibility.Collapsed : Visibility.Visible);

    /// <summary>Visible when the value is null. The other half of an empty state.</summary>
    public static readonly IValueConverter AbsentToVisibility =
        new DelegateConverter(value => value is null ? Visibility.Visible : Visibility.Collapsed);

    /// <summary>Visible when the string has something in it.</summary>
    public static readonly IValueConverter TextToVisibility =
        new DelegateConverter(value =>
            string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible);

    /// <summary>Visible when a collection has anything in it.</summary>
    public static readonly IValueConverter AnyToVisibility =
        new DelegateConverter(value =>
            value is System.Collections.ICollection { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed);

    private sealed class DelegateConverter(Func<object?, object> convert) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            convert(value);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
