using Microsoft.EntityFrameworkCore;
using ShopManager.Data;
using ShopManager.Models;
using System.Linq.Expressions;

namespace ShopManager.Services;

public class MonthlyScheduleService(AppDbContext db, ShopContext shopContext)
{
    public async Task<List<MonthlySchedule>> GetAllAsync() =>
        await db.MonthlySchedules
            .Where(m => m.ShopId == shopContext.ShopId)
            .OrderByDescending(m => m.Year)
            .ThenByDescending(m => m.Month)
            .ToListAsync();

    public async Task<MonthlySchedule?> GetAsync(int year, int month) =>
        await db.MonthlySchedules
            .AsNoTracking()
            .Where(m => m.ShopId == shopContext.ShopId)
            .Include(m => m.Entries)
                .ThenInclude(e => e.Employee)
            .Include(m => m.Entries)
                .ThenInclude(e => e.ShiftSetting)
            .FirstOrDefaultAsync(m => m.Year == year && m.Month == month);

    public async Task<MonthlySchedule?> GetByIdAsync(int id) =>
        await db.MonthlySchedules
            .Include(m => m.Entries)
                .ThenInclude(e => e.Employee)
            .Include(m => m.Entries)
                .ThenInclude(e => e.ShiftSetting)
            .FirstOrDefaultAsync(m => m.Id == id);

    public async Task<bool> ExistsAsync(int year, int month) =>
        await db.MonthlySchedules.AnyAsync(m =>
            m.ShopId == shopContext.ShopId && m.Year == year && m.Month == month);

    /// <summary>
    /// 建立月班表，從系統設定帶入店休日預設值
    /// </summary>
    public async Task<MonthlySchedule> CreateAsync(int year, int month, ShopSetting settings,
        List<ShiftDayConfig>? shiftDayConfigs = null,
        List<int>? additionalClosedDays = null)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var closedDays = new List<int>();

        // 根據系統設定的每周固定店休日，產生該月的店休日清單
        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(year, month, day);
            var dayOfWeek = (int)date.DayOfWeek;
            if (settings.ClosedDaysOfWeek.Contains(dayOfWeek))
                closedDays.Add(day);
        }

        // 合併國定假日（只加入該月範圍內且尚未在清單中的日期）
        if (additionalClosedDays is { Count: > 0 })
        {
            foreach (var d in additionalClosedDays.Where(d => d >= 1 && d <= daysInMonth && !closedDays.Contains(d)))
                closedDays.Add(d);
            closedDays.Sort();
        }

        var schedule = new MonthlySchedule
        {
            ShopId = shopContext.ShopId,
            Year = year,
            Month = month,
            ClosedDays = closedDays,
            WeekStartDay = settings.WeekStartDay,
            ShiftDayConfigs = shiftDayConfigs ?? new(),
            Status = ScheduleStatus.Draft
        };

        db.MonthlySchedules.Add(schedule);
        await db.SaveChangesAsync();
        return schedule;
    }

    public Task UpdateStaffingGapDaysAsync(int id, List<int> gapDays) =>
        UpdateFieldAsync(id, s => s.StaffingGapDays = gapDays, s => s.StaffingGapDays);

    public Task UpdateShiftDateOverridesAsync(int id, List<ShiftDateOverride> overrides) =>
        UpdateFieldAsync(id, s => s.ShiftDateOverrides = overrides, s => s.ShiftDateOverrides);

    public async Task UpdateAutoAssignConfigAsync(
        int id,
        List<WorkDayConditionConfig> conditionConfigs,
        List<EmployeeDayOff> dayOffs,
        List<EmployeeWorkDay> workDays,
        List<int> excludeIds)
    {
        var schedule = await db.MonthlySchedules.FindAsync(id);
        if (schedule is null) return;

        schedule.WorkDayConditionConfigs  = conditionConfigs;
        schedule.EmployeeDayOffs          = dayOffs;
        schedule.EmployeeWorkDays         = workDays;
        schedule.ExcludeFromAutoAssignIds = excludeIds;

        // 明確標記：HasConversion 集合屬性的 change tracking 不可靠，必須強制 IsModified
        var entry = db.Entry(schedule);
        entry.Property(e => e.WorkDayConditionConfigs).IsModified  = true;
        entry.Property(e => e.EmployeeDayOffs).IsModified          = true;
        entry.Property(e => e.EmployeeWorkDays).IsModified         = true;
        entry.Property(e => e.ExcludeFromAutoAssignIds).IsModified = true;

        await db.SaveChangesAsync();
    }

    public Task UpdateEmployeeDayOffsAsync(int id, List<EmployeeDayOff> dayOffs) =>
        UpdateFieldAsync(id, s => s.EmployeeDayOffs = dayOffs, s => s.EmployeeDayOffs);

    public Task UpdateWorkDayConditionConfigsAsync(int id, List<WorkDayConditionConfig> configs) =>
        UpdateFieldAsync(id, s => s.WorkDayConditionConfigs = configs, s => s.WorkDayConditionConfigs);

    public Task UpdateEmployeeWorkDaysAsync(int id, List<EmployeeWorkDay> workDays) =>
        UpdateFieldAsync(id, s => s.EmployeeWorkDays = workDays, s => s.EmployeeWorkDays);

    public Task UpdateExcludeFromAutoAssignIdsAsync(int id, List<int> excludedIds) =>
        UpdateFieldAsync(id, s => s.ExcludeFromAutoAssignIds = excludedIds, s => s.ExcludeFromAutoAssignIds);

    public Task UpdateClosedDaysAsync(int id, List<int> closedDays) =>
        UpdateFieldAsync(id, s => s.ClosedDays = closedDays, s => s.ClosedDays);

    public async Task UpdateStatusAsync(int id, ScheduleStatus status)
    {
        var schedule = await db.MonthlySchedules.FindAsync(id);
        if (schedule is not null)
        {
            schedule.Status = status;
            await db.SaveChangesAsync();
        }
    }

    public async Task DeleteAsync(int id)
    {
        var schedule = await db.MonthlySchedules.FindAsync(id);
        if (schedule is not null)
        {
            db.MonthlySchedules.Remove(schedule);
            await db.SaveChangesAsync();
        }
    }

    private async Task UpdateFieldAsync<T>(
        int id,
        Action<MonthlySchedule> apply,
        Expression<Func<MonthlySchedule, T>> property)
    {
        var schedule = await db.MonthlySchedules.FindAsync(id);
        if (schedule is null) return;
        apply(schedule);
        db.Entry(schedule).Property(property).IsModified = true;
        await db.SaveChangesAsync();
    }
}
