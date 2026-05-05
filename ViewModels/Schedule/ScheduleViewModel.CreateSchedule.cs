using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;

namespace ShopManager.ViewModels;

public partial class ScheduleViewModel
{
    // ── 建立班表用的暫存設定 ──────────────────────────────────────────────
    [ObservableProperty] private int _createYear  = DateTime.Today.Year;
    [ObservableProperty] private int _createMonth = DateTime.Today.Month;
    [ObservableProperty] private bool _hasShiftDayWarning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoadingHolidays))]
    private bool _isLoadingHolidays;
    public bool IsNotLoadingHolidays => !IsLoadingHolidays;

    public ObservableCollection<NationalHolidayItem> ImportedHolidays { get; } = new();

    // ── 班別／上班日條件／員工約束集合 ──────────────────────────────────
    public ObservableCollection<ShiftDayAssignmentItem> ShiftDayAssignments { get; } = new();
    public bool HasShiftAssignments => ShiftDayAssignments.Count > 0;
    public bool HasMoreShiftsToAdd  =>
        ShiftDayAssignments.Count(a => a.SelectedShift is not null) < EnabledShifts.Count;

    public ObservableCollection<WorkDayConditionItem>  WorkDayConditions  { get; } = new();
    public bool HasWorkDayConditions => WorkDayConditions.Count > 0;

    public ObservableCollection<EmployeeConstraintItem> EmployeeConstraints { get; } = new();
    public bool HasEmployeeConstraints => EmployeeConstraints.Count > 0;

    public ObservableCollection<DayOfWeekOption> CreateClosedDayOptions { get; } = new()
    {
        new(DayOfWeek.Monday,    "周一"),
        new(DayOfWeek.Tuesday,   "周二"),
        new(DayOfWeek.Wednesday, "周三"),
        new(DayOfWeek.Thursday,  "周四"),
        new(DayOfWeek.Friday,    "周五"),
        new(DayOfWeek.Saturday,  "周六"),
        new(DayOfWeek.Sunday,    "周日"),
    };

    // 建立班表表單的年月日選項
    public List<int> AvailableCreateMonthDays =>
        Enumerable.Range(1, DateTime.DaysInMonth(CreateYear, CreateMonth)).ToList();

    partial void OnCreateYearChanged(int value)
    {
        ImportedHolidays.Clear();
        OnPropertyChanged(nameof(AvailableCreateMonthDays));
    }

    partial void OnCreateMonthChanged(int value)
    {
        ImportedHolidays.Clear();
        OnPropertyChanged(nameof(AvailableCreateMonthDays));
    }

    // 星期排序表（AddShiftAssignment / AddWorkDayCondition / StartAutoAssignAsync 共用）
    internal static readonly (DayOfWeek Day, string Label)[] _dayOrder =
    [
        (DayOfWeek.Monday,    "一"),
        (DayOfWeek.Tuesday,   "二"),
        (DayOfWeek.Wednesday, "三"),
        (DayOfWeek.Thursday,  "四"),
        (DayOfWeek.Friday,    "五"),
        (DayOfWeek.Saturday,  "六"),
        (DayOfWeek.Sunday,    "日"),
    ];

    // ══════════════════════════════════════════
    // 新增班表流程
    // ══════════════════════════════════════════
    [RelayCommand]
    public async Task StartCreateScheduleAsync()
    {
        CreateYear         = SelectedYear;
        CreateMonth        = SelectedMonth;
        HasShiftDayWarning = false;
        ShiftDayAssignments.Clear();
        WorkDayConditions.Clear();
        ImportedHolidays.Clear();
        EmployeeConstraints.Clear();

        var settings = await _shopSettingService.GetAsync();
        foreach (var option in CreateClosedDayOptions)
            option.IsChecked = settings?.ClosedDaysOfWeek.Contains((int)option.Day) ?? false;

        // 還原套用班別（優先從當月；無則從前一個月）
        MonthlySchedule? sourceSchedule = null;
        if (CurrentSchedule is not null
            && CurrentSchedule.Year == CreateYear && CurrentSchedule.Month == CreateMonth)
        {
            sourceSchedule = CurrentSchedule;
        }
        else
        {
            var prevYear  = CreateMonth == 1 ? CreateYear - 1 : CreateYear;
            var prevMonth = CreateMonth == 1 ? 12 : CreateMonth - 1;
            sourceSchedule = await _scheduleService.GetAsync(prevYear, prevMonth);
        }

        if (sourceSchedule is not null)
        {
            foreach (var config in sourceSchedule.ShiftDayConfigs)
            {
                var shift = EnabledShifts.FirstOrDefault(s => s.Id == config.ShiftId);
                if (shift is null) continue;

                var usedIds   = ShiftDayAssignments
                    .Where(a => a.SelectedShift is not null)
                    .Select(a => a.SelectedShift!.Id)
                    .ToHashSet();
                var available = EnabledShifts
                    .Where(s => !usedIds.Contains(s.Id) || s.Id == shift.Id)
                    .ToList();

                var item = new ShiftDayAssignmentItem { AvailableShifts = available, SelectedShift = shift };
                foreach (var (day, label) in _dayOrder)
                {
                    var closed = CreateClosedDayOptions.FirstOrDefault(o => o.Day == day)?.IsChecked ?? false;
                    var cell   = new ShiftDayCell { Day = day, Label = label, IsShopClosed = closed };
                    if (!closed) cell.IsChecked = config.DaysOfWeek.Contains((int)day);
                    item.DayCells.Add(cell);
                }
                ShiftDayAssignments.Add(item);
            }
        }

        IsQuickAdding = false;
        IsBatchMode   = false;
        IsCreating    = true;
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
                ShiftId    = a.SelectedShift!.Id,
                DaysOfWeek = a.DayCells.Where(c => c.IsChecked).Select(c => (int)c.Day).ToList(),
            })
            .ToList();

        // 未手動設定班別時，自動為所有班別產生「非店休星期幾」的設定
        if (!shiftDayConfigs.Any())
        {
            var closedDows  = CreateClosedDayOptions
                .Where(o => o.IsChecked)
                .Select(o => (int)o.Day)
                .ToHashSet();
            var workingDows = Enumerable.Range(0, 7).Where(d => !closedDows.Contains(d)).ToList();
            if (workingDows.Any())
            {
                shiftDayConfigs = EnabledShifts.Select(s => new ShiftDayConfig
                {
                    ShiftId    = s.Id,
                    DaysOfWeek = workingDows,
                }).ToList();
            }
        }

        var additionalClosedDays = ImportedHolidays.Select(h => h.Day).ToList();
        await _scheduleService.CreateAsync(CreateYear, CreateMonth, settings, shiftDayConfigs, additionalClosedDays);

        IsCreating = false;
        ClearUndoStack();
        SelectedYear  = CreateYear;
        SelectedMonth = CreateMonth;
        _snackbarService.ShowSuccess($"{CreateYear} 年 {CreateMonth} 月班表已建立");

        await LoadScheduleAsync();
    }

    // ── 班別分配列管理 ──────────────────────────────────────────────────
    [RelayCommand]
    private void AddShiftAssignment()
    {
        var usedIds   = ShiftDayAssignments
            .Where(a => a.SelectedShift is not null)
            .Select(a => a.SelectedShift!.Id)
            .ToHashSet();

        var available = EnabledShifts.Where(s => !usedIds.Contains(s.Id)).ToList();
        if (available.Count == 0) return;

        var item = new ShiftDayAssignmentItem
        {
            AvailableShifts = available,
            SelectedShift   = available.FirstOrDefault(),
        };

        foreach (var (day, label) in _dayOrder)
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

    // ── 上班日條件管理 ──────────────────────────────────────────────────
    [RelayCommand]
    private void AddWorkDayCondition()
    {
        var item = new WorkDayConditionItem();
        foreach (var (day, label) in _dayOrder)
        {
            var closed      = CreateClosedDayOptions.FirstOrDefault(o => o.Day == day)?.IsChecked ?? false;
            var alreadyUsed = WorkDayConditions.SelectMany(c => c.DayCells).Any(c => c.Day == day && c.IsChecked);
            var cell = new WorkDayConditionCell { Day = day, Label = label, IsShopClosed = closed, IsAlreadyUsed = alreadyUsed };
            cell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(WorkDayConditionCell.IsChecked))
                    RefreshWorkDayConditionUsed();
            };
            item.DayCells.Add(cell);
        }
        WorkDayConditions.Add(item);
    }

    [RelayCommand]
    private void RemoveWorkDayCondition(WorkDayConditionItem item)
    {
        WorkDayConditions.Remove(item);
        RefreshWorkDayConditionUsed();
    }

    // ── 員工約束管理 ────────────────────────────────────────────────────
    [RelayCommand]
    private void AddEmployeeConstraint()
    {
        // 允許同一員工最多兩筆（各群組一筆：休假日/上班日 + 優先班別/不排班）
        var emp = ActiveEmployees.FirstOrDefault(e =>
            EmployeeConstraints.Count(c => c.SelectedEmployee?.Id == e.Id) < 2);
        if (emp is null) return;

        var usedTypes = EmployeeConstraints
            .Where(c => c.SelectedEmployee?.Id == emp.Id)
            .Select(c => c.ConstraintType)
            .ToHashSet();

        bool groupAUsed = usedTypes.Contains(EmployeeConstraintType.DayOff) ||
                          usedTypes.Contains(EmployeeConstraintType.WorkDay);
        bool groupBUsed = usedTypes.Contains(EmployeeConstraintType.ShiftPriority) ||
                          usedTypes.Contains(EmployeeConstraintType.ExcludeAutoAssign);

        var defaultType = !groupAUsed ? EmployeeConstraintType.DayOff
                        : !groupBUsed ? EmployeeConstraintType.ShiftPriority
                        : (EmployeeConstraintType?)null;
        if (defaultType is null) return;

        var item = new EmployeeConstraintItem
        {
            SelectedEmployee = emp,
            ConstraintType   = defaultType.Value,
        };
        item.InitializeDayCells(CreateYear, CreateMonth, CurrentSchedule?.ClosedDays ?? []);
        EmployeeConstraints.Add(item);
    }

    [RelayCommand]
    private void RemoveEmployeeConstraint(EmployeeConstraintItem item)
        => EmployeeConstraints.Remove(item);

    // ── 工作日條件「已使用」旗標刷新（OnClosedDayOptionChanged 也會呼叫）
    private void RefreshWorkDayConditionUsed()
    {
        var checkedDays = WorkDayConditions
            .SelectMany(c => c.DayCells)
            .Where(cell => cell.IsChecked)
            .Select(cell => cell.Day)
            .ToHashSet();

        foreach (var cond in WorkDayConditions)
            foreach (var cell in cond.DayCells)
                cell.IsAlreadyUsed = checkedDays.Contains(cell.Day) && !cell.IsChecked;
    }

    // ══════════════════════════════════════════
    // 導入國定假日
    // ══════════════════════════════════════════
    [RelayCommand]
    private async Task ImportNationalHolidaysAsync()
    {
        IsLoadingHolidays = true;
        ImportedHolidays.Clear();
        try
        {
            var url  = $"https://cdn.jsdelivr.net/gh/ruyut/TaiwanCalendar/data/{CreateYear}.json";
            var json = await _http.GetStringAsync(url);
            var all  = JsonSerializer.Deserialize<List<CalendarDayDto>>(json);
            if (all is null) return;

            var prefix   = $"{CreateYear}{CreateMonth:D2}";
            var holidays = all
                .Where(d => d.Date.StartsWith(prefix)
                         && d.IsHoliday
                         && !string.IsNullOrEmpty(d.Description)
                         && d.Week != "六" && d.Week != "日")
                .Select(d => new NationalHolidayItem
                {
                    Day   = int.Parse(d.Date[6..]),
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
}
