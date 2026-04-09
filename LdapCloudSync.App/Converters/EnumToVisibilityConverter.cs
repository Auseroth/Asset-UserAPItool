using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LdapCloudSync.App.Converters;

/// <summary>
/// Returns Visible when the bound enum value matches the ConverterParameter, Collapsed otherwise.
/// Usage: Visibility="{Binding MyEnum, Converter={StaticResource EnumToVis}, ConverterParameter=SomeValue}"
/// </summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return Visibility.Collapsed;
        return value.ToString() == parameter.ToString() ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}