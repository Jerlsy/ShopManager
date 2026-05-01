using MaterialDesignThemes.Wpf;
using ShopManager.ViewModels;
using System.Windows.Controls;

namespace ShopManager.Views.Dialogs;

public record ScheduleRulesDialogResult(
    List<int> FixedOffDays,
    List<int> ExcludedShiftIds,
    List<int> NotWithEmployeeIds,
    List<int> NotWithDayEmployeeIds);

public partial class ScheduleRulesDialog : UserControl
{
    private readonly List<DayOfWeekItem> _fixedOffItems;
    private readonly List<ShiftCheckItem> _excludeItems;
    private readonly List<ColleagueCheckItem> _notWithItems;
    private readonly List<ColleagueCheckItem> _notWithDayItems;

    public ScheduleRulesDialog(
        List<DayOfWeekItem> fixedOffDayItems,
        List<ShiftCheckItem> excludeShiftItems,
        List<ColleagueCheckItem> notWithItems,
        List<ColleagueCheckItem> notWithDayItems,
        List<int> shopClosedDays)
    {
        InitializeComponent();

        // 複製，避免取消時污染 ViewModel；店休日強制勾選並 disable
        _fixedOffItems = fixedOffDayItems
            .Select(d =>
            {
                var isClosed = shopClosedDays.Contains((int)d.Day);
                return new DayOfWeekItem
                {
                    Day = d.Day,
                    Label = d.Label,
                    IsShopClosed = isClosed,
                    IsChecked = d.IsChecked || isClosed,
                };
            })
            .ToList();

        _excludeItems = excludeShiftItems
            .Select(s => new ShiftCheckItem { ShiftId = s.ShiftId, Alias = s.Alias, IsChecked = s.IsChecked })
            .ToList();

        _notWithItems = notWithItems
            .Select(e => new ColleagueCheckItem { EmployeeId = e.EmployeeId, Name = e.Name, IsChecked = e.IsChecked })
            .ToList();

        _notWithDayItems = notWithDayItems
            .Select(e => new ColleagueCheckItem { EmployeeId = e.EmployeeId, Name = e.Name, IsChecked = e.IsChecked })
            .ToList();

        FixedOffList.ItemsSource = _fixedOffItems;

        if (_excludeItems.Count == 0)
        {
            NoShiftsHint.Visibility = System.Windows.Visibility.Visible;
            ExcludeShiftList.Visibility = System.Windows.Visibility.Collapsed;
        }
        else
        {
            ExcludeShiftList.ItemsSource = _excludeItems;
        }

        if (_notWithItems.Count == 0)
        {
            NoColleaguesHint.Visibility = System.Windows.Visibility.Visible;
            NotWithList.Visibility = System.Windows.Visibility.Collapsed;
        }
        else
        {
            NotWithList.ItemsSource = _notWithItems;
        }

        if (_notWithDayItems.Count == 0)
        {
            NoColleaguesDayHint.Visibility = System.Windows.Visibility.Visible;
            NotWithDayList.Visibility = System.Windows.Visibility.Collapsed;
        }
        else
        {
            NotWithDayList.ItemsSource = _notWithDayItems;
        }
    }

    private void OkButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var result = new ScheduleRulesDialogResult(
            FixedOffDays:         _fixedOffItems.Where(d => d.IsChecked && !d.IsShopClosed).Select(d => (int)d.Day).ToList(),
            ExcludedShiftIds:     _excludeItems.Where(s => s.IsChecked).Select(s => s.ShiftId).ToList(),
            NotWithEmployeeIds:   _notWithItems.Where(e => e.IsChecked).Select(e => e.EmployeeId).ToList(),
            NotWithDayEmployeeIds: _notWithDayItems.Where(e => e.IsChecked).Select(e => e.EmployeeId).ToList());

        DialogHost.CloseDialogCommand.Execute(result, this);
    }
}
