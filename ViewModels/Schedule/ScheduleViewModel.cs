using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ShopManager.Models;
using ShopManager.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;

namespace ShopManager.ViewModels;

public partial class ScheduleViewModel : ObservableObject
{
    // ── 服務 ──────────────────────────────────────────────────────────────
    private readonly MonthlyScheduleService _scheduleService;
    private readonly ScheduleService _entryService;
    private readonly ShiftSettingService _shiftService;
    private readonly EmployeeService _employeeService;
    private readonly ShopSettingService _shopSettingService;
    private readonly SalarySettingService _salaryService;
    private readonly ScheduleConflictService _conflictService;
    private readonly IAppSnackbarService _snackbarService;
    private readonly AutoScheduleService _autoScheduleService;
    private readonly HttpClient _http;
    private LaborLawSetting? _laborLaw;

    public ScheduleViewModel(
        MonthlyScheduleService scheduleService,
        ScheduleService        entryService,
        ShiftSettingService    shiftService,
        EmployeeService        employeeService,
        ShopSettingService     shopSettingService,
        SalarySettingService   salaryService,
        ScheduleConflictService conflictService,
        IAppSnackbarService    snackbarService,
        AutoScheduleService    autoScheduleService,
        HttpClient             http)
    {
        _scheduleService     = scheduleService;
        _entryService        = entryService;
        _shiftService        = shiftService;
        _employeeService     = employeeService;
        _shopSettingService  = shopSettingService;
        _salaryService       = salaryService;
        _conflictService     = conflictService;
        _snackbarService     = snackbarService;
        _autoScheduleService = autoScheduleService;
        _http                = http;

        foreach (var opt in CreateClosedDayOptions)
            opt.PropertyChanged += OnClosedDayOptionChanged;

        WeakReferenceMessenger.Default.Register<ShiftSettingChangedMessage>(this, async (_, _) =>
        {
            var shifts = await _shiftService.GetAllAsync();
            EnabledShifts.Clear();
            foreach (var s in shifts.Where(s => s.IsEnabled))
                EnabledShifts.Add(s);
            await LoadScheduleAsync();
        });

        ShiftDayAssignments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasShiftAssignments));
            OnPropertyChanged(nameof(HasMoreShiftsToAdd));
        };

        WorkDayConditions.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(HasWorkDayConditions));

        EmployeeConstraints.CollectionChanged += (_, e) =>
        {
            OnPropertyChanged(nameof(HasEmployeeConstraints));
            if (e.NewItems is not null)
                foreach (EmployeeConstraintItem item in e.NewItems)
                    item.PropertyChanged += OnConstraintItemPropertyChanged;
            if (e.OldItems is not null)
                foreach (EmployeeConstraintItem item in e.OldItems)
                    item.PropertyChanged -= OnConstraintItemPropertyChanged;
            RefreshConstraintAvailableTypes();
        };

        EmployeeDetailDayOffs.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(AvailableEmployeeDetailDays));
    }

    // ── 集合變更事件處理 ──────────────────────────────────────────────────
    private void OnClosedDayOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DayOfWeekOption.IsChecked)) return;
        if (sender is not DayOfWeekOption option) return;

        bool anyUnchecked = false;
        foreach (var assignment in ShiftDayAssignments)
        {
            var cell = assignment.DayCells.FirstOrDefault(c => c.Day == option.Day);
            if (cell is null) continue;
            cell.IsShopClosed = option.IsChecked;
            if (option.IsChecked && cell.IsChecked)
            {
                cell.IsChecked = false;
                anyUnchecked = true;
            }
        }
        foreach (var cond in WorkDayConditions)
        {
            var cell = cond.DayCells.FirstOrDefault(c => c.Day == option.Day);
            if (cell is null) continue;
            cell.IsShopClosed = option.IsChecked;
            if (option.IsChecked && cell.IsChecked)
            {
                cell.IsChecked = false;
                anyUnchecked = true;
            }
        }
        if (anyUnchecked) { HasShiftDayWarning = true; RefreshWorkDayConditionUsed(); }
    }

    private void OnConstraintItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EmployeeConstraintItem.SelectedEmployee) &&
            sender is EmployeeConstraintItem changedItem &&
            changedItem.SelectedEmployee is not null)
        {
            var othersTypes = EmployeeConstraints
                .Where(c => c != changedItem && c.SelectedEmployee?.Id == changedItem.SelectedEmployee.Id)
                .Select(c => c.ConstraintType)
                .ToHashSet();

            bool groupAUsed = othersTypes.Contains(EmployeeConstraintType.DayOff) ||
                              othersTypes.Contains(EmployeeConstraintType.WorkDay);
            bool groupBUsed = othersTypes.Contains(EmployeeConstraintType.ShiftPriority) ||
                              othersTypes.Contains(EmployeeConstraintType.ExcludeAutoAssign);

            bool currentIsGroupA = changedItem.ConstraintType is EmployeeConstraintType.DayOff
                                                              or EmployeeConstraintType.WorkDay;

            if (currentIsGroupA && groupAUsed && !groupBUsed)
                changedItem.ConstraintType = EmployeeConstraintType.ShiftPriority;
            else if (!currentIsGroupA && groupBUsed && !groupAUsed)
                changedItem.ConstraintType = EmployeeConstraintType.DayOff;
        }

        if (e.PropertyName is nameof(EmployeeConstraintItem.SelectedEmployee)
                           or nameof(EmployeeConstraintItem.ConstraintType))
            RefreshConstraintAvailableTypes();
    }

    private void RefreshConstraintAvailableTypes()
    {
        var allTypes = new[]
        {
            EmployeeConstraintType.DayOff,
            EmployeeConstraintType.WorkDay,
            EmployeeConstraintType.ShiftPriority,
            EmployeeConstraintType.ExcludeAutoAssign,
        };

        foreach (var item in EmployeeConstraints)
        {
            var available = allTypes.ToList();

            if (item.SelectedEmployee is not null)
            {
                var empId      = item.SelectedEmployee.Id;
                var othersTypes = EmployeeConstraints
                    .Where(c => c != item && c.SelectedEmployee?.Id == empId)
                    .Select(c => c.ConstraintType)
                    .ToHashSet();

                if (othersTypes.Contains(EmployeeConstraintType.DayOff) ||
                    othersTypes.Contains(EmployeeConstraintType.WorkDay))
                {
                    available.Remove(EmployeeConstraintType.DayOff);
                    available.Remove(EmployeeConstraintType.WorkDay);
                }
                if (othersTypes.Contains(EmployeeConstraintType.ShiftPriority) ||
                    othersTypes.Contains(EmployeeConstraintType.ExcludeAutoAssign))
                {
                    available.Remove(EmployeeConstraintType.ShiftPriority);
                    available.Remove(EmployeeConstraintType.ExcludeAutoAssign);
                }
            }

            // 用增量更新（而非 Clear + Add）以避免 ComboBox SelectedItem 在清空瞬間遺失
            foreach (var t in item.AvailableConstraintTypes.Except(available).ToList())
                item.AvailableConstraintTypes.Remove(t);
            foreach (var t in available)
                if (!item.AvailableConstraintTypes.Contains(t))
                    item.AvailableConstraintTypes.Add(t);
        }
    }

    // ══════════════════════════════════════════
    // 核心狀態
    // ══════════════════════════════════════════
    [ObservableProperty] private MonthlySchedule? _currentSchedule;
    [ObservableProperty] private int _selectedYear  = DateTime.Today.Year;
    [ObservableProperty] private int _selectedMonth = DateTime.Today.Month;
    [ObservableProperty] private CalendarViewMode _viewMode = CalendarViewMode.Month;
    [ObservableProperty] private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAutoAssignButton))]
    [NotifyPropertyChangedFor(nameof(IsViewingCalendar))]
    private bool _isCreating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAutoAssignButton))]
    private bool _hasSchedule;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAutoAssignButton))]
    [NotifyPropertyChangedFor(nameof(IsViewingCalendar))]
    private bool _isAutoAssigning;

    public bool ShowAutoAssignButton => HasSchedule && !IsCreating && !IsAutoAssigning;
    public bool IsViewingCalendar    => !IsCreating && !IsAutoAssigning;

    [ObservableProperty] private Employee? _selectedEmployee;

    // ── DayDetail / EntryCard 跨方法共用狀態 ─────────────────────────────
    [ObservableProperty] private bool _isDayDetailOpen;
    [ObservableProperty] private string _dayDetailTitle = string.Empty;
    [ObservableProperty] private double _timeGridHeight  = 480;
    [ObservableProperty] private CalendarDay? _dayViewDay;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DayDetailIsClosed))]
    [NotifyPropertyChangedFor(nameof(DayDetailDayHasOverride))]
    [NotifyPropertyChangedFor(nameof(ShowDayDetailShiftGroups))]
    [NotifyPropertyChangedFor(nameof(ShowDayDetailClosedMessage))]
    private CalendarDay? _dayDetailDay;

    // ── 衝突計數（LoadScheduleAsync 寫入）────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflicts))]
    private int _conflictCount;
    public bool HasConflicts => ConflictCount > 0;

    // ══════════════════════════════════════════
    // 還原（Undo）
    // ══════════════════════════════════════════
    private record UndoAction(string Description, Func<Task> Execute);
    private readonly Stack<UndoAction> _undoStack = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UndoDescription))]
    private bool _canUndo;

    public string UndoDescription =>
        _undoStack.TryPeek(out var top) ? $"還原：{top.Description}" : string.Empty;

    private void PushUndo(UndoAction action)
    {
        _undoStack.Push(action);
        CanUndo = true;
        OnPropertyChanged(nameof(UndoDescription));
    }

    private void ClearUndoStack()
    {
        _undoStack.Clear();
        CanUndo = false;
    }

    [RelayCommand]
    private async Task UndoAsync()
    {
        if (!_undoStack.TryPop(out var action)) return;
        CanUndo = _undoStack.Count > 0;
        OnPropertyChanged(nameof(UndoDescription));
        await action.Execute();
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess($"已還原：{action.Description}");
    }

    // ══════════════════════════════════════════
    // 資料源與視圖選項
    // ══════════════════════════════════════════
    public ObservableCollection<ShiftSetting>    EnabledShifts  { get; } = new();
    public ObservableCollection<Employee>        ActiveEmployees { get; } = new();
    public ObservableCollection<CalendarDay>     CalendarDays   { get; } = new();
    public ObservableCollection<CalendarWeekRow> CalendarWeeks  { get; } = new();
    public ObservableCollection<CalendarTimeSlot> TimeSlots     { get; } = new();
    public ObservableCollection<EmployeeWorkloadItem> EmployeeWorkloads { get; } = new();
    public List<string> DayHeaders { get; private set; } = new();
    private Dictionary<int, string> _monthHolidays = new();

    public static List<ViewModeOption> ViewModeOptions { get; } = new()
    {
        new(CalendarViewMode.Month, "月"),
        new(CalendarViewMode.Week,  "周"),
        new(CalendarViewMode.Day,   "日"),
    };

    public static List<int> AvailableYears  { get; } = Enumerable.Range(DateTime.Today.Year - 1, 5).ToList();
    public static List<int> AvailableMonths { get; } = Enumerable.Range(1, 12).ToList();

    public string CalendarTitle => ViewMode switch
    {
        CalendarViewMode.Day  => $"{SelectedDate:yyyy年M月d日}（{GetDayOfWeekText(SelectedDate.DayOfWeek)}）",
        CalendarViewMode.Week => GetWeekRangeTitle(),
        _                     => $"{SelectedYear} 年 {SelectedMonth} 月",
    };

    private string GetWeekRangeTitle()
    {
        var s = GetCurrentWeekStart();
        var e = s.AddDays(6);
        return s.Month == e.Month
            ? $"{s:yyyy年M月d日} – {e.Day}日"
            : $"{s:yyyy年M月d日} – {e:M月d日}";
    }

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

        _laborLaw = await _salaryService.GetLaborLawAsync();

        await LoadForMonthChangeAsync();
    }

    // 月份切換或初次載入：先抓假日（一次性 HTTP），再重建排班視圖
    private async Task LoadForMonthChangeAsync()
    {
        await LoadMonthHolidaysAsync();
        await LoadScheduleAsync();
    }

    // 排班操作後的快速重整：只查 DB，不重抓假日（已快取在 _monthHolidays）
    private async Task LoadScheduleAsync()
    {
        CurrentSchedule = await _scheduleService.GetAsync(SelectedYear, SelectedMonth);
        HasSchedule = CurrentSchedule is not null;
        BuildCalendarView();
        if (IsDayDetailOpen && DayDetailDay is not null)
            RebuildDayDetailGroups(DayDetailDay);
        ConflictCount = CurrentSchedule is not null
            ? await _conflictService.GetCountAsync(CurrentSchedule.Id)
            : 0;
        RebuildEmployeeWorkloads();
    }

    private void RebuildEmployeeWorkloads()
    {
        var countById = CurrentSchedule?.Entries
            .GroupBy(e => e.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Count()) ?? new();

        EmployeeWorkloads.Clear();

        if (CurrentSchedule is null)
        {
            foreach (var emp in ActiveEmployees)
                EmployeeWorkloads.Add(new EmployeeWorkloadItem { Employee = emp });
            return;
        }

        var daysInMonth = DateTime.DaysInMonth(CurrentSchedule.Year, CurrentSchedule.Month);
        var closedDays  = CurrentSchedule.ClosedDays.ToHashSet();

        foreach (var emp in ActiveEmployees)
        {
            var fixedOffDows = emp.ScheduleRules
                .Where(r => r.Type == ScheduleRuleType.FixedOff)
                .SelectMany(r => r.FixedOffDays)
                .ToHashSet();

            int availDays = 0;
            for (int d = 1; d <= daysInMonth; d++)
            {
                if (closedDays.Contains(d)) continue;
                var date = new DateOnly(CurrentSchedule.Year, CurrentSchedule.Month, d);
                if (fixedOffDows.Contains((int)date.DayOfWeek)) continue;
                availDays++;
            }

            EmployeeWorkloads.Add(new EmployeeWorkloadItem
            {
                Employee    = emp,
                ShiftCount  = countById.GetValueOrDefault(emp.Id, 0),
                TargetCount = availDays,
            });
        }
    }

    private async Task LoadMonthHolidaysAsync()
    {
        _monthHolidays.Clear();
        try
        {
            var url  = $"https://cdn.jsdelivr.net/gh/ruyut/TaiwanCalendar/data/{SelectedYear}.json";
            var json = await _http.GetStringAsync(url);
            var all  = JsonSerializer.Deserialize<List<CalendarDayDto>>(json);
            if (all is null) return;
            var prefix = $"{SelectedYear}{SelectedMonth:D2}";
            foreach (var d in all.Where(d =>
                d.Date.StartsWith(prefix) && d.IsHoliday && !string.IsNullOrEmpty(d.Description)))
            {
                _monthHolidays[int.Parse(d.Date[6..])] = d.Description;
            }
        }
        catch { }
    }

    // ══════════════════════════════════════════
    // 建構行事曆視圖資料
    // ══════════════════════════════════════════
    private void BuildCalendarView()
    {
        _shiftLookupCache = EnabledShifts.ToDictionary(s => s.Id);
        _evalDropCache.Clear();
        _evalCopyCache.Clear();
        switch (ViewMode)
        {
            case CalendarViewMode.Month: BuildMonthView(); break;
            case CalendarViewMode.Week:  BuildWeekView();  break;
            case CalendarViewMode.Day:   BuildDayView();   break;
        }
    }

    private void BuildMonthView()
    {
        CalendarDays.Clear();
        CalendarWeeks.Clear();
        if (CurrentSchedule is null) return;

        var daysInMonth = DateTime.DaysInMonth(SelectedYear, SelectedMonth);
        var firstDay    = new DateOnly(SelectedYear, SelectedMonth, 1);
        var weekStart   = CurrentSchedule.WeekStartDay;

        var dayNames = new[] { "日", "一", "二", "三", "四", "五", "六" };
        DayHeaders = Enumerable.Range(0, 7).Select(i => dayNames[(weekStart + i) % 7]).ToList();
        OnPropertyChanged(nameof(DayHeaders));

        var firstDow = (int)firstDay.DayOfWeek;
        var offset   = (firstDow - weekStart + 7) % 7;

        var allCells = new List<CalendarDay>();
        for (int i = 0; i < offset; i++)
            allCells.Add(new CalendarDay { IsPlaceholder = true });

        var gapDays = CurrentSchedule.StaffingGapDays.ToHashSet();

        for (int day = 1; day <= daysInMonth; day++)
        {
            var date     = new DateOnly(SelectedYear, SelectedMonth, day);
            var isClosed = CurrentSchedule.ClosedDays.Contains(day);
            var calDay   = new CalendarDay
            {
                Date          = date,
                Day           = day,
                DayOfWeekText = GetDayOfWeekText(date.DayOfWeek),
                IsClosed      = isClosed,
                IsToday       = date == DateOnly.FromDateTime(DateTime.Today),
                IsWeekend     = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                IsSelected    = date == SelectedDate,
                HasStaffingGap = !isClosed && gapDays.Contains(day),
                HolidayName   = _monthHolidays.GetValueOrDefault(day),
            };

            if (!isClosed)
            {
                foreach (var shift in GetShiftsForDay(date, CurrentSchedule))
                {
                    var entries = CurrentSchedule.Entries
                        .Where(e => e.Date == date && e.ShiftSettingId == shift.Id)
                        .ToList();

                    var v     = EvaluateShiftForDrop(date, shift);
                    var vCopy = EvaluateShiftForDropCopy(date, shift);
                    calDay.ShiftBlocks.Add(new ShiftBlock
                    {
                        ShiftSetting          = shift,
                        Date                  = date,
                        EntryItems            = new ObservableCollection<EntryItem>(
                            entries
                                .Where(e => e.Employee is not null)
                                .Select(e => new EntryItem { EntryId = e.Id, Employee = e.Employee!, Date = date, ShiftSetting = shift })),
                        IsDisabled            = v.IsBlocked,
                        DisabledReason        = v.Reason,
                        IsDisabledForCopy     = vCopy.IsBlocked,
                        DisabledReasonForCopy = vCopy.Reason,
                    });
                }
            }

            allCells.Add(calDay);
            CalendarDays.Add(calDay);
        }

        while (allCells.Count % 7 != 0)
            allCells.Add(new CalendarDay { IsPlaceholder = true });

        for (int w = 0; w < allCells.Count / 7; w++)
        {
            var weekDays  = allCells.Skip(w * 7).Take(7).ToList();
            var firstReal = weekDays.FirstOrDefault(d => !d.IsPlaceholder);
            var weekNum   = firstReal is not null
                ? System.Globalization.ISOWeek.GetWeekOfYear(firstReal.Date.ToDateTime(TimeOnly.MinValue))
                : 0;

            var row = new CalendarWeekRow { WeekNumber = weekNum };
            foreach (var d in weekDays) row.Days.Add(d);
            CalendarWeeks.Add(row);
        }
    }

    private void BuildWeekView()
    {
        CalendarDays.Clear();
        TimeSlots.Clear();
        if (CurrentSchedule is null) return;

        const double hourHeight  = 60.0;
        var weekStartDate        = GetCurrentWeekStart();
        var (hourMin, hourMax)   = GetShiftHourRange();
        TimeGridHeight           = (hourMax - hourMin) * hourHeight;

        for (int h = hourMin; h < hourMax; h++)
            TimeSlots.Add(new CalendarTimeSlot { Hour = h, Label = $"{h:D2}:00" });

        for (int d = 0; d < 7; d++)
        {
            var date            = weekStartDate.AddDays(d);
            var isInCurrentMonth = date.Year == SelectedYear && date.Month == SelectedMonth;
            var isClosed        = isInCurrentMonth && CurrentSchedule.ClosedDays.Contains(date.Day);

            var calDay = new CalendarDay
            {
                Date          = date,
                Day           = date.Day,
                DayOfWeekText = GetDayOfWeekText(date.DayOfWeek),
                IsToday       = date == DateOnly.FromDateTime(DateTime.Today),
                IsSelected    = date == SelectedDate,
                IsClosed      = isClosed,
                IsOutOfScope  = !isInCurrentMonth,
                HasStaffingGap = !isClosed && isInCurrentMonth
                    && CurrentSchedule.StaffingGapDays.Contains(date.Day),
            };

            if (!isClosed && isInCurrentMonth)
            {
                foreach (var shift in GetShiftsForDay(date, CurrentSchedule))
                {
                    var startMin = shift.StartTime.Hour * 60 + shift.StartTime.Minute;
                    var endMin   = shift.EndTime.Hour   * 60 + shift.EndTime.Minute;
                    var top      = (startMin - hourMin * 60) * (hourHeight / 60.0);
                    var height   = Math.Max(20, (endMin - startMin) * (hourHeight / 60.0));
                    var entries  = CurrentSchedule.Entries
                        .Where(e => e.Date == date && e.ShiftSettingId == shift.Id).ToList();

                    var v     = EvaluateShiftForDrop(date, shift);
                    var vCopy = EvaluateShiftForDropCopy(date, shift);
                    calDay.ShiftBlocks.Add(new ShiftBlock
                    {
                        ShiftSetting          = shift,
                        Date                  = date,
                        EntryItems            = new ObservableCollection<EntryItem>(
                            entries.Where(e => e.Employee is not null)
                                   .Select(e => new EntryItem { EntryId = e.Id, Employee = e.Employee!, Date = date, ShiftSetting = shift })),
                        IsDisabled            = v.IsBlocked,
                        DisabledReason        = v.Reason,
                        IsDisabledForCopy     = vCopy.IsBlocked,
                        DisabledReasonForCopy = vCopy.Reason,
                        BlockTop              = top,
                        BlockHeight           = height,
                    });
                }
            }

            CalendarDays.Add(calDay);
        }
    }

    private void BuildDayView()
    {
        CalendarDays.Clear();
        TimeSlots.Clear();
        if (CurrentSchedule is null) return;

        const double hourHeight  = 60.0;
        var isClosed             = CurrentSchedule.ClosedDays.Contains(SelectedDate.Day)
            && SelectedDate.Month == SelectedMonth && SelectedDate.Year == SelectedYear;

        var (hourMin, hourMax)   = GetShiftHourRange();
        TimeGridHeight           = (hourMax - hourMin) * hourHeight;

        for (int h = hourMin; h < hourMax; h++)
            TimeSlots.Add(new CalendarTimeSlot { Hour = h, Label = $"{h:D2}:00" });

        var calDay = new CalendarDay
        {
            Date          = SelectedDate,
            Day           = SelectedDate.Day,
            DayOfWeekText = GetDayOfWeekText(SelectedDate.DayOfWeek),
            IsToday       = SelectedDate == DateOnly.FromDateTime(DateTime.Today),
            IsSelected    = true,
            IsClosed      = isClosed,
        };

        if (!isClosed)
        {
            foreach (var shift in GetShiftsForDay(SelectedDate, CurrentSchedule))
            {
                var startMin = shift.StartTime.Hour * 60 + shift.StartTime.Minute;
                var endMin   = shift.EndTime.Hour   * 60 + shift.EndTime.Minute;
                var top      = (startMin - hourMin * 60) * (hourHeight / 60.0);
                var height   = Math.Max(20, (endMin - startMin) * (hourHeight / 60.0));
                var entries  = CurrentSchedule.Entries
                    .Where(e => e.Date == SelectedDate && e.ShiftSettingId == shift.Id).ToList();

                var v     = EvaluateShiftForDrop(SelectedDate, shift);
                var vCopy = EvaluateShiftForDropCopy(SelectedDate, shift);
                calDay.ShiftBlocks.Add(new ShiftBlock
                {
                    ShiftSetting          = shift,
                    Date                  = SelectedDate,
                    EntryItems            = new ObservableCollection<EntryItem>(
                        entries.Where(e => e.Employee is not null)
                               .Select(e => new EntryItem { EntryId = e.Id, Employee = e.Employee!, Date = SelectedDate, ShiftSetting = shift })),
                    IsDisabled            = v.IsBlocked,
                    DisabledReason        = v.Reason,
                    IsDisabledForCopy     = vCopy.IsBlocked,
                    DisabledReasonForCopy = vCopy.Reason,
                    BlockTop              = top,
                    BlockHeight           = height,
                });
            }
        }

        CalendarDays.Add(calDay);
        DayViewDay = calDay;
    }

    // ══════════════════════════════════════════
    // 規則評估：拖曳時標記哪些 ShiftBlock 不可放入
    // ══════════════════════════════════════════

    // BuildCalendarView 前更新，避免每個 ShiftBlock 重複建立
    private IReadOnlyDictionary<int, ShiftSetting> _shiftLookupCache = new Dictionary<int, ShiftSetting>();

    // 同一次 BuildCalendarView 內，相同 (date, shiftId) 只評估一次
    private readonly Dictionary<(DateOnly, int), ShiftValidationResult> _evalDropCache  = new();
    private readonly Dictionary<(DateOnly, int), ShiftValidationResult> _evalCopyCache  = new();

    private ShiftValidationResult EvaluateShiftForDrop(DateOnly date, ShiftSetting shift)
    {
        if (SelectedEmployee is null || CurrentSchedule is null)
            return ShiftValidationResult.Allow;

        var key = (date, shift.Id);
        if (!_evalDropCache.TryGetValue(key, out var result))
        {
            result = ShiftRuleEngine.Evaluate(new ShiftValidationContext(
                Employee:        SelectedEmployee,
                Date:            date,
                TargetShift:     shift,
                Schedule:        CurrentSchedule,
                ActiveEmployees: ActiveEmployees,
                ExcludeEntryId:  DragSourceEntryId,
                LaborLaw:        _laborLaw,
                ShiftLookup:     _shiftLookupCache));
            _evalDropCache[key] = result;
        }
        return result;
    }

    // 複製模式：僅群組 C（時間重疊 + 每日工時上限）
    private ShiftValidationResult EvaluateShiftForDropCopy(DateOnly date, ShiftSetting shift)
    {
        if (SelectedEmployee is null || CurrentSchedule is null)
            return ShiftValidationResult.Allow;

        var key = (date, shift.Id);
        if (!_evalCopyCache.TryGetValue(key, out var result))
        {
            result = ShiftRuleEngine.EvaluateForCopy(new ShiftValidationContext(
                Employee:        SelectedEmployee,
                Date:            date,
                TargetShift:     shift,
                Schedule:        CurrentSchedule,
                ActiveEmployees: ActiveEmployees,
                ExcludeEntryId:  DragSourceEntryId,
                LaborLaw:        _laborLaw,
                ShiftLookup:     _shiftLookupCache));
            _evalCopyCache[key] = result;
        }
        return result;
    }

    public async Task RemoveEntryAsync(int entryId)
    {
        await _entryService.RemoveEntryAsync(entryId);
        await LoadScheduleAsync();
    }

    // ══════════════════════════════════════════
    // 工具方法
    // ══════════════════════════════════════════
    private List<ShiftSetting> GetShiftsForDay(DateOnly date, MonthlySchedule schedule)
    {
        var dateOverride = schedule.ShiftDateOverrides.FirstOrDefault(o => o.Day == date.Day);
        if (dateOverride is not null)
            return EnabledShifts.Where(s => dateOverride.ShiftIds.Contains(s.Id)).ToList();

        if (schedule.ShiftDayConfigs.Count == 0)
            return EnabledShifts.ToList();

        var dow = (int)date.DayOfWeek;
        return EnabledShifts.Where(s =>
        {
            var cfg = schedule.ShiftDayConfigs.FirstOrDefault(c => c.ShiftId == s.Id);
            if (cfg is null) return false;
            return cfg.DaysOfWeek.Count == 0 || cfg.DaysOfWeek.Contains(dow);
        }).ToList();
    }

    private (int Min, int Max) GetShiftHourRange()
    {
        if (!EnabledShifts.Any()) return (0, 24);

        int min = 23, max = 1;
        foreach (var s in EnabledShifts)
        {
            if (s.StartTime.Hour < min) min = s.StartTime.Hour;
            if (s.EndTime > s.StartTime)
            {
                int endHour = s.EndTime.Hour + (s.EndTime.Minute > 0 ? 1 : 0);
                if (endHour > max) max = endHour;
            }
            else
            {
                max = 24; // 跨午夜班別，顯示到底
            }
        }
        return (min, Math.Min(max, 24));
    }

    private DateOnly GetCurrentWeekStart()
    {
        var weekStart   = CurrentSchedule?.WeekStartDay ?? 1;
        var selectedDow = (int)SelectedDate.DayOfWeek;
        var offset      = (selectedDow - weekStart + 7) % 7;
        return SelectedDate.AddDays(-offset);
    }

    private static bool IsShiftInHour(ShiftSetting shift, int hour)
    {
        var startHour = shift.StartTime.Hour;
        var endHour   = shift.EndTime.Hour;
        if (shift.EndTime > shift.StartTime)
            return hour >= startHour && hour < endHour;
        else
            return hour >= startHour || hour < endHour;
    }

    private static bool IsShiftPartialInHour(ShiftSetting shift, int hour) =>
        shift.EndTime.Minute > 0 && shift.EndTime.Hour == hour;

    private static string GetDayOfWeekText(DayOfWeek dow) => ShiftRuleEngine.DayText(dow);
}
