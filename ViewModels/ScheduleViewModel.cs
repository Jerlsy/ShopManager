using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using ShopManager.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShopManager.ViewModels;

public partial class ScheduleViewModel : ObservableObject
{
    private readonly MonthlyScheduleService _scheduleService;
    private readonly ScheduleService _entryService;
    private readonly ShiftSettingService _shiftService;
    private readonly EmployeeService _employeeService;
    private readonly ShopSettingService _shopSettingService;
    private readonly SalarySettingService _salaryService;
    private readonly IAppSnackbarService _snackbarService;
    private LaborLawSetting? _laborLaw;

    public ScheduleViewModel(
        MonthlyScheduleService scheduleService,
        ScheduleService entryService,
        ShiftSettingService shiftService,
        EmployeeService employeeService,
        ShopSettingService shopSettingService,
        SalarySettingService salaryService,
        IAppSnackbarService snackbarService)
    {
        _scheduleService = scheduleService;
        _entryService = entryService;
        _shiftService = shiftService;
        _employeeService = employeeService;
        _shopSettingService = shopSettingService;
        _salaryService = salaryService;
        _snackbarService = snackbarService;

        foreach (var opt in CreateClosedDayOptions)
            opt.PropertyChanged += OnClosedDayOptionChanged;

        ShiftDayAssignments = new();
        ShiftDayAssignments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasShiftAssignments));
            OnPropertyChanged(nameof(HasMoreShiftsToAdd));
        };
    }

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
        if (anyUnchecked) HasShiftDayWarning = true;
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
    [ObservableProperty] private bool _hasShiftDayWarning;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoadingHolidays))]
    private bool _isLoadingHolidays;
    public bool IsNotLoadingHolidays => !IsLoadingHolidays;

    public ObservableCollection<NationalHolidayItem> ImportedHolidays { get; } = new();
    [ObservableProperty] private bool _isDayDetailOpen;
    [ObservableProperty] private string _dayDetailTitle = string.Empty;
    [ObservableProperty] private double _timeGridHeight = 480;
    [ObservableProperty] private CalendarDay? _dayViewDay;
    [ObservableProperty] private bool _isEntryCardOpen;
    [ObservableProperty] private Employee? _entryCardEmployee;
    [ObservableProperty] private string _entryCardShiftInfo = string.Empty;
    private int _entryCardEntryId;
    public ObservableCollection<DayDetailShiftGroup> DayDetailGroups { get; } = new();
    public ObservableCollection<ShiftDayAssignmentItem> ShiftDayAssignments { get; }
    public bool HasShiftAssignments => ShiftDayAssignments.Count > 0;
    public bool HasMoreShiftsToAdd =>
        ShiftDayAssignments.Count(a => a.SelectedShift is not null) < EnabledShifts.Count;

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
    public ObservableCollection<CalendarWeekRow> CalendarWeeks { get; } = new();
    public ObservableCollection<CalendarTimeSlot> TimeSlots { get; } = new();
    public List<string> DayHeaders { get; private set; } = new();
    private Dictionary<int, string> _monthHolidays = new();

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

    partial void OnSelectedYearChanged(int value) { OnPropertyChanged(nameof(CalendarTitle)); _ = LoadScheduleAsync(); }
    partial void OnSelectedMonthChanged(int value) { OnPropertyChanged(nameof(CalendarTitle)); _ = LoadScheduleAsync(); }
    partial void OnCreateYearChanged(int value) => ImportedHolidays.Clear();
    partial void OnCreateMonthChanged(int value) => ImportedHolidays.Clear();
    partial void OnViewModeChanged(CalendarViewMode value)
    {
        OnPropertyChanged(nameof(IsMonthView));
        OnPropertyChanged(nameof(IsWeekView));
        OnPropertyChanged(nameof(IsDayView));
        OnPropertyChanged(nameof(CalendarTitle));
        BuildCalendarView();
    }

    public bool IsMonthView => ViewMode == CalendarViewMode.Month;
    public bool IsWeekView  => ViewMode == CalendarViewMode.Week;
    public bool IsDayView   => ViewMode == CalendarViewMode.Day;

    [RelayCommand] public void SetMonthView() => ViewMode = CalendarViewMode.Month;
    [RelayCommand] public void SetWeekView()  => ViewMode = CalendarViewMode.Week;
    [RelayCommand] public void SetDayView()   => ViewMode = CalendarViewMode.Day;
    partial void OnSelectedDateChanged(DateOnly value) { OnPropertyChanged(nameof(CalendarTitle)); BuildCalendarView(); }
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

        _laborLaw = await _salaryService.GetLaborLawAsync();

        await LoadScheduleAsync();
    }

    private async Task LoadScheduleAsync()
    {
        CurrentSchedule = await _scheduleService.GetAsync(SelectedYear, SelectedMonth);
        HasSchedule = CurrentSchedule is not null;
        await LoadMonthHolidaysAsync();
        BuildCalendarView();
    }

    private async Task LoadMonthHolidaysAsync()
    {
        _monthHolidays.Clear();
        try
        {
            var url = $"https://cdn.jsdelivr.net/gh/ruyut/TaiwanCalendar/data/{SelectedYear}.json";
            var json = await _http.GetStringAsync(url);
            var all = JsonSerializer.Deserialize<List<CalendarDayDto>>(json);
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
    // 新增班表流程
    // ══════════════════════════════════════════
    [RelayCommand]
    public async Task StartCreateScheduleAsync()
    {
        CreateYear = SelectedYear;
        CreateMonth = SelectedMonth;
        HasShiftDayWarning = false;
        ShiftDayAssignments.Clear();
        ImportedHolidays.Clear();

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
        // 若班表已存在，先刪除舊班表再重建
        var existing = await _scheduleService.GetAsync(CreateYear, CreateMonth);
        if (existing is not null)
            await _scheduleService.DeleteAsync(existing.Id);

        var settings = await _shopSettingService.GetAsync() ?? new ShopSetting();
        settings.ClosedDaysOfWeek = CreateClosedDayOptions
            .Where(o => o.IsChecked)
            .Select(o => (int)o.Day)
            .ToList();

        var shiftDayConfigs = ShiftDayAssignments
            .Where(a => a.SelectedShift is not null)
            .Select(a => new ShiftDayConfig
            {
                ShiftId = a.SelectedShift!.Id,
                DaysOfWeek = a.DayCells.Where(c => c.IsChecked).Select(c => (int)c.Day).ToList(),
            })
            .Where(c => c.DaysOfWeek.Count > 0)
            .ToList();

        // 未手動設定班別時，自動為所有班別產生「非店休星期幾」的設定
        if (!shiftDayConfigs.Any())
        {
            var closedDows = CreateClosedDayOptions
                .Where(o => o.IsChecked)
                .Select(o => (int)o.Day)
                .ToHashSet();
            var workingDows = Enumerable.Range(0, 7).Where(d => !closedDows.Contains(d)).ToList();
            if (workingDows.Any())
            {
                shiftDayConfigs = EnabledShifts.Select(s => new ShiftDayConfig
                {
                    ShiftId = s.Id,
                    DaysOfWeek = workingDows,
                }).ToList();
            }
        }

        var additionalClosedDays = ImportedHolidays.Select(h => h.Day).ToList();
        await _scheduleService.CreateAsync(CreateYear, CreateMonth, settings, shiftDayConfigs, additionalClosedDays);
        IsCreating = false;

        SelectedYear = CreateYear;
        SelectedMonth = CreateMonth;
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess($"{CreateYear} 年 {CreateMonth} 月班表已建立");
    }

    [RelayCommand]
    private void OpenDayDetail(CalendarDay day)
    {
        if (day.IsPlaceholder || CurrentSchedule is null) return;

        DayDetailTitle = $"{day.Date:yyyy年M月d日}（週{GetDayOfWeekText(day.Date.DayOfWeek)}）";
        DayDetailGroups.Clear();

        foreach (var shift in EnabledShifts)
        {
            var employees = CurrentSchedule.Entries
                .Where(e => e.Date == day.Date && e.ShiftSettingId == shift.Id && e.Employee is not null)
                .Select(e => new EntryItem { EntryId = e.Id, Employee = e.Employee!, Date = e.Date, ShiftSetting = shift })
                .ToList();
            DayDetailGroups.Add(new DayDetailShiftGroup { Shift = shift, Employees = employees });
        }

        IsDayDetailOpen = true;
    }

    [RelayCommand]
    private void CloseDayDetail() => IsDayDetailOpen = false;

    [RelayCommand]
    private void OpenEntryCard(EntryItem item)
    {
        EntryCardEmployee = item.Employee;
        EntryCardShiftInfo = $"{item.Date:yyyy/M/d}  {item.ShiftSetting?.Alias ?? ""}";
        _entryCardEntryId = item.EntryId;
        IsEntryCardOpen = true;
    }

    [RelayCommand]
    private void CloseEntryCard() => IsEntryCardOpen = false;

    [RelayCommand]
    private async Task RemoveEntryFromCardAsync()
    {
        IsEntryCardOpen = false;
        IsDayDetailOpen = false;
        await _entryService.RemoveEntryAsync(_entryCardEntryId);
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess("已從班表移除");
    }

    [RelayCommand]
    private void AddShiftAssignment()
    {
        var usedIds = ShiftDayAssignments
            .Where(a => a.SelectedShift is not null)
            .Select(a => a.SelectedShift!.Id)
            .ToHashSet();

        var available = EnabledShifts.Where(s => !usedIds.Contains(s.Id)).ToList();
        if (available.Count == 0) return;

        var item = new ShiftDayAssignmentItem
        {
            AvailableShifts = available,
            SelectedShift = available.FirstOrDefault(),
        };

        var dayOrder = new (DayOfWeek Day, string Label)[]
        {
            (DayOfWeek.Monday,    "一"),
            (DayOfWeek.Tuesday,   "二"),
            (DayOfWeek.Wednesday, "三"),
            (DayOfWeek.Thursday,  "四"),
            (DayOfWeek.Friday,    "五"),
            (DayOfWeek.Saturday,  "六"),
            (DayOfWeek.Sunday,    "日"),
        };

        foreach (var (day, label) in dayOrder)
        {
            var closed = CreateClosedDayOptions.FirstOrDefault(o => o.Day == day)?.IsChecked ?? false;
            item.DayCells.Add(new ShiftDayCell { Day = day, Label = label, IsShopClosed = closed });
        }

        ShiftDayAssignments.Add(item);
    }

    [RelayCommand]
    private void RemoveShiftAssignment(ShiftDayAssignmentItem item)
    {
        ShiftDayAssignments.Remove(item);
        OnPropertyChanged(nameof(HasMoreShiftsToAdd));
    }

    private static readonly HttpClient _http = new();

    [RelayCommand]
    private async Task ImportNationalHolidaysAsync()
    {
        IsLoadingHolidays = true;
        ImportedHolidays.Clear();
        try
        {
            var url = $"https://cdn.jsdelivr.net/gh/ruyut/TaiwanCalendar/data/{CreateYear}.json";
            var json = await _http.GetStringAsync(url);
            var all = JsonSerializer.Deserialize<List<CalendarDayDto>>(json);
            if (all is null) return;

            var prefix = $"{CreateYear}{CreateMonth:D2}";
            var holidays = all
                .Where(d => d.Date.StartsWith(prefix)
                         && d.IsHoliday
                         && !string.IsNullOrEmpty(d.Description)
                         && d.Week != "六" && d.Week != "日")
                .Select(d => new NationalHolidayItem
                {
                    Day = int.Parse(d.Date[6..]),
                    Label = $"{CreateMonth}/{int.Parse(d.Date[6..])} {d.Description}",
                })
                .ToList();

            foreach (var h in holidays)
                ImportedHolidays.Add(h);

            if (ImportedHolidays.Count == 0)
                _snackbarService.ShowSuccess("該月無國定假日（平日）");
            else
                _snackbarService.ShowSuccess($"已導入 {ImportedHolidays.Count} 個國定假日");
        }
        catch
        {
            _snackbarService.ShowError("無法取得國定假日資料，請確認網路連線");
        }
        finally
        {
            IsLoadingHolidays = false;
        }
    }

    // ══════════════════════════════════════════
    // 導覽（月/周/日 共用同一對按鈕）
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
        CalendarWeeks.Clear();
        if (CurrentSchedule is null) return;

        var daysInMonth = DateTime.DaysInMonth(SelectedYear, SelectedMonth);
        var firstDay = new DateOnly(SelectedYear, SelectedMonth, 1);
        var weekStart = CurrentSchedule.WeekStartDay;

        // Build ordered day headers based on WeekStartDay
        var dayNames = new[] { "日", "一", "二", "三", "四", "五", "六" };
        DayHeaders = Enumerable.Range(0, 7).Select(i => dayNames[(weekStart + i) % 7]).ToList();
        OnPropertyChanged(nameof(DayHeaders));

        var firstDow = (int)firstDay.DayOfWeek;
        var offset = (firstDow - weekStart + 7) % 7;

        var allCells = new List<CalendarDay>();
        for (int i = 0; i < offset; i++)
            allCells.Add(new CalendarDay { IsPlaceholder = true });

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
                HolidayName = _monthHolidays.GetValueOrDefault(day),
            };

            if (!isClosed)
            {
                var dow = (int)date.DayOfWeek;
                var shiftsForDay = CurrentSchedule.ShiftDayConfigs.Count == 0
                    ? EnabledShifts.ToList()
                    : EnabledShifts.Where(s => CurrentSchedule.ShiftDayConfigs
                        .Any(c => c.ShiftId == s.Id && c.DaysOfWeek.Contains(dow))).ToList();

                foreach (var shift in shiftsForDay)
                {
                    var entries = CurrentSchedule.Entries
                        .Where(e => e.Date == date && e.ShiftSettingId == shift.Id)
                        .ToList();

                    var v = EvaluateShiftForDrop(date, shift);
                    calDay.ShiftBlocks.Add(new ShiftBlock
                    {
                        ShiftSetting   = shift,
                        Date           = date,
                        EntryItems     = new ObservableCollection<EntryItem>(
                            entries
                                .Where(e => e.Employee is not null)
                                .Select(e => new EntryItem { EntryId = e.Id, Employee = e.Employee!, Date = date, ShiftSetting = shift })),
                        IsDisabled     = v.IsBlocked,
                        DisabledReason = v.Reason,
                    });
                }
            }

            allCells.Add(calDay);
            CalendarDays.Add(calDay);
        }

        // Pad trailing placeholders to complete last row
        while (allCells.Count % 7 != 0)
            allCells.Add(new CalendarDay { IsPlaceholder = true });

        // Build week rows with ISO week numbers
        for (int w = 0; w < allCells.Count / 7; w++)
        {
            var weekDays = allCells.Skip(w * 7).Take(7).ToList();
            var firstReal = weekDays.FirstOrDefault(d => !d.IsPlaceholder);
            var weekNum = firstReal is not null
                ? System.Globalization.ISOWeek.GetWeekOfYear(firstReal.Date.ToDateTime(TimeOnly.MinValue))
                : 0;

            var row = new CalendarWeekRow { WeekNumber = weekNum };
            foreach (var d in weekDays)
                row.Days.Add(d);
            CalendarWeeks.Add(row);
        }
    }

    private void BuildWeekView()
    {
        CalendarDays.Clear();
        TimeSlots.Clear();
        if (CurrentSchedule is null) return;

        const double hourHeight = 60.0;
        var weekStartDate = GetCurrentWeekStart();
        var (hourMin, hourMax) = GetShiftHourRange();
        TimeGridHeight = (hourMax - hourMin) * hourHeight;

        for (int h = hourMin; h < hourMax; h++)
            TimeSlots.Add(new CalendarTimeSlot { Hour = h, Label = $"{h:D2}:00" });

        for (int d = 0; d < 7; d++)
        {
            var date = weekStartDate.AddDays(d);
            var isClosed = CurrentSchedule.ClosedDays.Contains(date.Day)
                && date.Month == SelectedMonth && date.Year == SelectedYear;

            var calDay = new CalendarDay
            {
                Date = date,
                Day = date.Day,
                DayOfWeekText = GetDayOfWeekText(date.DayOfWeek),
                IsToday   = date == DateOnly.FromDateTime(DateTime.Today),
                IsSelected = date == SelectedDate,
                IsClosed  = isClosed,
            };

            if (!isClosed)
            {
                var dow = (int)date.DayOfWeek;
                var shiftsForDay = CurrentSchedule.ShiftDayConfigs.Count == 0
                    ? EnabledShifts.ToList()
                    : EnabledShifts.Where(s => CurrentSchedule.ShiftDayConfigs
                        .Any(c => c.ShiftId == s.Id && c.DaysOfWeek.Contains(dow))).ToList();

                foreach (var shift in shiftsForDay)
                {
                    var startMin = shift.StartTime.Hour * 60 + shift.StartTime.Minute;
                    var endMin   = shift.EndTime.Hour   * 60 + shift.EndTime.Minute;
                    var top      = (startMin - hourMin * 60) * (hourHeight / 60.0);
                    var height   = Math.Max(20, (endMin - startMin) * (hourHeight / 60.0));
                    var entries  = CurrentSchedule.Entries
                        .Where(e => e.Date == date && e.ShiftSettingId == shift.Id).ToList();

                    var v = EvaluateShiftForDrop(date, shift);
                    calDay.ShiftBlocks.Add(new ShiftBlock
                    {
                        ShiftSetting   = shift,
                        Date           = date,
                        EntryItems     = new ObservableCollection<EntryItem>(
                            entries.Where(e => e.Employee is not null)
                                   .Select(e => new EntryItem { EntryId = e.Id, Employee = e.Employee!, Date = date, ShiftSetting = shift })),
                        IsDisabled     = v.IsBlocked,
                        DisabledReason = v.Reason,
                        BlockTop       = top,
                        BlockHeight    = height,
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

        const double hourHeight = 60.0;
        var isClosed = CurrentSchedule.ClosedDays.Contains(SelectedDate.Day)
            && SelectedDate.Month == SelectedMonth && SelectedDate.Year == SelectedYear;

        var (hourMin, hourMax) = GetShiftHourRange();
        TimeGridHeight = (hourMax - hourMin) * hourHeight;

        for (int h = hourMin; h < hourMax; h++)
            TimeSlots.Add(new CalendarTimeSlot { Hour = h, Label = $"{h:D2}:00" });

        var calDay = new CalendarDay
        {
            Date = SelectedDate,
            Day  = SelectedDate.Day,
            DayOfWeekText = GetDayOfWeekText(SelectedDate.DayOfWeek),
            IsToday   = SelectedDate == DateOnly.FromDateTime(DateTime.Today),
            IsSelected = true,
            IsClosed  = isClosed,
        };

        if (!isClosed)
        {
            var dow = (int)SelectedDate.DayOfWeek;
            var shiftsForDay = CurrentSchedule.ShiftDayConfigs.Count == 0
                ? EnabledShifts.ToList()
                : EnabledShifts.Where(s => CurrentSchedule.ShiftDayConfigs
                    .Any(c => c.ShiftId == s.Id && c.DaysOfWeek.Contains(dow))).ToList();

            foreach (var shift in shiftsForDay)
            {
                var startMin = shift.StartTime.Hour * 60 + shift.StartTime.Minute;
                var endMin   = shift.EndTime.Hour   * 60 + shift.EndTime.Minute;
                var top      = (startMin - hourMin * 60) * (hourHeight / 60.0);
                var height   = Math.Max(20, (endMin - startMin) * (hourHeight / 60.0));
                var entries  = CurrentSchedule.Entries
                    .Where(e => e.Date == SelectedDate && e.ShiftSettingId == shift.Id).ToList();

                var v = EvaluateShiftForDrop(SelectedDate, shift);
                calDay.ShiftBlocks.Add(new ShiftBlock
                {
                    ShiftSetting   = shift,
                    Date           = SelectedDate,
                    EntryItems     = new ObservableCollection<EntryItem>(
                        entries.Where(e => e.Employee is not null)
                               .Select(e => new EntryItem { EntryId = e.Id, Employee = e.Employee!, Date = SelectedDate, ShiftSetting = shift })),
                    IsDisabled     = v.IsBlocked,
                    DisabledReason = v.Reason,
                    BlockTop       = top,
                    BlockHeight    = height,
                });
            }
        }

        CalendarDays.Add(calDay);
        DayViewDay = calDay;
    }

    // ══════════════════════════════════════════
    // 拖放排班
    // ══════════════════════════════════════════
    // 拖曳來源 EntryId（班表間移動時排除重疊/重複檢查的自身班次）
    public int DragSourceEntryId { get; set; } = -1;

    public async Task DropEmployeeAsync(Employee employee, DateOnly date, ShiftSetting shift, int? sourceEntryId = null)
    {
        if (CurrentSchedule is null) return;

        // 重複檢查（移動時排除來源班次本身）
        var existing = CurrentSchedule.Entries.Any(e =>
            e.EmployeeId == employee.Id &&
            e.Date == date &&
            e.ShiftSettingId == shift.Id &&
            e.Id != (sourceEntryId ?? -1));
        if (existing) return;

        await _entryService.AddEntryAsync(new ScheduleEntry
        {
            MonthlyScheduleId = CurrentSchedule.Id,
            EmployeeId = employee.Id,
            Date = date,
            ShiftSettingId = shift.Id,
        });

        if (sourceEntryId.HasValue)
            await _entryService.RemoveEntryAsync(sourceEntryId.Value);

        await LoadScheduleAsync();
    }

    public async Task RemoveEntryAsync(int entryId)
    {
        await _entryService.RemoveEntryAsync(entryId);
        await LoadScheduleAsync();
    }

    // ══════════════════════════════════════════
    // 排班規則評估（委派至 ShiftRuleEngine）
    // ══════════════════════════════════════════
    private ShiftValidationResult EvaluateShiftForDrop(DateOnly date, ShiftSetting shift)
    {
        if (SelectedEmployee is null || CurrentSchedule is null)
            return ShiftValidationResult.Allow;

        return ShiftRuleEngine.Evaluate(new ShiftValidationContext(
            Employee:        SelectedEmployee,
            Date:            date,
            TargetShift:     shift,
            Schedule:        CurrentSchedule,
            ActiveEmployees: ActiveEmployees,
            ExcludeEntryId:  DragSourceEntryId,
            LaborLaw:        _laborLaw));
    }

    // ══════════════════════════════════════════
    // 工具方法
    // ══════════════════════════════════════════
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

    // 班別在該小時內「未滿一整時」結束（例如 EndTime=18:30 → hour=18 為半格）
    private static bool IsShiftPartialInHour(ShiftSetting shift, int hour) =>
        shift.EndTime.Minute > 0 && shift.EndTime.Hour == hour;

    private static string GetDayOfWeekText(DayOfWeek dow) => ShiftRuleEngine.DayText(dow);
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
    public DateOnly Date { get; set; }
    public ShiftSetting? ShiftSetting { get; set; }
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
    public string? HolidayName { get; set; }
    public ObservableCollection<ShiftBlock> ShiftBlocks { get; } = new();
}

public class CalendarWeekRow
{
    public int WeekNumber { get; set; }
    public List<CalendarDay> Days { get; } = new();
}

public class ShiftBlock
{
    public ShiftSetting ShiftSetting { get; set; } = null!;
    public DateOnly Date { get; set; }
    public ObservableCollection<EntryItem> EntryItems { get; set; } = new();
    public bool IsDisabled { get; set; }
    public string DisabledReason { get; set; } = string.Empty;
    // 時間軸定位（周/日視圖）
    public double BlockTop { get; set; }
    public double BlockHeight { get; set; }
    public System.Windows.Thickness BlockMargin => new(2, BlockTop, 2, 0);
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

public partial class ShiftDayCell : ObservableObject
{
    public DayOfWeek Day { get; init; }
    public string Label { get; init; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnabled))]
    private bool _isShopClosed;

    public bool IsEnabled => !IsShopClosed;

    [ObservableProperty] private bool _isChecked;
}

public partial class ShiftDayAssignmentItem : ObservableObject
{
    public IReadOnlyList<ShiftSetting> AvailableShifts { get; init; } = [];
    [ObservableProperty] private ShiftSetting? _selectedShift;
    public ObservableCollection<ShiftDayCell> DayCells { get; } = new();
}

public class NationalHolidayItem
{
    public int Day { get; init; }
    public string Label { get; init; } = string.Empty;
}

public class DayDetailShiftGroup
{
    public ShiftSetting Shift { get; set; } = null!;
    public List<EntryItem> Employees { get; set; } = new();
}

file record CalendarDayDto(
    [property: JsonPropertyName("date")]        string Date,
    [property: JsonPropertyName("week")]        string Week,
    [property: JsonPropertyName("isHoliday")]   bool   IsHoliday,
    [property: JsonPropertyName("description")] string Description);
