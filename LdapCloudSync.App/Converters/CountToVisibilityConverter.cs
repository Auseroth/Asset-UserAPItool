using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LdapCloudSync.App.Converters;

/// <summary>
/// Converts an integer count to Visibility. Count > 0 = Visible, 0 = Collapsed.
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}