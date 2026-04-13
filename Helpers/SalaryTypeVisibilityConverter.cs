using ShopManager.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShopManager.Helpers;

/// <summary>
/// SalaryType 對應 Visibility
/// ConverterParameter = "Hourly" | "Monthly" | "Contract"
/// 當類型相符時 Visible，否則 Collapsed
/// </summary>
[ValueConversion(typeof(SalaryType), typeof(Visibility))]
public class SalaryTypeVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SalaryType type && parameter is string param)
        {
            return param switch
            {
                "Hourly" => type == SalaryType.Hourly ? Visibility.Visible : Visibility.Collapsed,
                "Monthly" => type == SalaryType.Monthly ? Visibility.Visible : Visibility.Collapsed,
                "Contract" => type == SalaryType.Contract ? Visibility.Visible : Visibility.Collapsed,
                _ => Visibility.Collapsed
            };
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
