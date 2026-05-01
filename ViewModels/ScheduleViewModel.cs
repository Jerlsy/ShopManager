using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
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
    private readonly ScheduleConflictService _conflictService;
    private readonly IAppSnackbarService _snackbarService;
    private LaborLawSetting? _laborLaw;

    public ScheduleViewModel(
        MonthlyScheduleService scheduleService,
        ScheduleService entryService,
        ShiftSettingService shiftService,
        EmployeeService employeeService,
        ShopSettingService shopSettingService,
        SalarySettingService salaryService,
        ScheduleConflictService conflictService,
        IAppSnackbarService snackbarService)
    {
        _scheduleService = scheduleService;
        _entryService = entryService;
        _shiftService = shiftService;
        _employeeService = employeeService;
        _shopSettingService = shopSettingService;
        _salaryService = salaryService;
        _conflictService = conflictService;
        _snackbarService = snackbarService;

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

        ShiftDayAssignments = new();
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
        // 只在員工欄位改變時，對「該筆」做自動切換群組，避免動到其他既有筆的資料
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
                var empId = item.SelectedEmployee.Id;
                var othersTypes = EmployeeConstraints
                    .Where(c => c != item && c.SelectedEmployee?.Id == empId)
                    .Select(c => c.ConstraintType)
                    .ToHashSet();

                // 群組 A（休假日/上班日）已被其他筆佔用 → 整組濾掉
                if (othersTypes.Contains(EmployeeConstraintType.DayOff) ||
                    othersTypes.Contains(EmployeeConstraintType.WorkDay))
                {
                    available.Remove(EmployeeConstraintType.DayOff);
                    available.Remove(EmployeeConstraintType.WorkDay);
                }
                // 群組 B（優先班別/不排班）已被其他筆佔用 → 整組濾掉
                if (othersTypes.Contains(EmployeeConstraintType.ShiftPriority) ||
                    othersTypes.Contains(EmployeeConstraintType.ExcludeAutoAssign))
                {
                    available.Remove(EmployeeConstraintType.ShiftPriority);
                    available.Remove(EmployeeConstraintType.ExcludeAutoAssign);
                }
            }

            // 用增量更新（而非 Clear + Add）以避免 ComboBox SelectedItem 在清空瞬間遺失
            // 不在此處自動切換 ConstraintType，以免影響既有筆的設定資料
            foreach (var t in item.AvailableConstraintTypes.Except(available).ToList())
                item.AvailableConstraintTypes.Remove(t);
            foreach (var t in available)
                if (!item.AvailableConstraintTypes.Contains(t))
                    item.AvailableConstraintTypes.Add(t);
        }
    }

    // ── 狀態 ──────────────────────────────────
    [ObservableProperty] private MonthlySchedule? _currentSchedule;
    [ObservableProperty] private int _selectedYear = DateTime.Today.Year;
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
    public ObservableCollection<ShiftBlock> DayDetailGroups { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DayDetailIsClosed))]
    [NotifyPropertyChangedFor(nameof(DayDetailDayHasOverride))]
    [NotifyPropertyChangedFor(nameof(ShowDayDetailShiftGroups))]
    [NotifyPropertyChangedFor(nameof(ShowDayDetailClosedMessage))]
    private CalendarDay? _dayDetailDay;
    public bool DayDetailIsClosed => DayDetailDay?.IsClosed ?? false;
    public bool DayDetailDayHasOverride =>
        DayDetailDay is not null &&
        CurrentSchedule?.ShiftDateOverrides.Any(o => o.Day == DayDetailDay.Date.Day) == true;
    public bool ShowDayDetailShiftGroups   => !DayDetailIsClosed && !IsShiftOverrideEditing;
    public bool ShowDayDetailClosedMessage =>  DayDetailIsClosed && !IsShiftOverrideEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDayDetailShiftGroups))]
    [NotifyPropertyChangedFor(nameof(ShowDayDetailClosedMessage))]
    private bool _isShiftOverrideEditing;
    public ObservableCollection<ShiftOverrideCell> ShiftOverrideCells { get; } = new();

    // ── 員工本月排班詳情 ──────────────────────────
    [ObservableProperty] private bool _isEmployeeDetailOpen;
    [ObservableProperty] private EmployeeWorkloadItem? _employeeDetailItem;
    [ObservableProperty] private int? _employeeDetailNewDayOff;
    public ObservableCollection<EmployeeDetailEntry>     EmployeeDetailEntries     { get; } = new();
    public ObservableCollection<EmployeeRuleDisplayItem> EmployeeDetailRules       { get; } = new();
    public ObservableCollection<int>                     EmployeeDetailDayOffs     { get; } = new();
    public ObservableCollection<string>                  EmployeeDetailPriorityShifts { get; } = new();

    public List<int> AvailableEmployeeDetailDays
    {
        get
        {
            if (CurrentSchedule is null) return [];
            var daysInMonth = DateTime.DaysInMonth(CurrentSchedule.Year, CurrentSchedule.Month);
            var closed  = CurrentSchedule.ClosedDays.ToHashSet();
            var already = EmployeeDetailDayOffs.ToHashSet();
            return Enumerable.Range(1, daysInMonth)
                .Where(d => !closed.Contains(d) && !already.Contains(d))
                .ToList();
        }
    }

    [RelayCommand]
    private void OpenEmployeeDetail(EmployeeWorkloadItem item)
    {
        EmployeeDetailItem   = item;
        EmployeeDetailEntries.Clear();
        EmployeeDetailRules.Clear();
        EmployeeDetailDayOffs.Clear();
        EmployeeDetailPriorityShifts.Clear();
        EmployeeDetailNewDayOff = null;

        if (CurrentSchedule is not null)
        {
            foreach (var entry in CurrentSchedule.Entries
                .Where(e => e.EmployeeId == item.Employee.Id && e.ShiftSetting is not null)
                .OrderBy(e => e.Date)
                .ThenBy(e => e.ShiftSetting!.StartTime))
            {
                EmployeeDetailEntries.Add(new EmployeeDetailEntry
                {
                    DateText      = $"{entry.Date:MM/dd}",
                    DayOfWeekText = GetDayOfWeekText(entry.Date.DayOfWeek),
                    ShiftAlias    = entry.ShiftSetting!.Alias,
                    TimeRange     = $"{entry.ShiftSetting.StartTime:HH\\:mm} – {entry.ShiftSetting.EndTime:HH\\:mm}",
                });
            }
        }

        foreach (var rule in item.Employee.ScheduleRules)
        {
            switch (rule.Type)
            {
                case ScheduleRuleType.FixedOff when rule.FixedOffDays.Count > 0:
                    EmployeeDetailRules.Add(new EmployeeRuleDisplayItem
                    {
                        TypeLabel = "固定休假",
                        Detail = string.Join("、", rule.FixedOffDays.OrderBy(d => d)
                            .Select(d => "週" + GetDayOfWeekText((DayOfWeek)d))),
                    });
                    break;

                case ScheduleRuleType.ExcludeShift when rule.ExcludedShiftIds.Count > 0:
                    EmployeeDetailRules.Add(new EmployeeRuleDisplayItem
                    {
                        TypeLabel = "排除班別",
                        Detail = string.Join("、", rule.ExcludedShiftIds
                            .Select(id => EnabledShifts.FirstOrDefault(s => s.Id == id)?.Alias ?? $"#{id}")),
                    });
                    break;

                case ScheduleRuleType.NotWith when rule.ExcludedColleagueIds.Count > 0:
                    EmployeeDetailRules.Add(new EmployeeRuleDisplayItem
                    {
                        TypeLabel = "不與同班",
                        Detail = string.Join("、", rule.ExcludedColleagueIds
                            .Select(id => ActiveEmployees.FirstOrDefault(e => e.Id == id)?.Name ?? $"#{id}")),
                    });
                    break;

                case ScheduleRuleType.NotWithDay when rule.ExcludedColleagueIds.Count > 0:
                    EmployeeDetailRules.Add(new EmployeeRuleDisplayItem
                    {
                        TypeLabel = "不與同天",
                        Detail = string.Join("、", rule.ExcludedColleagueIds
                            .Select(id => ActiveEmployees.FirstOrDefault(e => e.Id == id)?.Name ?? $"#{id}")),
                    });
                    break;
            }
        }

        // 本月休息日
        if (CurrentSchedule is not null)
        {
            var existing = CurrentSchedule.EmployeeDayOffs.FirstOrDefault(d => d.EmployeeId == item.Employee.Id);
            if (existing is not null)
                foreach (var d in existing.Days) EmployeeDetailDayOffs.Add(d);
        }

        // 優先班別（唯讀顯示）
        for (int i = 0; i < item.Employee.PreferredShiftIds.Count; i++)
        {
            var shift = EnabledShifts.FirstOrDefault(s => s.Id == item.Employee.PreferredShiftIds[i]);
            if (shift is not null)
                EmployeeDetailPriorityShifts.Add($"{i + 1}. {shift.Alias}");
        }

        IsEmployeeDetailOpen = true;
    }

    [RelayCommand]
    private void CloseEmployeeDetail()
    {
        IsEmployeeDetailOpen = false;
        EmployeeDetailItem   = null;
        EmployeeDetailEntries.Clear();
        EmployeeDetailRules.Clear();
        EmployeeDetailDayOffs.Clear();
        EmployeeDetailPriorityShifts.Clear();
        EmployeeDetailNewDayOff = null;
    }

    [RelayCommand]
    private async Task AddEmployeeDetailDayOffAsync()
    {
        if (EmployeeDetailItem is null || CurrentSchedule is null || !EmployeeDetailNewDayOff.HasValue) return;
        var day   = EmployeeDetailNewDayOff.Value;
        var empId = EmployeeDetailItem.Employee.Id;

        // 若該日已有排班，先移除（規範：新增休息日後偵測並剃除已有排班）
        var entriesToRemove = CurrentSchedule.Entries
            .Where(e => e.EmployeeId == empId && e.Date.Day == day)
            .Select(e => e.Id)
            .ToList();
        if (entriesToRemove.Count > 0)
            await _entryService.RemoveEntriesAsync(entriesToRemove);

        var dayOffs = CurrentSchedule.EmployeeDayOffs.ToList();
        var slot    = dayOffs.FirstOrDefault(d => d.EmployeeId == empId);
        if (slot is null) { slot = new EmployeeDayOff { EmployeeId = empId, Days = new() }; dayOffs.Add(slot); }
        if (!slot.Days.Contains(day)) { slot.Days.Add(day); slot.Days.Sort(); }
        await _scheduleService.UpdateEmployeeDayOffsAsync(CurrentSchedule.Id, dayOffs);

        EmployeeDetailNewDayOff = null;
        await LoadScheduleAsync();

        // 同步更新浮層中的休息日清單與員工工作量卡片
        var newItem = EmployeeWorkloads.FirstOrDefault(w => w.Employee.Id == empId);
        if (newItem is not null) EmployeeDetailItem = newItem;
        EmployeeDetailDayOffs.Clear();
        var updated = CurrentSchedule?.EmployeeDayOffs.FirstOrDefault(d => d.EmployeeId == empId);
        if (updated is not null) foreach (var d in updated.Days) EmployeeDetailDayOffs.Add(d);

        _snackbarService.ShowSuccess(entriesToRemove.Count > 0
            ? $"已設為休息日，同時移除 {entriesToRemove.Count} 筆排班"
            : "已設為休息日");
    }

    [RelayCommand]
    private async Task RemoveEmployeeDetailDayOffAsync(int day)
    {
        if (EmployeeDetailItem is null || CurrentSchedule is null) return;
        var empId = EmployeeDetailItem.Employee.Id;

        var dayOffs = CurrentSchedule.EmployeeDayOffs.ToList();
        var slot    = dayOffs.FirstOrDefault(d => d.EmployeeId == empId);
        if (slot is null) return;
        slot.Days.Remove(day);
        if (slot.Days.Count == 0) dayOffs.Remove(slot);
        await _scheduleService.UpdateEmployeeDayOffsAsync(CurrentSchedule.Id, dayOffs);

        await LoadScheduleAsync();

        var newItem = EmployeeWorkloads.FirstOrDefault(w => w.Employee.Id == empId);
        if (newItem is not null) EmployeeDetailItem = newItem;
        EmployeeDetailDayOffs.Clear();
        var updated = CurrentSchedule?.EmployeeDayOffs.FirstOrDefault(d => d.EmployeeId == empId);
        if (updated is not null) foreach (var d in updated.Days) EmployeeDetailDayOffs.Add(d);
    }

    // ── 衝突 ──────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflicts))]
    private int _conflictCount;
    public bool HasConflicts => ConflictCount > 0;
    [ObservableProperty] private bool _isConflictPanelOpen;
    public ObservableCollection<ScheduleConflict> ConflictItems { get; } = new();
    public ObservableCollection<ShiftDayAssignmentItem> ShiftDayAssignments { get; }
    public bool HasShiftAssignments => ShiftDayAssignments.Count > 0;
    public bool HasMoreShiftsToAdd =>
        ShiftDayAssignments.Count(a => a.SelectedShift is not null) < EnabledShifts.Count;

    public ObservableCollection<WorkDayConditionItem> WorkDayConditions { get; } = new();
    public bool HasWorkDayConditions => WorkDayConditions.Count > 0;

    public ObservableCollection<EmployeeConstraintItem> EmployeeConstraints { get; } = new();
    public bool HasEmployeeConstraints => EmployeeConstraints.Count > 0;

    public ObservableCollection<EmployeeWorkloadItem> EmployeeWorkloads { get; } = new();

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

        var employee = QuickAddEmployee;
        var date     = QuickAddDate;
        var shift    = QuickAddShift;

        var added = await _entryService.AddEntryAsync(new ScheduleEntry
        {
            MonthlyScheduleId = CurrentSchedule.Id,
            EmployeeId = employee.Id,
            Date = date,
            ShiftSettingId = shift.Id,
        });

        IsQuickAdding = false;
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess($"已新增 {employee.Name} {date:MM/dd} {shift.Alias}");
        PushUndo(new UndoAction($"新增 {employee.Name} {date:MM/dd} {shift.Alias}",
            () => _entryService.RemoveEntryAsync(added.Id)));
    }

    // ══════════════════════════════════════════
    // 功能二：右鍵選單操作
    // ══════════════════════════════════════════

    [RelayCommand]
    public async Task DeleteEntryAsync(int entryId)
    {
        var entry = CurrentSchedule?.Entries.FirstOrDefault(e => e.Id == entryId);
        var restore = entry is null ? null : new ScheduleEntry
        {
            MonthlyScheduleId = entry.MonthlyScheduleId,
            EmployeeId        = entry.EmployeeId,
            Date              = entry.Date,
            ShiftSettingId    = entry.ShiftSettingId,
            Note              = entry.Note,
        };
        var label = $"{entry?.Employee?.Name ?? "員工"} {entry?.Date:MM/dd} {entry?.ShiftSetting?.Alias ?? ""}".Trim();

        await _entryService.RemoveEntryAsync(entryId);
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess("排班已刪除");

        if (restore is not null)
            PushUndo(new UndoAction($"刪除 {label}",
                () => _entryService.AddEntryAsync(restore)));
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

        var added = await _entryService.AddEntryAsync(new ScheduleEntry
        {
            MonthlyScheduleId = targetSchedule.Id,
            EmployeeId = entry.EmployeeId,
            Date = targetDate,
            ShiftSettingId = entry.ShiftSettingId,
        });

        await LoadScheduleAsync();
        _snackbarService.ShowSuccess($"已複製到 {targetDate:MM/dd}");
        PushUndo(new UndoAction($"複製 {entry.Employee?.Name ?? "員工"} 到 {targetDate:MM/dd}",
            () => _entryService.RemoveEntryAsync(added.Id)));
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

        var original        = CurrentSchedule?.Entries.FirstOrDefault(e => e.Id == _editEntryId);
        var originalShiftId = original?.ShiftSettingId ?? EditEntryShift.Id;
        var originalNote    = original?.Note ?? string.Empty;
        var entryLabel      = $"{original?.Employee?.Name ?? "員工"} {original?.Date:MM/dd}";
        var id              = _editEntryId;

        await _entryService.UpdateEntryAsync(id, EditEntryShift.Id, EditEntryNote);
        IsEditEntryOpen = false;
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess("排班已更新");

        PushUndo(new UndoAction($"編輯 {entryLabel} 排班",
            () => _entryService.UpdateEntryAsync(id, originalShiftId, originalNote)));
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

        var addedEntries = await _entryService.AddEntriesAsync(entriesToAdd);
        IsBatchMode = false;
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess(
            $"已新增 {addedEntries.Count} 筆排班（略過 {entriesToAdd.Count - addedEntries.Count} 筆重複）");

        if (addedEntries.Count > 0)
        {
            var ids = addedEntries.Select(e => e.Id).ToList();
            PushUndo(new UndoAction(
                $"批次新增 {BatchEmployee?.Name ?? "員工"} {addedEntries.Count} 筆排班",
                () => _entryService.RemoveEntriesAsync(ids)));
        }
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

        var addedEntries = await _entryService.AddEntriesAsync(entriesToAdd);
        await LoadScheduleAsync();

        if (addedEntries.Count == 0)
            _snackbarService.ShowError("本週已有相同排班，無需複製");
        else
        {
            _snackbarService.ShowSuccess($"已複製 {addedEntries.Count} 筆排班到本週");
            var ids = addedEntries.Select(e => e.Id).ToList();
            PushUndo(new UndoAction($"複製上週 {addedEntries.Count} 筆排班",
                () => _entryService.RemoveEntriesAsync(ids)));
        }
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

    partial void OnSelectedYearChanged(int value) { OnPropertyChanged(nameof(CalendarTitle)); ClearUndoStack(); _ = LoadForMonthChangeAsync(); }
    partial void OnSelectedMonthChanged(int value) { OnPropertyChanged(nameof(CalendarTitle)); ClearUndoStack(); _ = LoadForMonthChangeAsync(); }
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
    partial void OnDayViewDayChanged(CalendarDay? value)
    {
        OnPropertyChanged(nameof(DayViewDayIsClosed));
        OnPropertyChanged(nameof(DayViewDayHasOverride));
    }
    public bool DayViewDayIsClosed    => DayViewDay?.IsClosed ?? false;
    public bool DayViewDayHasOverride =>
        DayViewDay is not null &&
        CurrentSchedule?.ShiftDateOverrides.Any(o => o.Day == DayViewDay.Date.Day) == true;
    partial void OnSelectedDateChanged(DateOnly value) { OnPropertyChanged(nameof(CalendarTitle)); BuildCalendarView(); }
    partial void OnSelectedEmployeeChanged(Employee? value)
    {
        BuildCalendarView();
        if (IsDayDetailOpen && DayDetailDay is not null)
            RebuildDayDetailGroups(DayDetailDay);
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
            // 還原套用班別
            foreach (var config in sourceSchedule.ShiftDayConfigs)
            {
                var shift = EnabledShifts.FirstOrDefault(s => s.Id == config.ShiftId);
                if (shift is null) continue;

                var usedIds  = ShiftDayAssignments
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
        ClearUndoStack();
        SelectedYear = CreateYear;
        SelectedMonth = CreateMonth;
        _snackbarService.ShowSuccess($"{CreateYear} 年 {CreateMonth} 月班表已建立");

        await LoadScheduleAsync();
    }

    [RelayCommand]
    private void OpenDayDetail(CalendarDay? day)
    {
        if (day is null || day.IsPlaceholder || CurrentSchedule is null) return;
        DayDetailDay = day;
        DayDetailTitle = $"{day.Date:yyyy年M月d日}（週{GetDayOfWeekText(day.Date.DayOfWeek)}）";
        RebuildDayDetailGroups(day);
        IsDayDetailOpen = true;
    }

    // 共用重建方法：評估邏輯與 BuildCalendarView 完全相同，確保規則異動時自動同步
    private void RebuildDayDetailGroups(CalendarDay day)
    {
        if (CurrentSchedule is null) return;
        DayDetailGroups.Clear();

        foreach (var shift in GetShiftsForDay(day.Date, CurrentSchedule))
        {
            var entries = CurrentSchedule.Entries
                .Where(e => e.Date == day.Date && e.ShiftSettingId == shift.Id && e.Employee is not null)
                .ToList();

            var v     = EvaluateShiftForDrop(day.Date, shift);
            var vCopy = EvaluateShiftForDropCopy(day.Date, shift);
            DayDetailGroups.Add(new ShiftBlock
            {
                ShiftSetting          = shift,
                Date                  = day.Date,
                EntryItems            = new ObservableCollection<EntryItem>(
                    entries.Select(e => new EntryItem { EntryId = e.Id, Employee = e.Employee!, Date = day.Date, ShiftSetting = shift })),
                IsDisabled            = v.IsBlocked,
                DisabledReason        = v.Reason,
                IsDisabledForCopy     = vCopy.IsBlocked,
                DisabledReasonForCopy = vCopy.Reason,
            });
        }
    }

    [RelayCommand]
    private void CloseDayDetail()
    {
        IsDayDetailOpen = false;
        DayDetailDay    = null;
    }

    // 月/周視圖：日期詳情浮層中的店休切換
    [RelayCommand]
    private async Task SetDayDetailDayClosedAsync()
    {
        if (CurrentSchedule is null || DayDetailDay is null) return;
        var date = DayDetailDay.Date;

        var entryIds = CurrentSchedule.Entries.Where(e => e.Date == date).Select(e => e.Id).ToList();
        if (entryIds.Count > 0)
            await _entryService.RemoveEntriesAsync(entryIds);

        var newClosedDays = CurrentSchedule.ClosedDays.ToList();
        if (!newClosedDays.Contains(date.Day))
        {
            newClosedDays.Add(date.Day);
            newClosedDays.Sort();
        }
        await _scheduleService.UpdateClosedDaysAsync(CurrentSchedule.Id, newClosedDays);

        IsDayDetailOpen = false;
        DayDetailDay = null;
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess($"{date:MM/dd} 已設為店休日");
    }

    [RelayCommand]
    private async Task SetDayDetailDayOpenAsync()
    {
        if (CurrentSchedule is null || DayDetailDay is null) return;
        var date = DayDetailDay.Date;

        var newClosedDays = CurrentSchedule.ClosedDays.Where(d => d != date.Day).ToList();
        await _scheduleService.UpdateClosedDaysAsync(CurrentSchedule.Id, newClosedDays);

        IsDayDetailOpen = false;
        DayDetailDay = null;
        await LoadScheduleAsync();

        // 重新開啟讓使用者看到啟用的班別
        var updatedDay = CalendarDays.FirstOrDefault(d => d.Date == date);
        if (updatedDay is not null)
            OpenDayDetail(updatedDay);

        _snackbarService.ShowSuccess($"{date:MM/dd} 已設為上班日");
    }

    // 日視圖：標題列中的店休切換
    [RelayCommand]
    private async Task SetDayViewDayClosedAsync()
    {
        if (CurrentSchedule is null || DayViewDay is null) return;
        var date = DayViewDay.Date;

        var entryIds = CurrentSchedule.Entries.Where(e => e.Date == date).Select(e => e.Id).ToList();
        if (entryIds.Count > 0)
            await _entryService.RemoveEntriesAsync(entryIds);

        var newClosedDays = CurrentSchedule.ClosedDays.ToList();
        if (!newClosedDays.Contains(date.Day))
        {
            newClosedDays.Add(date.Day);
            newClosedDays.Sort();
        }
        await _scheduleService.UpdateClosedDaysAsync(CurrentSchedule.Id, newClosedDays);

        await LoadScheduleAsync();
        _snackbarService.ShowSuccess($"{date:MM/dd} 已設為店休日");
    }

    [RelayCommand]
    private async Task SetDayViewDayOpenAsync()
    {
        if (CurrentSchedule is null || DayViewDay is null) return;
        var date = DayViewDay.Date;

        var newClosedDays = CurrentSchedule.ClosedDays.Where(d => d != date.Day).ToList();
        await _scheduleService.UpdateClosedDaysAsync(CurrentSchedule.Id, newClosedDays);

        await LoadScheduleAsync();
        _snackbarService.ShowSuccess($"{date:MM/dd} 已設為上班日");
    }

    // ── 單日班別覆寫 ────────────────────────────────────────────────

    [RelayCommand]
    private void StartShiftOverrideEdit()
    {
        if (DayDetailDay is null || CurrentSchedule is null) return;
        PopulateShiftOverrideCells(DayDetailDay.Date);
        IsShiftOverrideEditing = true;
    }

    [RelayCommand]
    private void OpenShiftOverrideForDayView()
    {
        if (DayViewDay is null || CurrentSchedule is null) return;
        OpenDayDetail(DayViewDay);
        PopulateShiftOverrideCells(DayViewDay.Date);
        IsShiftOverrideEditing = true;
    }

    private void PopulateShiftOverrideCells(DateOnly date)
    {
        ShiftOverrideCells.Clear();
        var existing = CurrentSchedule!.ShiftDateOverrides.FirstOrDefault(o => o.Day == date.Day);
        var dow = (int)date.DayOfWeek;

        foreach (var shift in EnabledShifts)
        {
            bool isChecked = existing is not null
                ? existing.ShiftIds.Contains(shift.Id)
                : CurrentSchedule.ShiftDayConfigs.Count == 0 ||
                  CurrentSchedule.ShiftDayConfigs.Any(c => c.ShiftId == shift.Id && c.DaysOfWeek.Contains(dow));

            ShiftOverrideCells.Add(new ShiftOverrideCell { Shift = shift, IsChecked = isChecked });
        }
    }

    [RelayCommand]
    private void CancelShiftOverrideEdit()
    {
        IsShiftOverrideEditing = false;
        ShiftOverrideCells.Clear();
    }

    [RelayCommand]
    private async Task SaveShiftOverrideAsync()
    {
        if (CurrentSchedule is null || DayDetailDay is null) return;
        var day = DayDetailDay.Date.Day;
        var selectedIds = ShiftOverrideCells.Where(c => c.IsChecked).Select(c => c.Shift.Id).ToList();
        var overrides = CurrentSchedule.ShiftDateOverrides.Where(o => o.Day != day).ToList();
        overrides.Add(new ShiftDateOverride { Day = day, ShiftIds = selectedIds });
        await _scheduleService.UpdateShiftDateOverridesAsync(CurrentSchedule.Id, overrides);

        IsShiftOverrideEditing = false;
        ShiftOverrideCells.Clear();
        IsDayDetailOpen = false;
        DayDetailDay = null;
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess($"{DayDetailDay?.Date:MM/dd} 班別設定已儲存");
    }

    [RelayCommand]
    private async Task ClearShiftOverrideAsync()
    {
        if (CurrentSchedule is null || DayDetailDay is null) return;
        var day = DayDetailDay.Date.Day;
        var overrides = CurrentSchedule.ShiftDateOverrides.Where(o => o.Day != day).ToList();
        await _scheduleService.UpdateShiftDateOverridesAsync(CurrentSchedule.Id, overrides);

        IsShiftOverrideEditing = false;
        ShiftOverrideCells.Clear();
        IsDayDetailOpen = false;
        DayDetailDay = null;
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess("已清除當日自訂班別，恢復月設定");
    }

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

    // ── 衝突面板 ────────────────────────────────────────────────────
    [RelayCommand]
    private async Task CheckConflictsAsync()
    {
        if (CurrentSchedule is null) return;

        ConflictCount = await _conflictService.RecheckAsync(CurrentSchedule.Id);

        var items = await _conflictService.GetAsync(CurrentSchedule.Id);
        ConflictItems.Clear();
        foreach (var c in items) ConflictItems.Add(c);

        IsConflictPanelOpen = true;

        if (ConflictCount == 0)
            _snackbarService.ShowSuccess("太棒了！目前班表無任何規則衝突");
    }

    [RelayCommand]
    private void CloseConflictPanel() => IsConflictPanelOpen = false;

    [RelayCommand]
    private async Task RemoveEntryFromCardAsync()
    {
        var entry = CurrentSchedule?.Entries.FirstOrDefault(e => e.Id == _entryCardEntryId);
        var restore = entry is null ? null : new ScheduleEntry
        {
            MonthlyScheduleId = entry.MonthlyScheduleId,
            EmployeeId        = entry.EmployeeId,
            Date              = entry.Date,
            ShiftSettingId    = entry.ShiftSettingId,
            Note              = entry.Note,
        };
        var label = $"{entry?.Employee?.Name ?? "員工"} {entry?.Date:MM/dd} {entry?.ShiftSetting?.Alias ?? ""}".Trim();

        IsEntryCardOpen = false;
        IsDayDetailOpen = false;
        await _entryService.RemoveEntryAsync(_entryCardEntryId);
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess("已從班表移除");

        if (restore is not null)
            PushUndo(new UndoAction($"移除 {label}",
                () => _entryService.AddEntryAsync(restore)));
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

    private static readonly (DayOfWeek Day, string Label)[] _dayOrder =
    [
        (DayOfWeek.Monday,    "一"),
        (DayOfWeek.Tuesday,   "二"),
        (DayOfWeek.Wednesday, "三"),
        (DayOfWeek.Thursday,  "四"),
        (DayOfWeek.Friday,    "五"),
        (DayOfWeek.Saturday,  "六"),
        (DayOfWeek.Sunday,    "日"),
    ];

    [RelayCommand]
    private void AddWorkDayCondition()
    {
        var item = new WorkDayConditionItem();
        foreach (var (day, label) in _dayOrder)
        {
            var closed = CreateClosedDayOptions.FirstOrDefault(o => o.Day == day)?.IsChecked ?? false;
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

        // 選第一個可用群組的代表類型（整群互斥，不能選衝突群組的任一類型）
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

    // ══════════════════════════════════════════
    // 自動排班流程（獨立於建立班表）
    // ══════════════════════════════════════════
    [RelayCommand]
    public async Task StartAutoAssignAsync()
    {
        if (CurrentSchedule is null) return;
        CreateYear  = SelectedYear;
        CreateMonth = SelectedMonth;
        WorkDayConditions.Clear();
        EmployeeConstraints.Clear();

        var settings = await _shopSettingService.GetAsync();
        foreach (var option in CreateClosedDayOptions)
            option.IsChecked = settings?.ClosedDaysOfWeek.Contains((int)option.Day) ?? false;

        // 還原上班日條件
        foreach (var config in CurrentSchedule.WorkDayConditionConfigs)
        {
            var condItem = new WorkDayConditionItem { MinPerDay = config.MinPerDay, MaxPerShift = config.MaxPerShift };
            foreach (var (day, label) in _dayOrder)
            {
                var closed      = CreateClosedDayOptions.FirstOrDefault(o => o.Day == day)?.IsChecked ?? false;
                var alreadyUsed = WorkDayConditions.SelectMany(c => c.DayCells).Any(c => c.Day == day && c.IsChecked);
                var cell = new WorkDayConditionCell { Day = day, Label = label, IsShopClosed = closed, IsAlreadyUsed = alreadyUsed };
                if (!closed && !alreadyUsed) cell.IsChecked = config.DaysOfWeek.Contains((int)day);
                cell.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(WorkDayConditionCell.IsChecked)) RefreshWorkDayConditionUsed();
                };
                condItem.DayCells.Add(cell);
            }
            WorkDayConditions.Add(condItem);
        }
        if (WorkDayConditions.Count > 0) RefreshWorkDayConditionUsed();

        var closedDays = CurrentSchedule.ClosedDays;

        // 還原休假日
        foreach (var dayOff in CurrentSchedule.EmployeeDayOffs)
        {
            var emp = ActiveEmployees.FirstOrDefault(e => e.Id == dayOff.EmployeeId);
            if (emp is null || dayOff.Days.Count == 0) continue;
            var item = new EmployeeConstraintItem { SelectedEmployee = emp, ConstraintType = EmployeeConstraintType.DayOff };
            foreach (var d in dayOff.Days.OrderBy(x => x)) item.DayOffDays.Add(d);
            item.InitializeDayCells(CreateYear, CreateMonth, closedDays);
            EmployeeConstraints.Add(item);
        }

        // 還原強制上班日
        foreach (var workDay in CurrentSchedule.EmployeeWorkDays)
        {
            var emp = ActiveEmployees.FirstOrDefault(e => e.Id == workDay.EmployeeId);
            if (emp is null || workDay.Days.Count == 0) continue;
            var item = new EmployeeConstraintItem { SelectedEmployee = emp, ConstraintType = EmployeeConstraintType.WorkDay };
            foreach (var d in workDay.Days.OrderBy(x => x)) item.WorkDayDays.Add(d);
            item.InitializeDayCells(CreateYear, CreateMonth, closedDays);
            EmployeeConstraints.Add(item);
        }

        // 還原排除自動排班
        foreach (var excludedId in CurrentSchedule.ExcludeFromAutoAssignIds)
        {
            var emp = ActiveEmployees.FirstOrDefault(e => e.Id == excludedId);
            if (emp is null) continue;
            EmployeeConstraints.Add(new EmployeeConstraintItem { SelectedEmployee = emp, ConstraintType = EmployeeConstraintType.ExcludeAutoAssign });
        }

        // 還原優先班別（從員工資料）
        foreach (var emp in ActiveEmployees.Where(e => e.PreferredShiftIds.Count > 0))
        {
            var item = new EmployeeConstraintItem { SelectedEmployee = emp, ConstraintType = EmployeeConstraintType.ShiftPriority };
            foreach (var sid in emp.PreferredShiftIds)
            {
                var shift = EnabledShifts.FirstOrDefault(s => s.Id == sid);
                if (shift is not null) item.PriorityShifts.Add(shift);
            }
            if (item.PriorityShifts.Count > 0) EmployeeConstraints.Add(item);
        }

        IsQuickAdding = false;
        IsBatchMode   = false;
        IsAutoAssigning = true;
    }

    [RelayCommand]
    public void CancelAutoAssign()
    {
        WorkDayConditions.Clear();
        EmployeeConstraints.Clear();
        IsAutoAssigning = false;
    }

    [RelayCommand]
    public async Task ConfirmAutoAssignAsync()
    {
        if (CurrentSchedule is null) return;

        // 收集所有自動排班設定，一次性寫入（避免 EF Core HasConversion change tracking 失效導致部分欄位未儲存）
        var conditionConfigs = WorkDayConditions
            .Select(c => new WorkDayConditionConfig
            {
                MinPerDay   = c.MinPerDay,
                MaxPerShift = c.MaxPerShift,
                DaysOfWeek  = c.DayCells.Where(d => d.IsChecked).Select(d => (int)d.Day).ToList(),
            })
            .Where(c => c.DaysOfWeek.Count > 0)
            .ToList();

        var dayOffList = EmployeeConstraints
            .Where(c => c.ConstraintType == EmployeeConstraintType.DayOff
                     && c.SelectedEmployee is not null && c.DayOffDays.Count > 0)
            .GroupBy(c => c.SelectedEmployee!.Id)
            .Select(g => new EmployeeDayOff { EmployeeId = g.Key, Days = g.SelectMany(c => c.DayOffDays).Distinct().OrderBy(d => d).ToList() })
            .ToList();

        var workDayList = EmployeeConstraints
            .Where(c => c.ConstraintType == EmployeeConstraintType.WorkDay
                     && c.SelectedEmployee is not null && c.WorkDayDays.Count > 0)
            .GroupBy(c => c.SelectedEmployee!.Id)
            .Select(g => new EmployeeWorkDay { EmployeeId = g.Key, Days = g.SelectMany(c => c.WorkDayDays).Distinct().OrderBy(d => d).ToList() })
            .ToList();

        var excludeIds = EmployeeConstraints
            .Where(c => c.ConstraintType == EmployeeConstraintType.ExcludeAutoAssign && c.SelectedEmployee is not null)
            .Select(c => c.SelectedEmployee!.Id)
            .Distinct()
            .ToList();

        await _scheduleService.UpdateAutoAssignConfigAsync(CurrentSchedule.Id, conditionConfigs, dayOffList, workDayList, excludeIds);

        // 儲存優先班別至員工資料（含清除：未出現在約束清單的員工視為已移除偏好）
        var preferredMap = EmployeeConstraints
            .Where(c => c.ConstraintType == EmployeeConstraintType.ShiftPriority && c.SelectedEmployee is not null)
            .ToDictionary(c => c.SelectedEmployee!.Id, c => c.PriorityShifts.Select(s => s.Id).ToList());

        foreach (var emp in ActiveEmployees)
        {
            var newIds = preferredMap.TryGetValue(emp.Id, out var ids) ? ids : [];
            if (newIds.SequenceEqual(emp.PreferredShiftIds)) continue;
            await _employeeService.UpdatePreferredShiftsAsync(emp.Id, newIds);
            emp.PreferredShiftIds = newIds;
        }

        // 取得最新班表，清空現有排班後重新排班
        var freshSchedule = await _scheduleService.GetAsync(SelectedYear, SelectedMonth);
        if (freshSchedule is null) return;

        await _entryService.ClearAllEntriesAsync(freshSchedule.Id);
        freshSchedule.Entries.Clear();

        var (added, gaps, gapDays) = await AutoAssignAsync(freshSchedule);
        await _scheduleService.UpdateStaffingGapDaysAsync(freshSchedule.Id, gapDays);

        IsAutoAssigning = false;
        WorkDayConditions.Clear();
        EmployeeConstraints.Clear();
        ClearUndoStack();

        _snackbarService.ShowSuccess(added > 0
            ? $"自動排班完成，新增 {added} 筆排班"
            : "自動排班完成（無需新增排班）");
        if (gaps.Count > 0)
        {
            var preview = string.Join("、", gaps.Take(3));
            var suffix  = gaps.Count > 3 ? $"…等 {gaps.Count} 筆" : string.Empty;
            _snackbarService.ShowWarning($"部分班次人數不足：{preview}{suffix}");
        }

        await LoadScheduleAsync();
    }

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
    // 自動排班演算法
    // ══════════════════════════════════════════
    private async Task<(int Added, List<string> Gaps, List<int> GapDays)> AutoAssignAsync(MonthlySchedule schedule)
    {
        // 建立「星期幾 → 條件」對應（每天最多一條條件）
        var condByDow = new Dictionary<DayOfWeek, WorkDayConditionItem>();
        foreach (var cond in WorkDayConditions)
            foreach (var cell in cond.DayCells.Where(c => c.IsChecked && !c.IsShopClosed))
                condByDow[cell.Day] = cond;

        if (condByDow.Count == 0) return (0, [], []);

        // 建立「有條件的上班日」清單
        var daysInMonth = DateTime.DaysInMonth(schedule.Year, schedule.Month);
        var workDays = new List<(DateOnly Date, List<ShiftSetting> Shifts, WorkDayConditionItem Cond)>();

        for (int d = 1; d <= daysInMonth; d++)
        {
            var date = new DateOnly(schedule.Year, schedule.Month, d);
            if (schedule.ClosedDays.Contains(d)) continue;
            if (!condByDow.TryGetValue(date.DayOfWeek, out var cond)) continue;

            var shiftsForDay = GetShiftsForDay(date, schedule);
            if (shiftsForDay.Count > 0)
                workDays.Add((date, shiftsForDay, cond));
        }

        if (workDays.Count == 0 || ActiveEmployees.Count == 0) return (0, [], []);

        // 排除不參加自動排班的員工
        var excludeSet = schedule.ExcludeFromAutoAssignIds.ToHashSet();
        var employees  = ActiveEmployees.Where(e => !excludeSet.Contains(e.Id)).ToList();

        if (employees.Count == 0) return (0, [], []);

        // Phase 0：建立跳過表（休假日 + 強制上班日）
        var skipDays   = new Dictionary<int, HashSet<int>>();
        var forcedDays = new Dictionary<int, HashSet<int>>();
        foreach (var dayOff  in schedule.EmployeeDayOffs)   skipDays[dayOff.EmployeeId]     = dayOff.Days.ToHashSet();
        foreach (var workDay in schedule.EmployeeWorkDays)   forcedDays[workDay.EmployeeId]  = workDay.Days.ToHashSet();

        bool ShouldSkip(Employee emp, DateOnly date) =>
            (skipDays.TryGetValue(emp.Id, out var offs)   && offs.Contains(date.Day)) ||
            (forcedDays.TryGetValue(emp.Id, out var fds)  && !fds.Contains(date.Day));

        // 追蹤各員工總排班數與各班別排班數，用於公平排序與優先班別配額控制
        // 呼叫前已清空 schedule.Entries，起始值皆為 0
        var assignedCount    = employees.ToDictionary(e => e.Id, _ => 0);
        var assignedPerShift = employees.ToDictionary(e => e.Id, _ => new Dictionary<int, int>());

        // 計算每個班別的「公平配額」：整月該班總人次 / 員工人數
        // 優先班別員工只在自己的配額內享有優先權，配額耗盡後與一般員工平等競爭
        // 確保優先班別是「同樣總班數內優先分配到偏好班別」而非「壟斷整個班別」
        var totalSlotsPerShift = new Dictionary<int, int>();
        foreach (var (_, wdShifts, wdCond) in workDays)
            foreach (var s in wdShifts)
                totalSlotsPerShift[s.Id] = totalSlotsPerShift.GetValueOrDefault(s.Id, 0) + wdCond.MaxPerShift;
        var preferredQuota = totalSlotsPerShift.ToDictionary(
            kvp => kvp.Key,
            kvp => (double)kvp.Value / employees.Count);

        var entriesToSave = new List<ScheduleEntry>();
        var gaps = new List<string>();
        var gapDaySet = new HashSet<int>();
        var shiftLookup = (IReadOnlyDictionary<int, ShiftSetting>)EnabledShifts.ToDictionary(s => s.Id);

        foreach (var (date, shifts, cond) in workDays)
        {
            // Phase 1：每班填至 MaxPerShift
            // 主排序：已排班數（維持公平分配）
            // 次排序：有設定此班為優先的員工優先（在同工作量內取得偏好班別）
            foreach (var shift in shifts)
            {
                int assignedToShift = schedule.Entries.Count(e => e.Date == date && e.ShiftSettingId == shift.Id);
                if (assignedToShift >= cond.MaxPerShift) continue;

                var eligible = employees
                    .Where(e => !ShouldSkip(e, date))
                    .OrderBy(e => assignedCount[e.Id])
                    .ThenBy(e => {
                        if (!e.PreferredShiftIds.Contains(shift.Id)) return double.MaxValue;
                        int shiftCount = assignedPerShift[e.Id].GetValueOrDefault(shift.Id, 0);
                        // 超過公平配額後不再享有優先權，退回一般競爭
                        if (shiftCount >= preferredQuota.GetValueOrDefault(shift.Id, 0)) return double.MaxValue;
                        // 配額內：此班比例越低者越優先（公平分散）
                        int total = assignedCount[e.Id];
                        return total == 0 ? 0.0 : (double)shiftCount / total;
                    })
                    .ThenBy(e => {
                        if (!e.PreferredShiftIds.Contains(shift.Id)) return int.MaxValue;
                        int shiftCount = assignedPerShift[e.Id].GetValueOrDefault(shift.Id, 0);
                        if (shiftCount >= preferredQuota.GetValueOrDefault(shift.Id, 0)) return int.MaxValue;
                        return e.PreferredShiftIds.IndexOf(shift.Id);
                    });

                foreach (var emp in eligible)
                {
                    if (assignedToShift >= cond.MaxPerShift) break;

                    if (schedule.Entries.Any(e =>
                        e.Date == date && e.ShiftSettingId == shift.Id && e.EmployeeId == emp.Id))
                        continue;

                    var ctx = new ShiftValidationContext(
                        Employee: emp, Date: date, TargetShift: shift,
                        Schedule: schedule, ActiveEmployees: employees, LaborLaw: _laborLaw,
                        ShiftLookup: shiftLookup);

                    if (ShiftRuleEngine.Evaluate(ctx).IsBlocked) continue;

                    var entry = new ScheduleEntry
                    {
                        MonthlyScheduleId = schedule.Id,
                        EmployeeId = emp.Id, Date = date, ShiftSettingId = shift.Id,
                    };
                    schedule.Entries.Add(entry);
                    entriesToSave.Add(entry);
                    assignedCount[emp.Id]++;
                    assignedPerShift[emp.Id][shift.Id] = assignedPerShift[emp.Id].GetValueOrDefault(shift.Id, 0) + 1;
                    assignedToShift++;
                }
            }

            // Phase 2：若當日人數仍不足 MinPerDay，跨班別補排（仍尊重 MaxPerShift）
            var todayEmpIds = schedule.Entries
                .Where(e => e.Date == date)
                .Select(e => e.EmployeeId)
                .Distinct()
                .ToHashSet();

            if (todayEmpIds.Count < cond.MinPerDay)
            {
                foreach (var emp in employees
                    .Where(e => !todayEmpIds.Contains(e.Id) && !ShouldSkip(e, date))
                    .OrderBy(e => assignedCount[e.Id]))
                {
                    if (todayEmpIds.Count >= cond.MinPerDay) break;

                    // 補排時：優先嘗試員工偏好的班別（配額內），再依原順序
                    var shiftsOrdered = shifts
                        .OrderBy(s => {
                            if (!emp.PreferredShiftIds.Contains(s.Id)) return 1;
                            int sc = assignedPerShift[emp.Id].GetValueOrDefault(s.Id, 0);
                            return sc < preferredQuota.GetValueOrDefault(s.Id, 0) ? 0 : 1;
                        })
                        .ThenBy(s => emp.PreferredShiftIds.Contains(s.Id)
                            ? emp.PreferredShiftIds.IndexOf(s.Id) : int.MaxValue);

                    foreach (var shift in shiftsOrdered)
                    {
                        int shiftCount = schedule.Entries.Count(e =>
                            e.Date == date && e.ShiftSettingId == shift.Id);
                        if (shiftCount >= cond.MaxPerShift) continue;

                        if (schedule.Entries.Any(e =>
                            e.Date == date && e.ShiftSettingId == shift.Id && e.EmployeeId == emp.Id))
                            continue;

                        var ctx = new ShiftValidationContext(
                            Employee: emp, Date: date, TargetShift: shift,
                            Schedule: schedule, ActiveEmployees: employees, LaborLaw: _laborLaw,
                            ShiftLookup: shiftLookup);

                        if (ShiftRuleEngine.Evaluate(ctx).IsBlocked) continue;

                        var entry = new ScheduleEntry
                        {
                            MonthlyScheduleId = schedule.Id,
                            EmployeeId = emp.Id, Date = date, ShiftSettingId = shift.Id,
                        };
                        schedule.Entries.Add(entry);
                        entriesToSave.Add(entry);
                        assignedCount[emp.Id]++;
                        assignedPerShift[emp.Id][shift.Id] = assignedPerShift[emp.Id].GetValueOrDefault(shift.Id, 0) + 1;
                        todayEmpIds.Add(emp.Id);
                        break;
                    }
                }

                if (todayEmpIds.Count < cond.MinPerDay)
                {
                    gaps.Add($"{date:MM/dd} 人力不足（{todayEmpIds.Count}/{cond.MinPerDay}）");
                    gapDaySet.Add(date.Day);
                }
            }
        }

        if (entriesToSave.Count > 0)
            await _entryService.AddEntriesAsync(entriesToSave);

        return (entriesToSave.Count, gaps, gapDaySet.ToList());
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
        _shiftLookupCache = EnabledShifts.ToDictionary(s => s.Id);
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

        var gapDays = CurrentSchedule.StaffingGapDays.ToHashSet();

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
                HasStaffingGap = !isClosed && gapDays.Contains(day),
                HolidayName = _monthHolidays.GetValueOrDefault(day),
            };

            if (!isClosed)
            {
                var shiftsForDay = GetShiftsForDay(date, CurrentSchedule);

                foreach (var shift in shiftsForDay)
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
            var isInCurrentMonth = date.Year == SelectedYear && date.Month == SelectedMonth;
            var isClosed = isInCurrentMonth && CurrentSchedule.ClosedDays.Contains(date.Day);

            var calDay = new CalendarDay
            {
                Date = date,
                Day = date.Day,
                DayOfWeekText = GetDayOfWeekText(date.DayOfWeek),
                IsToday      = date == DateOnly.FromDateTime(DateTime.Today),
                IsSelected   = date == SelectedDate,
                IsClosed     = isClosed,
                IsOutOfScope = !isInCurrentMonth,
                HasStaffingGap = !isClosed && isInCurrentMonth
                    && CurrentSchedule.StaffingGapDays.Contains(date.Day),
            };

            if (!isClosed && isInCurrentMonth)
            {
                var shiftsForDay = GetShiftsForDay(date, CurrentSchedule);

                foreach (var shift in shiftsForDay)
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
            var shiftsForDay = GetShiftsForDay(SelectedDate, CurrentSchedule);

            foreach (var shift in shiftsForDay)
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
    // 拖放排班
    // ══════════════════════════════════════════
    //
    // ── 操作對應（由 View 層決定入口）─────────────────────────────────
    //  1. 交換 (Swap)   — 拖到員工頭像 → EntryChip_Drop → SwapEmployeeAsync
    //                     略過規則：交換後雙方各自獨立不共班，NotWith 不適用
    //  2. 移動 (Move)   — 拖到班表空白 + 非 Ctrl → DropEmployeeAsync
    //                     規則：全部 #1-#6（能否共事 + 時間 + 工時）
    //  3. 複製 (Copy)   — 拖到班表空白 + Ctrl → DropEmployeeAsync(isCopy:true)
    //                     規則：僅群組 C（#5 時間重疊 + #6 每日工時上限）
    //  4. 新增 (Add)    — 從員工清單拖入（無來源 EntryId）→ DropEmployeeAsync
    //                     規則：全部 #1-#6
    //
    // ══════════════════════════════════════════

    // 拖曳進行時由 View 設定，供 EvaluateShiftForDrop 排除來源班次自身
    public int DragSourceEntryId { get; set; } = -1;

    // ── 操作②③④：由 ShiftBlock_Drop 呼叫（移動 / 複製 / 新增）───────
    public async Task DropEmployeeAsync(Employee employee, DateOnly date, ShiftSetting shift,
        int? sourceEntryId = null, bool isCopy = false)
    {
        if (CurrentSchedule is null) return;

        var alreadyExists = CurrentSchedule.Entries.Any(e =>
            e.EmployeeId == employee.Id &&
            e.Date == date &&
            e.ShiftSettingId == shift.Id &&
            e.Id != (sourceEntryId ?? -1));
        if (alreadyExists) return;

        await ExecuteAddOrMoveAsync(employee, date, shift, sourceEntryId, isCopy);
    }

    // ── 操作①：由 EntryChip_Drop 呼叫（頭像對頭像交換）────────────────
    public async Task SwapEmployeeAsync(Employee dragEmployee, int sourceEntryId, EntryItem targetEntry)
    {
        if (CurrentSchedule is null) return;

        var sourceEntry = CurrentSchedule.Entries.FirstOrDefault(e => e.Id == sourceEntryId);
        var destEntry   = CurrentSchedule.Entries.FirstOrDefault(e => e.Id == targetEntry.EntryId);
        if (sourceEntry is null || destEntry is null) return;
        if (sourceEntry.EmployeeId == destEntry.EmployeeId) return;

        // 對方若已在來源格則無法交換（結果會重複）
        var wouldDuplicate = CurrentSchedule.Entries.Any(e =>
            e.Date == sourceEntry.Date &&
            e.ShiftSettingId == sourceEntry.ShiftSettingId &&
            e.EmployeeId == destEntry.EmployeeId);
        if (wouldDuplicate)
        {
            _snackbarService.ShowError($"「{destEntry.Employee?.Name ?? "員工"}」已排在該班次，無法交換");
            return;
        }

        if (targetEntry.ShiftSetting is null) return;
        await ExecuteSwapAsync(dragEmployee, targetEntry.Date, targetEntry.ShiftSetting, sourceEntry, destEntry);
    }

    // ── 操作①：交換 ────────────────────────────────────────────────
    private async Task ExecuteSwapAsync(Employee employee, DateOnly date, ShiftSetting shift,
        ScheduleEntry sourceEntry, ScheduleEntry destEntry)
    {
        var srcRestore = SnapshotEntry(sourceEntry);
        var dstRestore = SnapshotEntry(destEntry);
        var empName  = employee.Name;
        var destName = destEntry.Employee?.Name ?? "員工";

        var added = await _entryService.AddEntriesAsync([
            new ScheduleEntry { MonthlyScheduleId = CurrentSchedule!.Id, EmployeeId = employee.Id,       Date = date,             ShiftSettingId = shift.Id                },
            new ScheduleEntry { MonthlyScheduleId = CurrentSchedule.Id,  EmployeeId = destEntry.EmployeeId, Date = sourceEntry.Date, ShiftSettingId = sourceEntry.ShiftSettingId },
        ]);
        await _entryService.RemoveEntriesAsync([sourceEntry.Id, destEntry.Id]);
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess($"已交換 {empName} 與 {destName} 的排班");

        if (added.Count == 2)
        {
            var addedIds = added.Select(e => e.Id).ToList();
            PushUndo(new UndoAction($"交換 {empName} 與 {destName} 的排班",
                async () =>
                {
                    await _entryService.RemoveEntriesAsync(addedIds);
                    await _entryService.AddEntriesAsync([srcRestore, dstRestore]);
                }));
        }
    }

    // ── 操作②③④：移動 / 複製 / 新增 ──────────────────────────────
    private async Task ExecuteAddOrMoveAsync(Employee employee, DateOnly date, ShiftSetting shift,
        int? sourceEntryId, bool isCopy)
    {
        // 移動（非複製）：快照來源以供還原
        ScheduleEntry? srcSnapshot = null;
        if (sourceEntryId.HasValue && !isCopy)
        {
            var src = CurrentSchedule!.Entries.FirstOrDefault(e => e.Id == sourceEntryId.Value);
            if (src is not null) srcSnapshot = SnapshotEntry(src);
        }

        var added = await _entryService.AddEntryAsync(new ScheduleEntry
        {
            MonthlyScheduleId = CurrentSchedule!.Id,
            EmployeeId        = employee.Id,
            Date              = date,
            ShiftSettingId    = shift.Id,
        });

        if (sourceEntryId.HasValue && !isCopy)
            await _entryService.RemoveEntryAsync(sourceEntryId.Value);

        await LoadScheduleAsync();

        var addedId = added.Id;
        if (sourceEntryId.HasValue && !isCopy)
            PushUndo(new UndoAction($"移動 {employee.Name} 到 {date:MM/dd} {shift.Alias}",
                async () =>
                {
                    await _entryService.RemoveEntryAsync(addedId);
                    if (srcSnapshot is not null) await _entryService.AddEntryAsync(srcSnapshot);
                }));
        else
            PushUndo(new UndoAction(
                isCopy ? $"複製 {employee.Name} 到 {date:MM/dd} {shift.Alias}"
                       : $"新增 {employee.Name} {date:MM/dd} {shift.Alias}",
                () => _entryService.RemoveEntryAsync(addedId)));
    }

    // ── 工具：建立 ScheduleEntry 快照（用於 Undo 還原）────────────
    private static ScheduleEntry SnapshotEntry(ScheduleEntry src) => new()
    {
        MonthlyScheduleId = src.MonthlyScheduleId,
        EmployeeId        = src.EmployeeId,
        Date              = src.Date,
        ShiftSettingId    = src.ShiftSettingId,
        Note              = src.Note,
    };

    // ── 規則評估：拖曳時標記哪些 ShiftBlock 不可放入 ────────────────
    // 由 BuildCalendarView 對每個 ShiftBlock 呼叫，SelectedEmployee ≠ null 時才評估
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
            LaborLaw:        _laborLaw,
            ShiftLookup:     _shiftLookupCache));
    }

    // 複製模式：僅群組 C（#7-#10）
    private ShiftValidationResult EvaluateShiftForDropCopy(DateOnly date, ShiftSetting shift)
    {
        if (SelectedEmployee is null || CurrentSchedule is null)
            return ShiftValidationResult.Allow;

        return ShiftRuleEngine.EvaluateForCopy(new ShiftValidationContext(
            Employee:        SelectedEmployee,
            Date:            date,
            TargetShift:     shift,
            Schedule:        CurrentSchedule,
            ActiveEmployees: ActiveEmployees,
            ExcludeEntryId:  DragSourceEntryId,
            LaborLaw:        _laborLaw,
            ShiftLookup:     _shiftLookupCache));
    }

    // BuildCalendarView 前更新，避免每個 ShiftBlock 重複建立
    private IReadOnlyDictionary<int, ShiftSetting> _shiftLookupCache = new Dictionary<int, ShiftSetting>();

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
            // 未勾選任何星期 → 視為全部上班日
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

file record CalendarDayDto(
    [property: JsonPropertyName("date")]        string Date,
    [property: JsonPropertyName("week")]        string Week,
    [property: JsonPropertyName("isHoliday")]   bool   IsHoliday,
    [property: JsonPropertyName("description")] string Description);
