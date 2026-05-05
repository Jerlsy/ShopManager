using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;

namespace ShopManager.ViewModels;

public partial class ScheduleViewModel
{
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

        IsQuickAdding   = false;
        IsBatchMode     = false;
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

        // 收集所有自動排班設定，一次性寫入（避免 EF Core HasConversion change tracking 失效）
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

        // 取得最新班表，只清除明天以後的排班（過去日期鎖定，保留供演算法計算）
        var freshSchedule = await _scheduleService.GetAsync(SelectedYear, SelectedMonth);
        if (freshSchedule is null) return;

        var tomorrow = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        await _entryService.ClearFutureEntriesAsync(freshSchedule.Id, tomorrow);
        var lockedEntries = freshSchedule.Entries.Where(e => e.Date < tomorrow).ToList();
        freshSchedule.Entries.Clear();
        foreach (var e in lockedEntries) freshSchedule.Entries.Add(e);

        var (added, gaps, gapDays) = await _autoScheduleService.AutoAssignAsync(
            freshSchedule, ActiveEmployees, EnabledShifts, _laborLaw);
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
}
