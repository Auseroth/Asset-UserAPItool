using System.Globalization;
using System.Windows.Data;

namespace LdapCloudSync.App.Converters;

/// <summary>
/// Converts between an enum value and a boolean for RadioButton binding.
/// ConverterParameter = the enum value name this RadioButton represents.
/// </summary>
public sealed class EnumBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string enumName)
            return Enum.Parse(targetType, enumName);

        return Binding.DoNothing;
    }
}