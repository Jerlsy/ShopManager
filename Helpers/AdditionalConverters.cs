using ShopManager.Models;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShopManager.Helpers;

/// <summary>DateOnly ↔ DateTime?（供 WPF DatePicker 使用）</summary>
[ValueConversion(typeof(DateOnly), typeof(DateTime?))]
public class DateOnlyConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateOnly d ? new DateTime(d.Year, d.Month, d.Day) : (DateTime?)null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateTime dt ? DateOnly.FromDateTime(dt) : DateOnly.FromDateTime(DateTime.Today);
}

/// <summary>DateOnly? ↔ DateTime?（可空版，供離職日使用）</summary>
[ValueConversion(typeof(DateOnly?), typeof(DateTime?))]
public class NullableDateOnlyConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateOnly d ? new DateTime(d.Year, d.Month, d.Day) : (DateTime?)null;

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateTime dt ? DateOnly.FromDateTime(dt) : (DateOnly?)null;
}

/// <summary>姓名取首字（中文取第一個字，英文取首字母大寫）</summary>
[ValueConversion(typeof(string), typeof(string))]
public class NameInitialConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var name = value as string ?? string.Empty;
        return name.Length > 0 ? name[0].ToString() : "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>bool → GridLength（ConverterParameter="TrueValue|FalseValue"，支援 * 和數字）</summary>
[ValueConversion(typeof(bool), typeof(GridLength))]
public class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        var parts = (parameter as string ?? "*|*").Split('|');
        var token = flag ? parts[0] : (parts.Length > 1 ? parts[1] : "*");
        return ParseGridLength(token);
    }

    private static GridLength ParseGridLength(string token) =>
        token == "*" ? new GridLength(1, GridUnitType.Star)
        : token == "0" ? new GridLength(0)
        : token.EndsWith("*") && double.TryParse(token[..^1], out var sw)
            ? new GridLength(sw, GridUnitType.Star)
        : double.TryParse(token, out var px)
            ? new GridLength(px)
        : new GridLength(1, GridUnitType.Star);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>null 或空字串 → Collapsed，非 null → Visible（用於顯示驗證錯誤訊息）</summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNull = value is null || (value is string s && string.IsNullOrEmpty(s));
        var inverse = parameter is string p && p == "Inverse";
        return (isNull ^ inverse) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>
/// int（Count）== 0 → Visible，!= 0 → Collapsed
/// 用於顯示「清單為空」提示訊息
/// ConverterParameter="Inverse" 可反轉（0→Collapsed）
/// </summary>
[ValueConversion(typeof(int), typeof(Visibility))]
public class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isZero = value is int i && i == 0;
        var inverse = parameter is string p && p == "Inverse";
        return (isZero ^ inverse) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>
/// CalendarViewMode → Visibility
/// ConverterParameter: "Month", "Week", "Day", "WeekOrDay"
/// </summary>
[ValueConversion(typeof(ViewModels.CalendarViewMode), typeof(Visibility))]
public class ViewModeVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ViewModels.CalendarViewMode mode || parameter is not string target)
            return Visibility.Collapsed;

        var visible = target switch
        {
            "Month" => mode == ViewModels.CalendarViewMode.Month,
            "Week" => mode == ViewModels.CalendarViewMode.Week,
            "Day" => mode == ViewModels.CalendarViewMode.Day,
            "WeekOrDay" => mode is ViewModels.CalendarViewMode.Week or ViewModels.CalendarViewMode.Day,
            _ => false
        };
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>比較兩個色碼字串是否相同（供調色盤選中高亮）</summary>
public class ColorMatchConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is string a && values[1] is string b)
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>byte[]? → BitmapImage（供大頭貼圖片顯示）</summary>
[ValueConversion(typeof(byte[]), typeof(System.Windows.Media.ImageSource))]
public class BytesToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not byte[] data || data.Length == 0) return null;
        var bi = new System.Windows.Media.Imaging.BitmapImage();
        bi.BeginInit();
        bi.StreamSource = new System.IO.MemoryStream(data);
        bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bi.EndInit();
        return bi;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>hex 色碼字串 → SolidColorBrush（供班別色塊顯示）</summary>
[ValueConversion(typeof(string), typeof(System.Windows.Media.SolidColorBrush))]
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                return new System.Windows.Media.SolidColorBrush(color);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HexToBrushConverter] 無效色碼 '{hex}': {ex.Message}");
            }
        }
        return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>EmployeeConstraintType → 顯示文字（休假日 / 上班日 / 優先班別 / 不排班）</summary>
[ValueConversion(typeof(ViewModels.EmployeeConstraintType), typeof(string))]
public class ConstraintTypeToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ViewModels.EmployeeConstraintType t ? t switch
        {
            ViewModels.EmployeeConstraintType.DayOff            => "休假日",
            ViewModels.EmployeeConstraintType.WorkDay           => "上班日",
            ViewModels.EmployeeConstraintType.ShiftPriority     => "優先班別",
            ViewModels.EmployeeConstraintType.ExcludeAutoAssign => "不排班",
            _ => value.ToString() ?? ""
        } : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>將數值乘上指定倍率，常用於視窗尺寸上限計算。支援單一綁定與多重綁定。</summary>
[ValueConversion(typeof(double), typeof(double))]
public class MultiplyDoubleConverter : IValueConverter, IMultiValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double number)
            return 0d;

        var factor = 1d;
        if (parameter is string text &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            factor = parsed;
        }

        return number * factor;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return 0d;
        
        var val = values[0] is double d ? d : 0d;
        var factor = 1d;

        if (values[1] is double f) factor = f;
        else if (values[1] is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var ps)) factor = ps;
        else if (parameter is string p && double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var pp)) factor = pp;

        return val * factor;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>bool 取反（true → false, false → true）</summary>
[ValueConversion(typeof(bool), typeof(bool))]
public class BoolInverseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;
}

/// <summary>double/decimal != 0 → Visible，= 0 → Collapsed</summary>
[ValueConversion(typeof(double), typeof(Visibility))]
public class NonZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var nonZero = value switch
        {
            double d   => d != 0,
            decimal dc => dc != 0,
            int i      => i != 0,
            _          => false,
        };
        return nonZero ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>BonusPresetType == Custom → Visible（用於自訂名稱欄位顯示）</summary>
[ValueConversion(typeof(Models.BonusPresetType), typeof(Visibility))]
public class BonusTypeToCustomLabelVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Models.BonusPresetType t && t == Models.BonusPresetType.Custom
            ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>URL 字串 → BitmapImage（供 LINE 頭像等網路圖片顯示）</summary>
[ValueConversion(typeof(string), typeof(System.Windows.Media.ImageSource))]
public class UrlToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrEmpty(url)) return null;
        try
        {
            var bi = new System.Windows.Media.Imaging.BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(url);
            bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnDemand;
            bi.EndInit();
            return bi;
        }
        catch { return null; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>BonusPresetType != Custom → Visible（用於預設名稱文字顯示）</summary>
[ValueConversion(typeof(Models.BonusPresetType), typeof(Visibility))]
public class BonusTypeToPresetLabelVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Models.BonusPresetType t && t != Models.BonusPresetType.Custom
            ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
