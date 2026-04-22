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

    // ── 員工拖放開始（從員工清單） ──────────────────────
    private void Employee_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Employee employee
            && DataContext is ScheduleViewModel vm)
        {
            vm.SelectedEmployee = employee;
            var data = new DataObject("Employee", employee);
            DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
            vm.SelectedEmployee = null;
            DragTooltipPopup.IsOpen = false;
        }
    }

    // ── 班表間拖曳（從班次 EntryItem 開始） ──────────────
    private Point _dragStartPoint;
    private EntryItem? _dragEntryCandidate;

    private void EntryRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _dragEntryCandidate = (sender as FrameworkElement)?.DataContext as EntryItem;
    }

    private void EntryRow_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragEntryCandidate is null) return;

        var pos = e.GetPosition(null);
        var diff = _dragStartPoint - pos;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        if (DataContext is not ScheduleViewModel vm) return;

        var entry = _dragEntryCandidate;
        _dragEntryCandidate = null;

        vm.DragSourceEntryId = entry.EntryId;
        vm.SelectedEmployee = entry.Employee;
        var data = new DataObject("Employee", entry.Employee);
        data.SetData("EntryId", entry.EntryId);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        vm.DragSourceEntryId = -1;
        vm.SelectedEmployee = null;
        DragTooltipPopup.IsOpen = false;
    }

    private void EntryRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragEntryCandidate is null) return;
        var entry = _dragEntryCandidate;
        _dragEntryCandidate = null;

        if (DataContext is ScheduleViewModel vm)
        {
            vm.OpenEntryCardCommand.Execute(entry);
            e.Handled = true;
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

        ShiftBlock? block = null;
        if (sender is FrameworkElement fe)
        {
            if (fe.Tag is ShiftBlock b)        block = b;
            else if (fe.DataContext is ShiftBlock b2) block = b2;
        }

        if (block is not null && block.IsDisabled)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            if (!string.IsNullOrEmpty(block.DisabledReason))
            {
                DragTooltipText.Text = block.DisabledReason;
                DragTooltipPopup.IsOpen = true;
            }
            else
            {
                DragTooltipPopup.IsOpen = false;
            }
        }
        else
        {
            e.Effects = e.Data.GetDataPresent("EntryId") ? DragDropEffects.Move : DragDropEffects.Copy;
            e.Handled = true;
            DragTooltipPopup.IsOpen = false;
        }
    }

    private async void ShiftBlock_Drop(object sender, DragEventArgs e)
    {
        DragTooltipPopup.IsOpen = false;
        if (!e.Data.GetDataPresent("Employee")) return;
        if (DataContext is not ScheduleViewModel vm) return;

        var employee = (Employee)e.Data.GetData("Employee");
        int? sourceEntryId = e.Data.GetDataPresent("EntryId") ? (int?)e.Data.GetData("EntryId") : null;

        ShiftBlock? block = null;
        if (sender is FrameworkElement fe)
        {
            if (fe.Tag is ShiftBlock b)        block = b;
            else if (fe.DataContext is ShiftBlock b2) block = b2;
        }

        if (block is null || block.IsDisabled) return;

        await vm.DropEmployeeAsync(employee, block.Date, block.ShiftSetting, sourceEntryId);
        e.Handled = true;
    }

    // ── 日期格子點擊 → 月視圖開啟詳情，周/日視圖開啟快速新增 ───────
    private void CalendarDay_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement src &&
            (src.DataContext is EntryItem || src.DataContext is ShiftBlock))
            return;

        if (sender is FrameworkElement fe
            && fe.DataContext is CalendarDay day
            && !day.IsPlaceholder
            && DataContext is ScheduleViewModel vm)
        {
            if (vm.IsMonthView)
            {
                vm.OpenDayDetailCommand.Execute(day);
                e.Handled = true;
            }
            else if (!day.IsClosed)
            {
                vm.OpenQuickAddCommand.Execute(day);
                e.Handled = true;
            }
        }
    }

    // ── 周視圖日期標題點擊 → 開啟日期詳情 ──
    private void WeekDayHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe
            && fe.DataContext is CalendarDay day
            && DataContext is ScheduleViewModel vm)
        {
            vm.OpenDayDetailCommand.Execute(day);
        }
    }

    // ── 日期詳情背景點擊 → 關閉 ──
    private void DayDetailOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.Source == sender && DataContext is ScheduleViewModel vm)
        {
            vm.CloseDayDetailCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── 員工大頭貼點擊（日期詳情浮層中的 chip）→ 開啟員工資訊卡 ──
    private void EntryAvatar_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is EntryItem entry
            && DataContext is ScheduleViewModel vm)
        {
            vm.OpenEntryCardCommand.Execute(entry);
            e.Handled = true;
        }
    }

    // ── 員工資訊卡背景點擊 → 關閉 ──
    private void EntryCardOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.Source == sender && DataContext is ScheduleViewModel vm)
        {
            vm.CloseEntryCardCommand.Execute(null);
            e.Handled = true;
        }
    }
}
