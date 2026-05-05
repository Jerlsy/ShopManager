using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;

namespace ShopManager.ViewModels;

public partial class ScheduleViewModel
{
    // ── 年月 / 視圖切換回呼 ──────────────────────────────────────────────
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

    partial void OnViewModeChanged(CalendarViewMode value)
    {
        OnPropertyChanged(nameof(IsMonthView));
        OnPropertyChanged(nameof(IsWeekView));
        OnPropertyChanged(nameof(IsDayView));
        OnPropertyChanged(nameof(CalendarTitle));
        BuildCalendarView();
    }

    partial void OnDayViewDayChanged(CalendarDay? value)
    {
        OnPropertyChanged(nameof(DayViewDayIsClosed));
        OnPropertyChanged(nameof(DayViewDayHasOverride));
    }

    public bool DayViewDayIsClosed    => DayViewDay?.IsClosed ?? false;
    public bool DayViewDayHasOverride =>
        DayViewDay is not null &&
        CurrentSchedule?.ShiftDateOverrides.Any(o => o.Day == DayViewDay.Date.Day) == true;

    partial void OnSelectedDateChanged(DateOnly value)
    {
        OnPropertyChanged(nameof(CalendarTitle));
        BuildCalendarView();
    }

    partial void OnSelectedEmployeeChanged(Employee? value)
    {
        BuildCalendarView();
        if (IsDayDetailOpen && DayDetailDay is not null)
            RebuildDayDetailGroups(DayDetailDay);
    }

    // ── 視圖模式旗標 ─────────────────────────────────────────────────────
    public bool IsMonthView => ViewMode == CalendarViewMode.Month;
    public bool IsWeekView  => ViewMode == CalendarViewMode.Week;
    public bool IsDayView   => ViewMode == CalendarViewMode.Day;

    [RelayCommand] public void SetMonthView() => ViewMode = CalendarViewMode.Month;
    [RelayCommand] public void SetWeekView()  => ViewMode = CalendarViewMode.Week;
    [RelayCommand] public void SetDayView()   => ViewMode = CalendarViewMode.Day;

    // ══════════════════════════════════════════
    // 導覽（月 / 周 / 日 共用同一對按鈕）
    // ══════════════════════════════════════════
    [RelayCommand]
    public void PreviousMonth()
    {
        switch (ViewMode)
        {
            case CalendarViewMode.Week:
                NavigateToDate(SelectedDate.AddDays(-7));
                break;
            case CalendarViewMode.Day:
                NavigateToDate(SelectedDate.AddDays(-1));
                break;
            default:
                if (SelectedMonth == 1) { SelectedYear--; SelectedMonth = 12; }
                else SelectedMonth--;
                break;
        }
    }

    [RelayCommand]
    public void NextMonth()
    {
        switch (ViewMode)
        {
            case CalendarViewMode.Week:
                NavigateToDate(SelectedDate.AddDays(7));
                break;
            case CalendarViewMode.Day:
                NavigateToDate(SelectedDate.AddDays(1));
                break;
            default:
                if (SelectedMonth == 12) { SelectedYear++; SelectedMonth = 1; }
                else SelectedMonth++;
                break;
        }
    }

    private void NavigateToDate(DateOnly date)
    {
        if (date.Year != SelectedYear) SelectedYear = date.Year;
        if (date.Month != SelectedMonth) SelectedMonth = date.Month;
        SelectedDate = date;
    }

    [RelayCommand]
    public void GoToToday()
    {
        SelectedYear  = DateTime.Today.Year;
        SelectedMonth = DateTime.Today.Month;
        SelectedDate  = DateOnly.FromDateTime(DateTime.Today);
    }
}
