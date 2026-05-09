using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using System.Collections.ObjectModel;

namespace ShopManager.ViewModels;

public partial class ScheduleViewModel
{
    // ── 員工本月排班詳情面板 ──────────────────────────────────────────────
    [ObservableProperty] private bool _isEmployeeDetailOpen;
    [ObservableProperty] private EmployeeWorkloadItem? _employeeDetailItem;
    [ObservableProperty] private int? _employeeDetailNewDayOff;

    public ObservableCollection<EmployeeDetailEntry>     EmployeeDetailEntries        { get; } = new();
    public ObservableCollection<EmployeeRuleDisplayItem> EmployeeDetailRules          { get; } = new();
    public ObservableCollection<int>                     EmployeeDetailDayOffs        { get; } = new();
    public ObservableCollection<string>                  EmployeeDetailPriorityShifts { get; } = new();

    public List<int> AvailableEmployeeDetailDays
    {
        get
        {
            if (CurrentSchedule is null) return [];
            var daysInMonth = DateTime.DaysInMonth(CurrentSchedule.Year, CurrentSchedule.Month);
            var closed      = CurrentSchedule.ClosedDays.ToHashSet();
            var already     = EmployeeDetailDayOffs.ToHashSet();
            return Enumerable.Range(1, daysInMonth)
                .Where(d => !closed.Contains(d) && !already.Contains(d))
                .ToList();
        }
    }

    [RelayCommand]
    private void OpenEmployeeDetail(EmployeeWorkloadItem item)
    {
        EmployeeDetailItem = item;
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
                var colleagues = CurrentSchedule.Entries
                    .Where(e => e.Date == entry.Date &&
                                e.EmployeeId != item.Employee.Id &&
                                e.Employee is not null)
                    .Select(e => e.Employee!)
                    .DistinctBy(e => e.Id)
                    .ToList();

                EmployeeDetailEntries.Add(new EmployeeDetailEntry
                {
                    DateText      = $"{entry.Date:MM/dd}",
                    DayOfWeekText = GetDayOfWeekText(entry.Date.DayOfWeek),
                    ShiftAlias    = entry.ShiftSetting!.Alias,
                    TimeRange     = $"{entry.ShiftSetting.StartTime:HH\\:mm} – {entry.ShiftSetting.EndTime:HH\\:mm}",
                    Colleagues    = colleagues,
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

        // 若該日已有排班，先移除（新增休息日後自動剔除衝突排班）
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

        var newItem = EmployeeWorkloads.FirstOrDefault(w => w.Employee.Id == empId);
        if (newItem is not null) EmployeeDetailItem = newItem;
        EmployeeDetailDayOffs.Clear();
        var updated = CurrentSchedule?.EmployeeDayOffs.FirstOrDefault(d => d.EmployeeId == empId);
        if (updated is not null) foreach (var d in updated.Days) EmployeeDetailDayOffs.Add(d);

        if (IsAutoAssigning)
            SyncDayOffToConstraints(empId, day, isAdding: true);

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

        if (IsAutoAssigning)
            SyncDayOffToConstraints(empId, day, isAdding: false);
    }

    private void SyncDayOffToConstraints(int empId, int day, bool isAdding)
    {
        var constraint = EmployeeConstraints.FirstOrDefault(c =>
            c.ConstraintType == EmployeeConstraintType.DayOff &&
            c.SelectedEmployee?.Id == empId);

        if (isAdding)
        {
            if (constraint is null)
            {
                var emp = ActiveEmployees.FirstOrDefault(e => e.Id == empId);
                if (emp is null) return;
                constraint = new EmployeeConstraintItem
                {
                    SelectedEmployee = emp,
                    ConstraintType   = EmployeeConstraintType.DayOff,
                };
                constraint.InitializeDayCells(SelectedYear, SelectedMonth, CurrentSchedule?.ClosedDays ?? []);
                EmployeeConstraints.Add(constraint);
            }
            var addCell = constraint.DayOffCells.FirstOrDefault(c => c.Day == day);
            if (addCell is not null) addCell.IsChecked = true;
        }
        else
        {
            if (constraint is null) return;
            var removeCell = constraint.DayOffCells.FirstOrDefault(c => c.Day == day);
            if (removeCell is not null) removeCell.IsChecked = false;
            if (constraint.DayOffDays.Count == 0)
                EmployeeConstraints.Remove(constraint);
        }
    }
}
