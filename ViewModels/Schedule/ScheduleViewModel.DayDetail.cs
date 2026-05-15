using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using ShopManager.Services;
using System.Collections.ObjectModel;

namespace ShopManager.ViewModels;

public partial class ScheduleViewModel
{
    // ── 日期詳情浮層附屬狀態 ─────────────────────────────────────────────
    [ObservableProperty] private bool _isEntryCardOpen;
    [ObservableProperty] private Employee? _entryCardEmployee;
    [ObservableProperty] private string _entryCardShiftInfo = string.Empty;
    private int _entryCardEntryId;

    public ObservableCollection<ShiftBlock> DayDetailGroups { get; } = new();

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

    // ── 衝突面板附屬狀態 ─────────────────────────────────────────────────
    [ObservableProperty] private bool _isConflictPanelOpen;
    public ObservableCollection<ScheduleConflict> ConflictItems { get; } = new();

    // ── 推薦員工面板 ──────────────────────────────────────────────────────
    [ObservableProperty] private bool _isRecommendPanelOpen;
    [ObservableProperty] private ShiftBlock? _recommendTargetShift;
    public ObservableCollection<RecommendEmployeeItem> RecommendCandidates { get; } = new();

    // ══════════════════════════════════════════
    // 日期詳情浮層
    // ══════════════════════════════════════════
    [RelayCommand]
    private void OpenDayDetail(CalendarDay? day)
    {
        if (day is null || day.IsPlaceholder || CurrentSchedule is null) return;
        DayDetailDay   = day;
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

    // ── 月/周視圖：日期詳情浮層中的店休切換 ─────────────────────────────
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
        DayDetailDay    = null;
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
        DayDetailDay    = null;
        await LoadScheduleAsync();

        // 重新開啟讓使用者看到啟用的班別
        var updatedDay = CalendarDays.FirstOrDefault(d => d.Date == date);
        if (updatedDay is not null)
            OpenDayDetail(updatedDay);

        _snackbarService.ShowSuccess($"{date:MM/dd} 已設為上班日");
    }

    // ── 日視圖：標題列中的店休切換 ─────────────────────────────────────
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

    // ══════════════════════════════════════════
    // 單日班別覆寫
    // ══════════════════════════════════════════
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
        var dow      = (int)date.DayOfWeek;

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
        var day         = DayDetailDay.Date.Day;
        var selectedIds = ShiftOverrideCells.Where(c => c.IsChecked).Select(c => c.Shift.Id).ToList();
        var overrides   = CurrentSchedule.ShiftDateOverrides.Where(o => o.Day != day).ToList();
        overrides.Add(new ShiftDateOverride { Day = day, ShiftIds = selectedIds });
        await _scheduleService.UpdateShiftDateOverridesAsync(CurrentSchedule.Id, overrides);

        IsShiftOverrideEditing = false;
        ShiftOverrideCells.Clear();
        IsDayDetailOpen = false;
        DayDetailDay    = null;
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess($"{DayDetailDay?.Date:MM/dd} 班別設定已儲存");
    }

    [RelayCommand]
    private async Task ClearShiftOverrideAsync()
    {
        if (CurrentSchedule is null || DayDetailDay is null) return;
        var day      = DayDetailDay.Date.Day;
        var overrides = CurrentSchedule.ShiftDateOverrides.Where(o => o.Day != day).ToList();
        await _scheduleService.UpdateShiftDateOverridesAsync(CurrentSchedule.Id, overrides);

        IsShiftOverrideEditing = false;
        ShiftOverrideCells.Clear();
        IsDayDetailOpen = false;
        DayDetailDay    = null;
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess("已清除當日自訂班別，恢復月設定");
    }

    // ══════════════════════════════════════════
    // 排班卡片 & 衝突面板
    // ══════════════════════════════════════════
    [RelayCommand]
    private void OpenEntryCard(EntryItem item)
    {
        EntryCardEmployee  = item.Employee;
        EntryCardShiftInfo = $"{item.Date:yyyy/M/d}  {item.ShiftSetting?.Alias ?? ""}";
        _entryCardEntryId  = item.EntryId;
        IsEntryCardOpen    = true;
    }

    [RelayCommand]
    private void CloseEntryCard() => IsEntryCardOpen = false;

    [RelayCommand]
    private async Task RemoveEntryFromCardAsync()
    {
        var entry   = CurrentSchedule?.Entries.FirstOrDefault(e => e.Id == _entryCardEntryId);
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
        await _entryService.RemoveEntryAsync(_entryCardEntryId);
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess("已從班表移除");

        if (restore is not null)
            PushUndo(new UndoAction($"移除 {label}",
                () => _entryService.AddEntryAsync(restore)));
    }

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
    private void OpenRecommendPanel(ShiftBlock block)
    {
        RecommendTargetShift = block;
        RecommendCandidates.Clear();

        if (CurrentSchedule is null) return;

        var shiftLookup = EnabledShifts.ToDictionary(s => s.Id);

        foreach (var emp in ActiveEmployees)
        {
            if (CurrentSchedule.Entries.Any(e =>
                    e.EmployeeId == emp.Id &&
                    e.Date == block.Date &&
                    e.ShiftSettingId == block.ShiftSetting.Id)) continue;

            var ctx = new ShiftValidationContext(
                Employee:        emp,
                Date:            block.Date,
                TargetShift:     block.ShiftSetting,
                Schedule:        CurrentSchedule,
                ActiveEmployees: ActiveEmployees,
                LaborLaw:        _laborLaw,
                ShiftLookup:     shiftLookup);

            if (ShiftRuleEngine.Evaluate(ctx).IsBlocked) continue;

            var stats = CurrentSchedule.Entries
                .Where(e => e.EmployeeId == emp.Id && e.ShiftSetting is not null)
                .GroupBy(e => e.ShiftSettingId)
                .Select(g => new EmployeeDetailShiftStat
                {
                    ShiftAlias = g.First().ShiftSetting!.Alias,
                    ColorHex   = g.First().ShiftSetting!.Color,
                    Count      = g.Count(),
                })
                .OrderBy(s => s.ShiftAlias)
                .ToList();

            int half = (int)Math.Ceiling(stats.Count / 2.0);
            RecommendCandidates.Add(new RecommendEmployeeItem
            {
                Employee       = emp,
                ShiftStatsRow1 = stats.Take(half).ToList(),
                ShiftStatsRow2 = stats.Skip(half).ToList(),
            });
        }

        IsRecommendPanelOpen = true;
    }

    [RelayCommand]
    private void CloseRecommendPanel()
    {
        IsRecommendPanelOpen = false;
        RecommendTargetShift = null;
        RecommendCandidates.Clear();
    }

    [RelayCommand]
    private async Task AssignRecommendedEmployeeAsync(RecommendEmployeeItem item)
    {
        if (RecommendTargetShift is null || CurrentSchedule is null) return;

        await _entryService.AddEntryAsync(new ScheduleEntry
        {
            MonthlyScheduleId = CurrentSchedule.Id,
            EmployeeId        = item.Employee.Id,
            Date              = RecommendTargetShift.Date,
            ShiftSettingId    = RecommendTargetShift.ShiftSetting.Id,
        });

        RecommendCandidates.Remove(item);
        await LoadScheduleAsync();
        if (DayDetailDay is not null) RebuildDayDetailGroups(DayDetailDay);
        _snackbarService.ShowSuccess($"{item.Employee.Name} 已排入");
    }
}
