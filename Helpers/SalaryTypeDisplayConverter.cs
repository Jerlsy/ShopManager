using ShopManager.Models;
using System.Globalization;
using System.Windows.Data;

namespace ShopManager.Helpers;

[ValueConversion(typeof(SalaryType), typeof(string))]
public class SalaryTypeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SalaryType t ? t switch
        {
            SalaryType.Hourly  => "時薪制",
            SalaryType.Monthly => "月薪制",
            _ => value.ToString() ?? string.Empty
        } : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
