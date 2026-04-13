using System.Globalization;
using System.Windows.Data;

namespace ShopManager.Helpers;

/// <summary>null → 字串選擇，ConverterParameter="null時顯示|非null時顯示"</summary>
[ValueConversion(typeof(object), typeof(string))]
public class NullToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "是|否").Split('|');
        return value is null
            ? (parts.Length > 0 ? parts[0] : string.Empty)
            : (parts.Length > 1 ? parts[1] : string.Empty);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
