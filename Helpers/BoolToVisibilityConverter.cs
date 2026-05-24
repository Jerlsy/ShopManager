using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShopManager.Helpers;

/// <summary>
/// bool → Visibility 轉換器。
/// ConverterParameter 支援以下選項（可組合，用 | 分隔，順序不限）：
///   Inverse — 反轉 true/false 判斷
///   Hidden  — false 時返回 Hidden（保留版面空間）；省略則返回 Collapsed（不佔位）
/// 範例： "Inverse"、"Hidden"、"Inverse|Hidden"
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag      = value is bool b && b;
        var paramStr  = parameter as string ?? string.Empty;
        var inverse   = paramStr.Contains("Inverse", StringComparison.OrdinalIgnoreCase);
        var useHidden = paramStr.Contains("Hidden",  StringComparison.OrdinalIgnoreCase);

        if (inverse) flag = !flag;
        if (flag) return Visibility.Visible;
        return useHidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible;
}
