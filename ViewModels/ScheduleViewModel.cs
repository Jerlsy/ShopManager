using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using ShopManager.Services;
using System.Collections.ObjectModel;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace ShopManager.ViewModels;

public partial class ScheduleViewModel : ObservableObject
{
    private readonly MonthlyScheduleService _scheduleService;
    private readonly ScheduleService _entryService;
    private readonly ShiftSettingService _shiftService;
    private readonly EmployeeService _employeeService;
    private readonly ShopSettingService _shopSettingService;
    private readonly ISnackbarService _snackbarService;
    private readonly IContentDialogService _contentDialogService;

    public ScheduleViewModel(
        MonthlyScheduleService scheduleService,
        ScheduleService entryService,
        ShiftSettingService shiftService,
        EmployeeService employeeService,
        ShopSettingService shopSettingService,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService)
    {
        _scheduleService = scheduleService;
        _entryService = entryService;
        _shiftService = shiftService;
        _employeeService = employeeService;
        _shopSettingService = shopSettingService;
        _snackbarService = snackbarService;
        _contentDialogService = contentDialogService;
    }

    // ── 狀態 ──────────────────────────────────
    [ObservableProperty] private MonthlySchedule? _currentSchedule;
    [ObservableProperty] private int _selectedYear = DateTime.Today.Year;
    [ObservableProperty] private int _selectedMonth = DateTime.Today.Month;
    [ObservableProperty] private CalendarViewMode _viewMode = CalendarViewMode.Month;
    [ObservableProperty] private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    [ObservableProperty] private bool _isCreating; // 建立班表流程中
    [ObservableProperty] private bool _hasSchedule; // 當前月是否已有班表
    [ObservableProperty] private Employee? _selectedEmployee;

    // ── 建立班表用的暫存設定 ────────────────────
    [ObservableProperty] private int _createYear = DateTime.Today.Year;
    [ObservableProperty] private int _createMonth = DateTime.Today.Month;

    public ObservableCollection<DayOfWeekOption> CreateClosedDayOptions { get; } = new()
    {
        new(DayOfWeek.Monday, "周一"),
        new(DayOfWeek.Tuesday, "周二"),
        new(DayOfWeek.Wednesday, "周三"),
        new(DayOfWeek.Thursday, "周四"),
        new(DayOfWeek.Friday, "周五"),
        new(DayOfWeek.Saturday, "周六"),
        new(DayOfWeek.Sunday, "周日"),
    };

    // ── 資料源 ────────────────────────────────
    public ObservableCollection<ShiftSetting> EnabledShifts { get; } = new();
    public ObservableCollection<Employee> ActiveEmployees { get; } = new();
    public ObservableCollection<CalendarDay> CalendarDays { get; } = new();
    public ObservableCollection<CalendarTimeSlot> TimeSlots { get; } = new();

    // ── 視圖選項 ──────────────────────────────
    public static List<ViewModeOption> ViewModeOptions { get; } = new()
    {
        new(CalendarViewMode.Month, "月"),
        new(CalendarViewMode.Week, "周"),
        new(CalendarViewMode.Day, "日"),
    };

    public static List<int> AvailableYears { get; } =
        Enumerable.Range(DateTime.Today.Year - 1, 5).ToList();

    public static List<int> AvailableMonths { get; } =
        Enumerable.Range(1, 12).ToList();

    // ── 標題 ──────────────────────────────────
    public string CalendarTitle => $"{SelectedYear} 年 {SelectedMonth} 月";

    partial void OnSelectedYearChanged(int value) { OnPropertyChanged(nameof(CalendarTitle)); _ = LoadScheduleAsync(); }
    partial void OnSelectedMonthChanged(int value) { OnPropertyChanged(nameof(CalendarTitle)); _ = LoadScheduleAsync(); }
    partial void OnViewModeChanged(CalendarViewMode value) => BuildCalendarView();
    partial void OnSelectedDateChanged(DateOnly value) => BuildCalendarView();
    partial void OnSelectedEmployeeChanged(Employee? value) => BuildCalendarView();

    // ══════════════════════════════════════════
    // 初始載入
    // ══════════════════════════════════════════
    public async Task LoadAsync()
    {
        // 載入啟用中的班別
        var shifts = await _shiftService.GetAllAsync();
        EnabledShifts.Clear();
        foreach (var s in shifts.Where(s => s.IsEnabled))
            EnabledShifts.Add(s);

        // 載入在職員工
        var employees = await _employeeService.GetAllAsync();
        ActiveEmployees.Clear();
        foreach (var e in employees.Where(e => !e.IsResigned))
            ActiveEmployees.Add(e);

        await LoadScheduleAsync();
    }

    private async Task LoadScheduleAsync()
    {
        CurrentSchedule = await _scheduleService.GetAsync(SelectedYear, SelectedMonth);
        HasSchedule = CurrentSchedule is not null;
        BuildCalendarView();
    }

    // ══════════════════════════════════════════
    // 新增班表流程
    // ══════════════════════════════════════════
    [RelayCommand]
    public async Task StartCreateScheduleAsync()
    {
        CreateYear = SelectedYear;
        CreateMonth = SelectedMonth;

        // 帶入系統設定的店休日預設
        var settings = await _shopSettingService.GetAsync();
        foreach (var option in CreateClosedDayOptions)
            option.IsChecked = settings?.ClosedDaysOfWeek.Contains((int)option.Day) ?? false;

        IsCreating = true;
    }

    [RelayCommand]
    public void CancelCreate() => IsCreating = false;

    [RelayCommand]
    public async Task ConfirmCreateScheduleAsync()
    {
        if (await _scheduleService.ExistsAsync(CreateYear, CreateMonth))
            return; // 已存在，不重複建立

        var settings = await _shopSettingService.GetAsync() ?? new ShopSetting();

        // 用使用者調整後的店休日覆寫設定
        settings.ClosedDaysOfWeek = CreateClosedDayOptions
            .Where(o => o.IsChecked)
            .Select(o => (int)o.Day)
            .ToList();

        await _scheduleService.CreateAsync(CreateYear, CreateMonth, settings);
        IsCreating = false;

        SelectedYear = CreateYear;
        SelectedMonth = CreateMonth;
        await LoadScheduleAsync();
        _snackbarService.Show("建立成功", $"{CreateYear} 年 {CreateMonth} 月班表已建立",
            ControlAppearance.Success, null, TimeSpan.FromSeconds(3));
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
        SelectedYear = DateTime.Today.Year;
        SelectedMonth = DateTime.Today.Month;
        SelectedDate = DateOnly.FromDateTime(DateTime.Today);
    }

    // ══════════════════════════════════════════
    // 建構行事曆視圖資料
    // ══════════════════════════════════════════
    private void BuildCalendarView()
    {
        switch (ViewMode)
        {
            case CalendarViewMode.Month: BuildMonthView(); break;
            case CalendarViewMode.Week: BuildWeekView(); break;
            case CalendarViewMode.Day: BuildDayView(); break;
        }
    }

    private void BuildMonthView()
    {
        CalendarDays.Clear();
        if (CurrentSchedule is null) return;

        var daysInMonth = DateTime.DaysInMonth(SelectedYear, SelectedMonth);
        var firstDay = new DateOnly(SelectedYear, SelectedMonth, 1);
        var weekStart = CurrentSchedule.WeekStartDay; // 0=Sun, 1=Mon

        // 計算第一天前面需要幾個空格
        var firstDow = (int)firstDay.DayOfWeek;
        var offset = (firstDow - weekStart + 7) % 7;

        // 前面的空白天
        for (int i = 0; i < offset; i++)
            CalendarDays.Add(new CalendarDay { IsPlaceholder = true });

        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(SelectedYear, SelectedMonth, day);
            var isClosed = CurrentSchedule.ClosedDays.Contains(day);
            var isToday = date == DateOnly.FromDateTime(DateTime.Today);
            var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            var calDay = new CalendarDay
            {
                Date = date,
                Day = day,
                DayOfWeekText = GetDayOfWeekText(date.DayOfWeek),
                IsClosed = isClosed,
                IsToday = isToday,
                IsWeekend = isWeekend,
                IsSelected = date == SelectedDate,
            };

            // 如果不是店休日，填入所有啟用的班別色塊
            if (!isClosed)
            {
                foreach (var shift in EnabledShifts)
                {
                    var entries = CurrentSchedule.Entries
                        .Where(e => e.Date == date && e.ShiftSettingId == shift.Id)
                        .ToList();

                    calDay.ShiftBlocks.Add(new ShiftBlock
                    {
                        ShiftSetting = shift,
                        Date = date,
                        Employees = new ObservableCollection<Employee>(
                            entries.Select(e => e.Employee!).Where(e => e is not null)),
                        IsDisabled = IsShiftDisabledForEmployee(date, shift),
                    });
                }
            }

            CalendarDays.Add(calDay);
        }
    }

    private void BuildWeekView()
    {
        CalendarDays.Clear();
        TimeSlots.Clear();
        if (CurrentSchedule is null) return;

        var weekStart = CurrentSchedule.WeekStartDay;
        var selectedDow = (int)SelectedDate.DayOfWeek;
        var startOffset = (selectedDow - weekStart + 7) % 7;
        var weekStartDate = SelectedDate.AddDays(-startOffset);

        // 產生時間軸 (0~23 小時)
        for (int hour = 0; hour < 24; hour++)
        {
            var slot = new CalendarTimeSlot { Hour = hour, Label = $"{hour:D2}:00" };

            for (int d = 0; d < 7; d++)
            {
                var date = weekStartDate.AddDays(d);
                var isClosed = CurrentSchedule.ClosedDays.Contains(date.Day)
                    && date.Month == SelectedMonth && date.Year == SelectedYear;

                var daySlot = new DayTimeSlot { Date = date, Hour = hour, IsClosed = isClosed };

                if (!isClosed)
                {
                    foreach (var shift in EnabledShifts)
                    {
                        if (IsShiftInHour(shift, hour))
                        {
                            var entries = CurrentSchedule.Entries
                                .Where(e => e.Date == date && e.ShiftSettingId == shift.Id)
                                .ToList();

                            daySlot.ShiftBlocks.Add(new ShiftBlock
                            {
                                ShiftSetting = shift,
                                Date = date,
                                Employees = new ObservableCollection<Employee>(
                                    entries.Select(e => e.Employee!).Where(e => e is not null)),
                                IsDisabled = IsShiftDisabledForEmployee(date, shift),
                            });
                        }
                    }
                }
                slot.Days.Add(daySlot);
            }
            TimeSlots.Add(slot);
        }

        // 產生周的日期標題
        for (int d = 0; d < 7; d++)
        {
            var date = weekStartDate.AddDays(d);
            CalendarDays.Add(new CalendarDay
            {
                Date = date,
                Day = date.Day,
                DayOfWeekText = GetDayOfWeekText(date.DayOfWeek),
                IsToday = date == DateOnly.FromDateTime(DateTime.Today),
                IsSelected = date == SelectedDate,
                IsClosed = CurrentSchedule.ClosedDays.Contains(date.Day)
                    && date.Month == SelectedMonth && date.Year == SelectedYear,
            });
        }
    }

    private void BuildDayView()
    {
        TimeSlots.Clear();
        if (CurrentSchedule is null) return;

        var isClosed = CurrentSchedule.ClosedDays.Contains(SelectedDate.Day)
            && SelectedDate.Month == SelectedMonth && SelectedDate.Year == SelectedYear;

        for (int hour = 0; hour < 24; hour++)
        {
            var slot = new CalendarTimeSlot { Hour = hour, Label = $"{hour:D2}:00" };
            var daySlot = new DayTimeSlot { Date = SelectedDate, Hour = hour, IsClosed = isClosed };

            if (!isClosed)
            {
                foreach (var shift in EnabledShifts)
                {
                    if (IsShiftInHour(shift, hour))
                    {
                        var entries = CurrentSchedule.Entries
                            .Where(e => e.Date == SelectedDate && e.ShiftSettingId == shift.Id)
                            .ToList();

                        daySlot.ShiftBlocks.Add(new ShiftBlock
                        {
                            ShiftSetting = shift,
                            Date = SelectedDate,
                            Employees = new ObservableCollection<Employee>(
                                entries.Select(e => e.Employee!).Where(e => e is not null)),
                            IsDisabled = IsShiftDisabledForEmployee(SelectedDate, shift),
                        });
                    }
                }
            }
            slot.Days.Add(daySlot);
            TimeSlots.Add(slot);
        }
    }

    // ══════════════════════════════════════════
    // 拖放排班
    // ══════════════════════════════════════════
    public async Task DropEmployeeAsync(Employee employee, DateOnly date, ShiftSetting shift)
    {
        if (CurrentSchedule is null) return;

        // 檢查是否已排過
        var existing = CurrentSchedule.Entries
            .Any(e => e.EmployeeId == employee.Id && e.Date == date && e.ShiftSettingId == shift.Id);
        if (existing) return;

        var entry = new ScheduleEntry
        {
            MonthlyScheduleId = CurrentSchedule.Id,
            EmployeeId = employee.Id,
            Date = date,
            ShiftSettingId = shift.Id,
        };

        await _entryService.AddEntryAsync(entry);

        // 重新載入
        await LoadScheduleAsync();
    }

    public async Task RemoveEntryAsync(int entryId)
    {
        await _entryService.RemoveEntryAsync(entryId);
        await LoadScheduleAsync();
    }

    // ══════════════════════════════════════════
    // 排班規則檢查
    // ══════════════════════════════════════════
    private bool IsShiftDisabledForEmployee(DateOnly date, ShiftSetting shift)
    {
        if (SelectedEmployee is null) return false;
        var emp = SelectedEmployee;

        foreach (var rule in emp.ScheduleRules)
        {
            switch (rule.Type)
            {
                case ScheduleRuleType.FixedOff:
                    if (rule.FixedOffDays.Contains((int)date.DayOfWeek))
                        return true;
                    break;

                case ScheduleRuleType.ExcludeShift:
                    if (rule.ExcludedShiftIds.Contains(shift.Id))
                        return true;
                    break;

                case ScheduleRuleType.NotWith:
                    if (CurrentSchedule?.Entries.Any(e =>
                        e.Date == date && e.ShiftSettingId == shift.Id &&
                        rule.ExcludedColleagueIds.Contains(e.EmployeeId)) == true)
                        return true;
                    break;
            }
        }
        return false;
    }

    // ══════════════════════════════════════════
    // 工具方法
    // ══════════════════════════════════════════
    private static bool IsShiftInHour(ShiftSetting shift, int hour)
    {
        var startHour = shift.StartTime.Hour;
        var endHour = shift.EndTime.Hour;

        if (shift.EndTime > shift.StartTime)
            return hour >= startHour && hour < endHour;
        else // 跨日
            return hour >= startHour || hour < endHour;
    }

    private static string GetDayOfWeekText(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday => "一",
        DayOfWeek.Tuesday => "二",
        DayOfWeek.Wednesday => "三",
        DayOfWeek.Thursday => "四",
        DayOfWeek.Friday => "五",
        DayOfWeek.Saturday => "六",
        DayOfWeek.Sunday => "日",
        _ => ""
    };
}

// ══════════════════════════════════════════════
// 視圖模型輔助類別
// ══════════════════════════════════════════════

public enum CalendarViewMode { Month, Week, Day }

public record ViewModeOption(CalendarViewMode Value, string Label);

/// <summary>月視圖中的一天</summary>
public class CalendarDay
{
    public DateOnly Date { get; set; }
    public int Day { get; set; }
    public string DayOfWeekText { get; set; } = string.Empty;
    public bool IsClosed { get; set; }
    public bool IsToday { get; set; }
    public bool IsWeekend { get; set; }
    public bool IsSelected { get; set; }
    public bool IsPlaceholder { get; set; }
    public ObservableCollection<ShiftBlock> ShiftBlocks { get; } = new();
}

/// <summary>行事曆上的班別色塊</summary>
public class ShiftBlock
{
    public ShiftSetting ShiftSetting { get; set; } = null!;
    public DateOnly Date { get; set; }
    public ObservableCollection<Employee> Employees { get; set; } = new();
    public bool IsDisabled { get; set; }
}

/// <summary>時間軸一小時列</summary>
public class CalendarTimeSlot
{
    public int Hour { get; set; }
    public string Label { get; set; } = string.Empty;
    public ObservableCollection<DayTimeSlot> Days { get; } = new();
}

/// <summary>一天中的一個小時格</summary>
public class DayTimeSlot
{
    public DateOnly Date { get; set; }
    public int Hour { get; set; }
    public bool IsClosed { get; set; }
    public ObservableCollection<ShiftBlock> ShiftBlocks { get; } = new();
}
