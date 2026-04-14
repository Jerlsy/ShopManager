using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using ShopManager.Services;
using System.Collections.ObjectModel;

namespace ShopManager.ViewModels;

public partial class ScheduleViewModel : ObservableObject
{
    private readonly MonthlyScheduleService _scheduleService;
    private readonly ScheduleService _entryService;
    private readonly ShiftSettingService _shiftService;
    private readonly EmployeeService _employeeService;
    private readonly ShopSettingService _shopSettingService;
    private readonly IAppSnackbarService _snackbarService;

    public ScheduleViewModel(
        MonthlyScheduleService scheduleService,
        ScheduleService entryService,
        ShiftSettingService shiftService,
        EmployeeService employeeService,
        ShopSettingService shopSettingService,
        IAppSnackbarService snackbarService)
    {
        _scheduleService = scheduleService;
        _entryService = entryService;
        _shiftService = shiftService;
        _employeeService = employeeService;
        _shopSettingService = shopSettingService;
        _snackbarService = snackbarService;
    }

    // ── 狀態 ──────────────────────────────────
    [ObservableProperty] private MonthlySchedule? _currentSchedule;
    [ObservableProperty] private int _selectedYear = DateTime.Today.Year;
    [ObservableProperty] private int _selectedMonth = DateTime.Today.Month;
    [ObservableProperty] private CalendarViewMode _viewMode = CalendarViewMode.Month;
    [ObservableProperty] private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    [ObservableProperty] private bool _isCreating;
    [ObservableProperty] private bool _hasSchedule;
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

    // ══════════════════════════════════════════
    // 功能一：快速新增（點擊格子）
    // ══════════════════════════════════════════
    [ObservableProperty] private bool _isQuickAdding;
    [ObservableProperty] private DateOnly _quickAddDate;
    [ObservableProperty] private Employee? _quickAddEmployee;
    [ObservableProperty] private ShiftSetting? _quickAddShift;

    [RelayCommand]
    public void OpenQuickAdd(CalendarDay day)
    {
        if (day.IsPlaceholder || day.IsClosed) return;
        if (CurrentSchedule is null) return;

        QuickAddDate = day.Date;
        QuickAddEmployee = ActiveEmployees.FirstOrDefault();
        QuickAddShift = EnabledShifts.FirstOrDefault();
        IsCreating = false;
        IsBatchMode = false;
        IsQuickAdding = true;
    }

    [RelayCommand]
    public void CancelQuickAdd() => IsQuickAdding = false;

    [RelayCommand]
    public async Task ConfirmQuickAddAsync()
    {
        if (CurrentSchedule is null || QuickAddEmployee is null || QuickAddShift is null) return;

        var existing = CurrentSchedule.Entries.Any(e =>
            e.EmployeeId == QuickAddEmployee.Id &&
            e.Date == QuickAddDate &&
            e.ShiftSettingId == QuickAddShift.Id);

        if (existing)
        {
            _snackbarService.ShowError("該日期已有相同排班");
            return;
        }

        await _entryService.AddEntryAsync(new ScheduleEntry
        {
            MonthlyScheduleId = CurrentSchedule.Id,
            EmployeeId = QuickAddEmployee.Id,
            Date = QuickAddDate,
            ShiftSettingId = QuickAddShift.Id,
        });

        IsQuickAdding = false;
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess($"已新增 {QuickAddEmployee.Name} {QuickAddDate:MM/dd} {QuickAddShift.Alias}");
    }

    // ══════════════════════════════════════════
    // 功能二：右鍵選單操作
    // ══════════════════════════════════════════

    [RelayCommand]
    public async Task DeleteEntryAsync(int entryId)
    {
        await _entryService.RemoveEntryAsync(entryId);
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess("排班已刪除");
    }

    [RelayCommand]
    public async Task CopyToNextWeekAsync(int entryId)
    {
        if (CurrentSchedule is null) return;

        var entry = CurrentSchedule.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry is null) return;

        var targetDate = entry.Date.AddDays(7);
        var targetSchedule = targetDate.Year == SelectedYear && targetDate.Month == SelectedMonth
            ? CurrentSchedule
            : await _scheduleService.GetAsync(targetDate.Year, targetDate.Month);

        if (targetSchedule is null)
        {
            _snackbarService.ShowError($"{targetDate.Year}/{targetDate.Month} 班表不存在，請先建立");
            return;
        }

        var exists = targetSchedule.Entries.Any(e =>
            e.EmployeeId == entry.EmployeeId &&
            e.Date == targetDate &&
            e.ShiftSettingId == entry.ShiftSettingId);

        if (exists)
        {
            _snackbarService.ShowError("目標日期已有相同排班");
            return;
        }

        await _entryService.AddEntryAsync(new ScheduleEntry
        {
            MonthlyScheduleId = targetSchedule.Id,
            EmployeeId = entry.EmployeeId,
            Date = targetDate,
            ShiftSettingId = entry.ShiftSettingId,
        });

        await LoadScheduleAsync();
        _snackbarService.ShowSuccess($"已複製到 {targetDate:MM/dd}");
    }

    // ── 功能二補充：編輯排班 ─────────────────────
    [ObservableProperty] private bool _isEditEntryOpen;
    [ObservableProperty] private string _editEntryInfo = string.Empty;
    [ObservableProperty] private ShiftSetting? _editEntryShift;
    [ObservableProperty] private string _editEntryNote = string.Empty;
    private int _editEntryId;

    [RelayCommand]
    public void OpenEditEntry(int entryId)
    {
        if (CurrentSchedule is null) return;
        var entry = CurrentSchedule.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry is null) return;

        _editEntryId = entryId;
        EditEntryInfo = $"{entry.Employee?.Name} — {entry.Date:MM/dd}（{GetDayOfWeekText(entry.Date.DayOfWeek)}）";
        EditEntryShift = EnabledShifts.FirstOrDefault(s => s.Id == entry.ShiftSettingId)
                         ?? EnabledShifts.FirstOrDefault();
        EditEntryNote = entry.Note ?? string.Empty;
        IsEditEntryOpen = true;
        IsQuickAdding = false;
        IsBatchMode = false;
        IsCreating = false;
    }

    [RelayCommand]
    public async Task ConfirmEditEntryAsync()
    {
        if (EditEntryShift is null) return;
        await _entryService.UpdateEntryAsync(_editEntryId, EditEntryShift.Id, EditEntryNote);
        IsEditEntryOpen = false;
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess("排班已更新");
    }

    [RelayCommand]
    public void CancelEditEntry() => IsEditEntryOpen = false;

    // ══════════════════════════════════════════
    // 功能三：批次分配
    // ══════════════════════════════════════════
    [ObservableProperty] private bool _isBatchMode;
    [ObservableProperty] private Employee? _batchEmployee;
    [ObservableProperty] private ShiftSetting? _batchShift;
    [ObservableProperty] private DateTime? _batchStartDate = DateTime.Today;
    [ObservableProperty] private DateTime? _batchEndDate = DateTime.Today.AddDays(6);

    public ObservableCollection<DayOfWeekOption> BatchWeekdayOptions { get; } = new()
    {
        new(DayOfWeek.Monday,    "周一") { IsChecked = true },
        new(DayOfWeek.Tuesday,   "周二") { IsChecked = true },
        new(DayOfWeek.Wednesday, "周三") { IsChecked = true },
        new(DayOfWeek.Thursday,  "周四") { IsChecked = true },
        new(DayOfWeek.Friday,    "周五") { IsChecked = true },
        new(DayOfWeek.Saturday,  "周六"),
        new(DayOfWeek.Sunday,    "周日"),
    };

    [RelayCommand]
    public void StartBatch()
    {
        BatchEmployee = ActiveEmployees.FirstOrDefault();
        BatchShift = EnabledShifts.FirstOrDefault();
        BatchStartDate = new DateTime(SelectedYear, SelectedMonth, 1);
        BatchEndDate = new DateTime(SelectedYear, SelectedMonth,
            DateTime.DaysInMonth(SelectedYear, SelectedMonth));
        IsCreating = false;
        IsQuickAdding = false;
        IsBatchMode = true;
    }

    [RelayCommand]
    public void CancelBatch() => IsBatchMode = false;

    [RelayCommand]
    public async Task ConfirmBatchAsync()
    {
        if (BatchEmployee is null || BatchShift is null ||
            BatchStartDate is null || BatchEndDate is null) return;

        var start = DateOnly.FromDateTime(BatchStartDate.Value);
        var end = DateOnly.FromDateTime(BatchEndDate.Value);
        if (start > end)
        {
            _snackbarService.ShowError("起始日期不能晚於結束日期");
            return;
        }

        var selectedDays = BatchWeekdayOptions
            .Where(o => o.IsChecked)
            .Select(o => o.Day)
            .ToHashSet();

        if (!selectedDays.Any())
        {
            _snackbarService.ShowError("請至少選擇一個星期日");
            return;
        }

        // 按年月分組載入需要的班表
        var scheduleCache = new Dictionary<(int year, int month), MonthlySchedule?>();
        var entriesToAdd = new List<ScheduleEntry>();

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (!selectedDays.Contains(date.DayOfWeek)) continue;

            var key = (date.Year, date.Month);
            if (!scheduleCache.TryGetValue(key, out var schedule))
            {
                schedule = await _scheduleService.GetAsync(date.Year, date.Month);
                scheduleCache[key] = schedule;
            }
            if (schedule is null) continue;

            // 如果該日是店休日，跳過
            if (schedule.ClosedDays.Contains(date.Day)) continue;

            entriesToAdd.Add(new ScheduleEntry
            {
                MonthlyScheduleId = schedule.Id,
                EmployeeId = BatchEmployee.Id,
                Date = date,
                ShiftSettingId = BatchShift.Id,
            });
        }

        var added = await _entryService.AddEntriesAsync(entriesToAdd);
        IsBatchMode = false;
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess(
            $"已新增 {added} 筆排班（略過 {entriesToAdd.Count - added} 筆重複）");
    }

    // ══════════════════════════════════════════
    // 功能四：複製上週班表
    // ══════════════════════════════════════════
    [RelayCommand]
    public async Task CopyLastWeekAsync()
    {
        if (CurrentSchedule is null) return;

        var weekStart = GetCurrentWeekStart();
        var prevWeekStart = weekStart.AddDays(-7);
        var prevWeekEnd = weekStart.AddDays(-1);

        var prevEntries = await _entryService.GetEntriesByDateRangeAsync(prevWeekStart, prevWeekEnd);

        if (!prevEntries.Any())
        {
            _snackbarService.ShowError("上週無任何排班可複製");
            return;
        }

        var scheduleCache = new Dictionary<(int year, int month), MonthlySchedule?>();
        var entriesToAdd = new List<ScheduleEntry>();

        foreach (var entry in prevEntries)
        {
            var targetDate = entry.Date.AddDays(7);
            var key = (targetDate.Year, targetDate.Month);

            if (!scheduleCache.TryGetValue(key, out var targetSchedule))
            {
                targetSchedule = await _scheduleService.GetAsync(targetDate.Year, targetDate.Month);
                scheduleCache[key] = targetSchedule;
            }
            if (targetSchedule is null) continue;

            entriesToAdd.Add(new ScheduleEntry
            {
                MonthlyScheduleId = targetSchedule.Id,
                EmployeeId = entry.EmployeeId,
                Date = targetDate,
                ShiftSettingId = entry.ShiftSettingId,
            });
        }

        var added = await _entryService.AddEntriesAsync(entriesToAdd);
        await LoadScheduleAsync();

        if (added == 0)
            _snackbarService.ShowError("本週已有相同排班，無需複製");
        else
            _snackbarService.ShowSuccess($"已複製 {added} 筆排班到本週");
    }

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
        var shifts = await _shiftService.GetAllAsync();
        EnabledShifts.Clear();
        foreach (var s in shifts.Where(s => s.IsEnabled))
            EnabledShifts.Add(s);

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

        var settings = await _shopSettingService.GetAsync();
        foreach (var option in CreateClosedDayOptions)
            option.IsChecked = settings?.ClosedDaysOfWeek.Contains((int)option.Day) ?? false;

        IsQuickAdding = false;
        IsBatchMode = false;
        IsCreating = true;
    }

    [RelayCommand]
    public void CancelCreate() => IsCreating = false;

    [RelayCommand]
    public async Task ConfirmCreateScheduleAsync()
    {
        if (await _scheduleService.ExistsAsync(CreateYear, CreateMonth)) return;

        var settings = await _shopSettingService.GetAsync() ?? new ShopSetting();
        settings.ClosedDaysOfWeek = CreateClosedDayOptions
            .Where(o => o.IsChecked)
            .Select(o => (int)o.Day)
            .ToList();

        await _scheduleService.CreateAsync(CreateYear, CreateMonth, settings);
        IsCreating = false;

        SelectedYear = CreateYear;
        SelectedMonth = CreateMonth;
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess($"{CreateYear} 年 {CreateMonth} 月班表已建立");
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
        var weekStart = CurrentSchedule.WeekStartDay;

        var firstDow = (int)firstDay.DayOfWeek;
        var offset = (firstDow - weekStart + 7) % 7;

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
                        EntryItems = new ObservableCollection<EntryItem>(
                            entries
                                .Where(e => e.Employee is not null)
                                .Select(e => new EntryItem { EntryId = e.Id, Employee = e.Employee! })),
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
                                EntryItems = new ObservableCollection<EntryItem>(
                                    entries
                                        .Where(e => e.Employee is not null)
                                        .Select(e => new EntryItem { EntryId = e.Id, Employee = e.Employee! })),
                                IsDisabled = IsShiftDisabledForEmployee(date, shift),
                            });
                        }
                    }
                }
                slot.Days.Add(daySlot);
            }
            TimeSlots.Add(slot);
        }

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
                            EntryItems = new ObservableCollection<EntryItem>(
                                entries
                                    .Where(e => e.Employee is not null)
                                    .Select(e => new EntryItem { EntryId = e.Id, Employee = e.Employee! })),
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
                    if (rule.FixedOffDays.Contains((int)date.DayOfWeek)) return true;
                    break;
                case ScheduleRuleType.ExcludeShift:
                    if (rule.ExcludedShiftIds.Contains(shift.Id)) return true;
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
    private DateOnly GetCurrentWeekStart()
    {
        var weekStart = CurrentSchedule?.WeekStartDay ?? 1;
        var selectedDow = (int)SelectedDate.DayOfWeek;
        var offset = (selectedDow - weekStart + 7) % 7;
        return SelectedDate.AddDays(-offset);
    }

    private static bool IsShiftInHour(ShiftSetting shift, int hour)
    {
        var startHour = shift.StartTime.Hour;
        var endHour = shift.EndTime.Hour;
        if (shift.EndTime > shift.StartTime)
            return hour >= startHour && hour < endHour;
        else
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

/// <summary>排班記錄顯示單位（含 EntryId 供右鍵操作使用）</summary>
public class EntryItem
{
    public int EntryId { get; set; }
    public Employee Employee { get; set; } = null!;
}

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

public class ShiftBlock
{
    public ShiftSetting ShiftSetting { get; set; } = null!;
    public DateOnly Date { get; set; }
    public ObservableCollection<EntryItem> EntryItems { get; set; } = new();
    public bool IsDisabled { get; set; }
}

public class CalendarTimeSlot
{
    public int Hour { get; set; }
    public string Label { get; set; } = string.Empty;
    public ObservableCollection<DayTimeSlot> Days { get; } = new();
}

public class DayTimeSlot
{
    public DateOnly Date { get; set; }
    public int Hour { get; set; }
    public bool IsClosed { get; set; }
    public ObservableCollection<ShiftBlock> ShiftBlocks { get; } = new();
}
