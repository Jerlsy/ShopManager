using Microsoft.EntityFrameworkCore;
using ShopManager.Data;
using ShopManager.Models;

namespace ShopManager.Services;

/// <summary>
/// 班表衝突檢查服務。
/// 各 ViewModel 儲存「影響排班的參數」後呼叫對應的 RecheckBy* 方法；
/// ScheduleViewModel 開啟衝突面板時呼叫 RecheckAsync 取得最新結果。
/// </summary>
public class ScheduleConflictService(AppDbContext db, ShopContext shopContext)
{
    // ══════════════════════════════════════════
    // 觸發點（由各 ViewModel 儲存後呼叫）
    // ══════════════════════════════════════════

    /// <summary>員工設定變更 → 重新檢查當月及未來月份含此員工班次的班表，回傳總衝突筆數</summary>
    public async Task<int> RecheckByEmployeeAsync(int employeeId)
    {
        var (year, month) = CurrentYearMonth();
        var ids = await db.ScheduleEntries
            .Where(e => e.EmployeeId == employeeId)
            .Join(db.MonthlySchedules,
                e => e.MonthlyScheduleId,
                m => m.Id,
                (e, m) => m)
            .Where(m => m.Year > year || (m.Year == year && m.Month >= month))
            .Select(m => m.Id)
            .Distinct()
            .ToListAsync();
        int total = 0;
        foreach (var id in ids) total += await RecheckAsync(id);
        return total;
    }

    /// <summary>班別設定變更 → 重新檢查當月及未來月份含此班別班次的班表，回傳總衝突筆數</summary>
    public async Task<int> RecheckByShiftAsync(int shiftId)
    {
        var (year, month) = CurrentYearMonth();
        var ids = await db.ScheduleEntries
            .Where(e => e.ShiftSettingId == shiftId)
            .Join(db.MonthlySchedules,
                e => e.MonthlyScheduleId,
                m => m.Id,
                (e, m) => m)
            .Where(m => m.Year > year || (m.Year == year && m.Month >= month))
            .Select(m => m.Id)
            .Distinct()
            .ToListAsync();
        int total = 0;
        foreach (var id in ids) total += await RecheckAsync(id);
        return total;
    }

    /// <summary>勞基法設定變更 → 重新檢查此店當月及未來月份的班表，回傳總衝突筆數</summary>
    public async Task<int> RecheckAllForShopAsync()
    {
        var (year, month) = CurrentYearMonth();
        var ids = await db.MonthlySchedules
            .Where(m => m.ShopId == shopContext.ShopId &&
                        (m.Year > year || (m.Year == year && m.Month >= month)))
            .Select(m => m.Id)
            .ToListAsync();
        int total = 0;
        foreach (var id in ids) total += await RecheckAsync(id);
        return total;
    }

    private static (int Year, int Month) CurrentYearMonth()
    {
        var today = DateTime.Today;
        return (today.Year, today.Month);
    }

    // ══════════════════════════════════════════
    // 核心檢查
    // ══════════════════════════════════════════

    /// <summary>
    /// 對指定班表跑所有排班規則，以新結果取代舊衝突紀錄。
    /// 回傳衝突筆數（0 代表無衝突）。
    /// </summary>
    public async Task<int> RecheckAsync(int scheduleId)
    {
        var schedule = await db.MonthlySchedules
            .AsNoTracking()
            .Include(m => m.Entries).ThenInclude(e => e.Employee)
            .Include(m => m.Entries).ThenInclude(e => e.ShiftSetting)
            .FirstOrDefaultAsync(m => m.Id == scheduleId);
        if (schedule is null) return 0;

        var activeEmployees = await db.Employees
            .AsNoTracking()
            .Where(e => e.ShopId == shopContext.ShopId)
            .Include(e => e.ScheduleRules)
            .ToListAsync();

        var laborLaw = await db.LaborLawSettings.AsNoTracking().FirstOrDefaultAsync();

        // 清除舊衝突
        await db.ScheduleConflicts
            .Where(c => c.ScheduleId == scheduleId)
            .ExecuteDeleteAsync();

        var conflicts = new List<ScheduleConflict>();
        foreach (var entry in schedule.Entries)
        {
            if (entry.Employee is null || entry.ShiftSetting is null) continue;

            // 使用含 ScheduleRules 的完整員工物件
            var fullEmployee = activeEmployees.FirstOrDefault(e => e.Id == entry.EmployeeId)
                               ?? entry.Employee;

            var ctx = new ShiftValidationContext(
                Employee:        fullEmployee,
                Date:            entry.Date,
                TargetShift:     entry.ShiftSetting,
                Schedule:        schedule,
                ActiveEmployees: activeEmployees,
                ExcludeEntryId:  entry.Id,  // 排除自身，避免 TimeOverlap / DailyMax 誤判
                LaborLaw:        laborLaw);

            var result = ShiftRuleEngine.Evaluate(ctx);
            if (!result.IsBlocked) continue;

            conflicts.Add(new ScheduleConflict
            {
                ScheduleId   = scheduleId,
                EntryId      = entry.Id,
                EmployeeId   = entry.EmployeeId,
                EmployeeName = entry.Employee.Name,
                Date         = entry.Date,
                ShiftAlias   = entry.ShiftSetting.Alias,
                Reason       = result.Reason,
            });
        }

        if (conflicts.Count > 0)
            db.ScheduleConflicts.AddRange(conflicts);
        await db.SaveChangesAsync();
        return conflicts.Count;
    }

    // ══════════════════════════════════════════
    // 查詢
    // ══════════════════════════════════════════

    public async Task<List<ScheduleConflict>> GetAsync(int scheduleId) =>
        await db.ScheduleConflicts
            .AsNoTracking()
            .Where(c => c.ScheduleId == scheduleId)
            .OrderBy(c => c.Date)
            .ThenBy(c => c.ShiftAlias)
            .ThenBy(c => c.EmployeeName)
            .ToListAsync();

    public async Task<int> GetCountAsync(int scheduleId) =>
        await db.ScheduleConflicts.CountAsync(c => c.ScheduleId == scheduleId);
}
