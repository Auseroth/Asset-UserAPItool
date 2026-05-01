using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LdapCloudSync.App.Converters;

/// <summary>
/// Returns Collapsed when count > 0, Visible when count == 0.
/// Used for "empty state" labels that hide once items are present.
/// </summary>
public sealed class CountToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int n && n > 0 ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}