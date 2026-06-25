using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;

namespace ShopManager.ViewModels;

public partial class ScheduleViewModel
{
    // ── 年月 / 日期切換回呼 ──────────────────────────────────────────────
    partial void OnSelectedYearChanged(int value)
    {
        OnPropertyChanged(nameof(CalendarTitle));
        ClearUndoStack();
        _ = LoadForMonthChangeAsync();
    }

    partial void OnSelectedMonthChanged(int value)
    {
        OnPropertyChanged(nameof(CalendarTitle));
        ClearUndoStack();
        _ = LoadForMonthChangeAsync();
    }

    partial void OnSelectedDateChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(CalendarTitle));
        BuildCalendarView();
    }

    partial void OnSelectedEmployeeChanged(Employee? value)
    {
        // 只更新 IsDisabled 旗標，不重建 CalendarDays（避免拖曳時的 UI 抖動）
        InvalidateEvalCacheForEmployee();
        foreach (var day in CalendarDays)
        {
            if (day.IsPlaceholder || day.IsClosed || day.IsOutOfScope) continue;
            foreach (var block in day.ShiftBlocks)
            {
                if (value is null)
                {
                    block.IsDisabled            = false;
                    block.DisabledReason        = string.Empty;
                    block.IsDisabledForCopy     = false;
                    block.DisabledReasonForCopy = string.Empty;
                }
                else
                {
                    var v     = EvaluateShiftForDrop(day.Date, block.ShiftSetting);
                    var vCopy = EvaluateShiftForDropCopy(day.Date, block.ShiftSetting);
                    block.IsDisabled            = v.IsBlocked;
                    block.DisabledReason        = v.Reason;
                    block.IsDisabledForCopy     = vCopy.IsBlocked;
                    block.DisabledReasonForCopy = vCopy.Reason;
                }
            }
        }
        // DayDetail 浮層的 group 也是 ShiftBlock，獨立更新
        if (IsDayDetailOpen && DayDetailDay is not null)
        {
            foreach (var block in DayDetailGroups)
            {
                if (value is null)
                {
                    block.IsDisabled            = false;
                    block.DisabledReason        = string.Empty;
                    block.IsDisabledForCopy     = false;
                    block.DisabledReasonForCopy = string.Empty;
                }
                else
                {
                    var v     = EvaluateShiftForDrop(DayDetailDay.Date, block.ShiftSetting);
                    var vCopy = EvaluateShiftForDropCopy(DayDetailDay.Date, block.ShiftSetting);
                    block.IsDisabled            = v.IsBlocked;
                    block.DisabledReason        = v.Reason;
                    block.IsDisabledForCopy     = vCopy.IsBlocked;
                    block.DisabledReasonForCopy = vCopy.Reason;
                }
            }
        }
    }

    // ══════════════════════════════════════════
    // 月份導覽
    // ══════════════════════════════════════════
    [RelayCommand]
    public void PreviousMonth()
    {
        if (SelectedMonth == 1) { SelectedYear--; SelectedMonth = 12; }
        else SelectedMonth--;
    }

    [RelayCommand]
    public void NextMonth()
    {
        if (SelectedMonth == 12) { SelectedYear++; SelectedMonth = 1; }
        else SelectedMonth++;
    }

    [RelayCommand]
    public void GoToToday()
    {
        SelectedYear  = DateTime.Today.Year;
        SelectedMonth = DateTime.Today.Month;
        SelectedDate  = DateOnly.FromDateTime(DateTime.Today);
    }
}
