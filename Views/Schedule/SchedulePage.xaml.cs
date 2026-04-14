using ShopManager.Models;
using ShopManager.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ShopManager.Views.Schedule;

public partial class SchedulePage : UserControl
{
    public SchedulePage(ScheduleViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }

    // ── 員工拖放開始 ──────────────────────
    private void Employee_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Employee employee)
        {
            var data = new DataObject("Employee", employee);
            DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
        }
    }

    // ── 拖放到班別格子 ────────────────────
    private void ShiftBlock_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("Employee"))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (sender is FrameworkElement fe && fe.Tag is ShiftBlock block && block.IsDisabled)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void ShiftBlock_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("Employee")) return;
        if (DataContext is not ScheduleViewModel vm) return;

        var employee = (Employee)e.Data.GetData("Employee");

        ShiftBlock? block = null;
        if (sender is FrameworkElement fe)
        {
            if (fe.Tag is ShiftBlock b)
                block = b;
            else if (fe.DataContext is ShiftBlock b2)
                block = b2;
        }

        if (block is null || block.IsDisabled) return;

        await vm.DropEmployeeAsync(employee, block.Date, block.ShiftSetting);
        e.Handled = true;
    }

    // ── 日期格子點擊 → 開啟快速新增 ───────
    private void CalendarDay_Click(object sender, MouseButtonEventArgs e)
    {
        // 如果點擊源是員工 chip（EntryItem border），不要觸發快速新增
        if (e.OriginalSource is FrameworkElement src &&
            (src.DataContext is EntryItem || src.DataContext is ShiftBlock))
            return;

        if (sender is FrameworkElement fe
            && fe.DataContext is CalendarDay day
            && !day.IsPlaceholder
            && !day.IsClosed
            && DataContext is ScheduleViewModel vm)
        {
            vm.OpenQuickAddCommand.Execute(day);
            e.Handled = true;
        }
    }

    // ── 周視圖日期標題點擊（切換到日視圖）──
    private void WeekDayHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe
            && fe.DataContext is CalendarDay day
            && DataContext is ScheduleViewModel vm)
        {
            vm.SelectedDate = day.Date;
            vm.ViewMode = CalendarViewMode.Day;
        }
    }
}
