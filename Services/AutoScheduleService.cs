using ShopManager.Models;

namespace ShopManager.Services;

public class AutoScheduleService(ScheduleService entryService)
{
    /// <summary>
    /// 依照班表的 WorkDayConditionConfigs 自動排班，回傳（新增筆數、缺口說明、缺口日期）。
    /// 只排明天以後的日期，今天及以前的現有排班保留供公平計算。
    /// </summary>
    public async Task<(int Added, List<string> Gaps, List<int> GapDays)> AutoAssignAsync(
        MonthlySchedule         schedule,
        IReadOnlyList<Employee>     activeEmployees,
        IReadOnlyList<ShiftSetting> enabledShifts,
        LaborLawSetting?            laborLaw)
    {
        // 建立「星期幾 → 條件」對應（每天最多一條條件）
        var condByDow = new Dictionary<DayOfWeek, WorkDayConditionConfig>();
        foreach (var cfg in schedule.WorkDayConditionConfigs)
            foreach (var dow in cfg.DaysOfWeek)
                condByDow[(DayOfWeek)dow] = cfg;

        if (condByDow.Count == 0) return (0, [], []);

        var today    = DateOnly.FromDateTime(DateTime.Today);
        var tomorrow = today.AddDays(1);
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

        // Phase 0：建立跳過表（休假日 + 強制上班日）
        var skipDays   = schedule.EmployeeDayOffs.ToDictionary(d => d.EmployeeId, d => d.Days.ToHashSet());
        var forcedDays = schedule.EmployeeWorkDays.ToDictionary(d => d.EmployeeId, d => d.Days.ToHashSet());

        bool ShouldSkip(Employee emp, DateOnly date) =>
            (skipDays.TryGetValue(emp.Id, out var offs)  && offs.Contains(date.Day)) ||
            (forcedDays.TryGetValue(emp.Id, out var fds) && !fds.Contains(date.Day));

        // 計算起始值（鎖定過去日期的現有排班）
        var assignedCount = employees.ToDictionary(e => e.Id, e =>
            schedule.Entries.Count(en => en.EmployeeId == e.Id && en.Date < tomorrow));
        var assignedPerShift = employees.ToDictionary(e => e.Id, e =>
            schedule.Entries
                .Where(en => en.EmployeeId == e.Id && en.Date < tomorrow)
                .GroupBy(en => en.ShiftSettingId)
                .ToDictionary(g => g.Key, g => g.Count()));

        // 計算每班公平配額
        var totalSlotsPerShift = new Dictionary<int, int>();
        foreach (var (_, wdShifts, wdCond) in workDays)
            foreach (var s in wdShifts)
                totalSlotsPerShift[s.Id] = totalSlotsPerShift.GetValueOrDefault(s.Id, 0) + wdCond.MaxPerShift;
        var preferredQuota = totalSlotsPerShift.ToDictionary(
            kvp => kvp.Key,
            kvp => (double)kvp.Value / employees.Count);

        var shiftLookup    = (IReadOnlyDictionary<int, ShiftSetting>)enabledShifts.ToDictionary(s => s.Id);
        var entriesToSave  = new List<ScheduleEntry>();
        var gaps           = new List<string>();
        var gapDaySet      = new HashSet<int>();

        foreach (var (date, shifts, cond) in workDays)
        {
            // Phase 1：每班填至 MaxPerShift
            foreach (var shift in shifts)
            {
                int assignedToShift = schedule.Entries.Count(e => e.Date == date && e.ShiftSettingId == shift.Id);
                if (assignedToShift >= cond.MaxPerShift) continue;

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
                    .ThenBy(e =>
                    {
                        if (!e.PreferredShiftIds.Contains(shift.Id)) return int.MaxValue;
                        int sc = assignedPerShift[e.Id].GetValueOrDefault(shift.Id, 0);
                        if (sc >= preferredQuota.GetValueOrDefault(shift.Id, 0)) return int.MaxValue;
                        return e.PreferredShiftIds.IndexOf(shift.Id);
                    });

                foreach (var emp in eligible)
                {
                    if (assignedToShift >= cond.MaxPerShift) break;
                    if (schedule.Entries.Any(e => e.Date == date && e.ShiftSettingId == shift.Id && e.EmployeeId == emp.Id))
                        continue;

                    var ctx = new ShiftValidationContext(
                        Employee: emp, Date: date, TargetShift: shift,
                        Schedule: schedule, ActiveEmployees: employees, LaborLaw: laborLaw,
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

            // Phase 2：人數仍不足 MinPerDay → 跨班補排
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
                        int shiftCount = schedule.Entries.Count(e => e.Date == date && e.ShiftSettingId == shift.Id);
                        if (shiftCount >= cond.MaxPerShift) continue;
                        if (schedule.Entries.Any(e => e.Date == date && e.ShiftSettingId == shift.Id && e.EmployeeId == emp.Id))
                            continue;

                        var ctx = new ShiftValidationContext(
                            Employee: emp, Date: date, TargetShift: shift,
                            Schedule: schedule, ActiveEmployees: employees, LaborLaw: laborLaw,
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
