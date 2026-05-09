using ShopManager.Models;

namespace ShopManager.Services;

public class AutoScheduleService(ScheduleService entryService)
{
    /// <summary>
    /// 依照班表的 WorkDayConditionConfigs 自動排班，回傳（新增筆數、缺口說明、缺口日期）。
    /// 只排明天以後的日期，今天及以前的現有排班保留供公平計算。
    /// </summary>
    public async Task<(int Added, List<string> Gaps, List<int> GapDays)> AutoAssignAsync(
        MonthlySchedule             schedule,
        IReadOnlyList<Employee>     activeEmployees,
        IReadOnlyList<ShiftSetting> enabledShifts,
        LaborLawSetting?            laborLaw,
        bool                        dryRun   = false,
        DateOnly?                   fromDate = null)
    {
        var condByDow = new Dictionary<DayOfWeek, WorkDayConditionConfig>();
        foreach (var cfg in schedule.WorkDayConditionConfigs)
            foreach (var dow in cfg.DaysOfWeek)
                condByDow[(DayOfWeek)dow] = cfg;

        if (condByDow.Count == 0) return (0, [], []);

        var tomorrow    = fromDate ?? DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        var daysInMonth = DateTime.DaysInMonth(schedule.Year, schedule.Month);

        var workDays = new List<(DateOnly Date, List<ShiftSetting> Shifts, WorkDayConditionConfig Cond)>();
        for (int d = 1; d <= daysInMonth; d++)
        {
            var date = new DateOnly(schedule.Year, schedule.Month, d);
            if (date < tomorrow) continue;
            if (schedule.ClosedDays.Contains(d)) continue;
            if (!condByDow.TryGetValue(date.DayOfWeek, out var cond)) continue;

            var shiftsForDay = GetShiftsForDay(date, schedule, enabledShifts);
            if (shiftsForDay.Count > 0)
                workDays.Add((date, shiftsForDay, cond));
        }

        var excludeSet = schedule.ExcludeFromAutoAssignIds.ToHashSet();
        var employees  = activeEmployees.Where(e => !excludeSet.Contains(e.Id)).ToList();

        if (workDays.Count == 0 || employees.Count == 0) return (0, [], []);

        // Phase 0：跳過表
        var skipDays   = schedule.EmployeeDayOffs.ToDictionary(d => d.EmployeeId, d => d.Days.ToHashSet());
        var forcedDays = schedule.EmployeeWorkDays.ToDictionary(d => d.EmployeeId, d => d.Days.ToHashSet());

        bool ShouldSkip(Employee emp, DateOnly date) =>
            (skipDays.TryGetValue(emp.Id, out var offs) && offs.Contains(date.Day)) ||
            (forcedDays.TryGetValue(emp.Id, out var fds) && !fds.Contains(date.Day));

        // 計算公平配額：以「能排該班的員工數」為分母（排除 ExcludeShift）
        var totalSlotsPerShift = new Dictionary<int, int>();
        foreach (var (_, wdShifts, wdCond) in workDays)
            foreach (var s in wdShifts)
                totalSlotsPerShift[s.Id] = totalSlotsPerShift.GetValueOrDefault(s.Id, 0) + wdCond.MaxPerShift;

        var preferredQuota = totalSlotsPerShift.ToDictionary(
            kvp => kvp.Key,
            kvp =>
            {
                int eligible = employees.Count(e => !e.ScheduleRules
                    .Any(r => r.Type == ScheduleRuleType.ExcludeShift &&
                              r.ExcludedShiftIds.Contains(kvp.Key)));
                return eligible > 0 ? (double)kvp.Value / eligible : 0.0;
            });

        // 計算起始值（鎖定過去日期的現有排班），同時建立索引
        var index = new EntryIndex();
        var assignedCount    = employees.ToDictionary(e => e.Id, _ => 0);
        var assignedPerShift = employees.ToDictionary(e => e.Id, _ => new Dictionary<int, int>());

        foreach (var entry in schedule.Entries)
        {
            index.Add(entry);
            if (assignedCount.ContainsKey(entry.EmployeeId) && entry.Date < tomorrow)
            {
                assignedCount[entry.EmployeeId]++;
                var perShift = assignedPerShift[entry.EmployeeId];
                perShift[entry.ShiftSettingId] = perShift.GetValueOrDefault(entry.ShiftSettingId, 0) + 1;
            }
        }

        var shiftLookup   = (IReadOnlyDictionary<int, ShiftSetting>)enabledShifts.ToDictionary(s => s.Id);
        var entriesToSave = new List<ScheduleEntry>();
        var gaps          = new List<string>();
        var gapDaySet     = new HashSet<int>();

        // 集中管理新增班次（維護索引與計數器，避免重複）
        void AddEntry(Employee emp, DateOnly date, ShiftSetting shift)
        {
            var entry = new ScheduleEntry
            {
                MonthlyScheduleId = schedule.Id,
                EmployeeId        = emp.Id,
                Date              = date,
                ShiftSettingId    = shift.Id,
            };
            schedule.Entries.Add(entry);
            index.Add(entry);
            entriesToSave.Add(entry);
            assignedCount[emp.Id]++;
            assignedPerShift[emp.Id][shift.Id] =
                assignedPerShift[emp.Id].GetValueOrDefault(shift.Id, 0) + 1;
        }

        ShiftValidationContext MakeCtx(Employee emp, DateOnly date, ShiftSetting shift) =>
            new(Employee: emp, Date: date, TargetShift: shift,
                Schedule: schedule, ActiveEmployees: employees, LaborLaw: laborLaw,
                ShiftLookup: shiftLookup, Index: index);

        foreach (var (date, shifts, cond) in workDays)
        {
            // Phase 1：每班填至 MaxPerShift
            foreach (var shift in shifts)
            {
                int onShift = index.CountOnShift(date, shift.Id);
                if (onShift >= cond.MaxPerShift) continue;

                var eligible = employees
                    .Where(e => !ShouldSkip(e, date))
                    .OrderBy(e => assignedCount[e.Id])
                    .ThenBy(e =>
                    {
                        if (!e.PreferredShiftIds.Contains(shift.Id)) return double.MaxValue;
                        int sc = assignedPerShift[e.Id].GetValueOrDefault(shift.Id, 0);
                        if (sc >= preferredQuota.GetValueOrDefault(shift.Id, 0)) return double.MaxValue;
                        int total = assignedCount[e.Id];
                        return total == 0 ? 0.0 : (double)sc / total;
                    })
                    .ThenBy(e => e.PreferredShiftIds.Contains(shift.Id)
                        ? e.PreferredShiftIds.IndexOf(shift.Id) : int.MaxValue);

                foreach (var emp in eligible)
                {
                    if (index.CountOnShift(date, shift.Id) >= cond.MaxPerShift) break;
                    if (index.AlreadyAssigned(emp.Id, shift.Id, date)) continue;
                    if (ShiftRuleEngine.Evaluate(MakeCtx(emp, date, shift)).IsBlocked) continue;

                    AddEntry(emp, date, shift);
                }
            }

            // Phase 2：人數仍不足 MinPerDay → 跨班補排
            var todayEmpIds = index.EmpsOnDay(date);

            if (todayEmpIds.Count < cond.MinPerDay)
            {
                foreach (var emp in employees
                    .Where(e => !todayEmpIds.Contains(e.Id) && !ShouldSkip(e, date))
                    .OrderBy(e => assignedCount[e.Id]))
                {
                    if (index.EmpsOnDay(date).Count >= cond.MinPerDay) break;

                    var shiftsOrdered = shifts
                        .OrderBy(s =>
                        {
                            if (!emp.PreferredShiftIds.Contains(s.Id)) return 1;
                            int sc = assignedPerShift[emp.Id].GetValueOrDefault(s.Id, 0);
                            return sc < preferredQuota.GetValueOrDefault(s.Id, 0) ? 0 : 1;
                        })
                        .ThenBy(s => emp.PreferredShiftIds.Contains(s.Id)
                            ? emp.PreferredShiftIds.IndexOf(s.Id) : int.MaxValue);

                    foreach (var shift in shiftsOrdered)
                    {
                        if (index.CountOnShift(date, shift.Id) >= cond.MaxPerShift) continue;
                        if (index.AlreadyAssigned(emp.Id, shift.Id, date)) continue;
                        if (ShiftRuleEngine.Evaluate(MakeCtx(emp, date, shift)).IsBlocked) continue;

                        AddEntry(emp, date, shift);
                        break;
                    }
                }

                if (index.EmpsOnDay(date).Count < cond.MinPerDay)
                {
                    gaps.Add($"{date:MM/dd} 人力不足（{index.EmpsOnDay(date).Count}/{cond.MinPerDay}）");
                    gapDaySet.Add(date.Day);
                }
            }
        }

        if (!dryRun && entriesToSave.Count > 0)
            await entryService.AddEntriesAsync(entriesToSave);

        return (entriesToSave.Count, gaps, gapDaySet.ToList());
    }

    private static List<ShiftSetting> GetShiftsForDay(DateOnly date, MonthlySchedule schedule, IReadOnlyList<ShiftSetting> enabledShifts)
    {
        var dateOverride = schedule.ShiftDateOverrides.FirstOrDefault(o => o.Day == date.Day);
        if (dateOverride is not null)
            return enabledShifts.Where(s => dateOverride.ShiftIds.Contains(s.Id)).ToList();

        if (schedule.ShiftDayConfigs.Count == 0)
            return enabledShifts.ToList();

        var dow = (int)date.DayOfWeek;
        return enabledShifts.Where(s =>
        {
            var cfg = schedule.ShiftDayConfigs.FirstOrDefault(c => c.ShiftId == s.Id);
            if (cfg is null) return false;
            return cfg.DaysOfWeek.Count == 0 || cfg.DaysOfWeek.Contains(dow);
        }).ToList();
    }
}
