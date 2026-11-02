using System;
using System.Globalization;
using System.Windows.Data;

namespace NetMonitor.Controls;

/// <summary>Binds a radio button to an int or enum: checked when the value equals the parameter.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true) return Binding.DoNothing;
        string text = parameter.ToString()!;
        if (targetType == typeof(string)) return text;
        if (targetType == typeof(bool)) return bool.Parse(text);
        if (targetType.IsEnum) return Enum.Parse(targetType, text);
        return System.Convert.ChangeType(text, targetType, CultureInfo.InvariantCulture);
    }
}
