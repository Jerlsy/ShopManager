using Microsoft.EntityFrameworkCore;
using ShopManager.Data;
using ShopManager.Models;

namespace ShopManager.Services;

public record EmployeeHistory(List<string> ScheduleMonths, List<string> SalaryMonths)
{
    public bool IsEmpty => ScheduleMonths.Count == 0 && SalaryMonths.Count == 0;
}

public class EmployeeService(AppDbContext db, ShopContext shopContext)
{
    public async Task<List<Employee>> GetAllAsync() =>
        await db.Employees
            .Where(e => e.ShopId == shopContext.ShopId)
            .Include(e => e.DefaultShift)
            .Include(e => e.DefaultSalary)
            .Include(e => e.ScheduleRules)
            .Include(e => e.DefaultBonuses)
            .OrderBy(e => e.Name)
            .ToListAsync();

    public async Task<List<Employee>> GetAllNoTrackingAsync() =>
        await db.Employees
            .AsNoTracking()
            .Where(e => e.ShopId == shopContext.ShopId)
            .OrderBy(e => e.Name)
            .ToListAsync();

    // 排班頁專用：與 GetAllAsync 相同的 Include，但 AsNoTracking。
    // 排班頁的 DbContext 隨頁面長期存活，用追蹤會讓他頁改過的「既有員工」（姓名/顏色/排班規則）
    // 從 EF identity map 回傳舊值；排班頁只讀不存員工，故繞過追蹤以確保拿到最新。
    public async Task<List<Employee>> GetAllWithDetailsNoTrackingAsync() =>
        await db.Employees
            .AsNoTracking()
            .Where(e => e.ShopId == shopContext.ShopId)
            .Include(e => e.DefaultShift)
            .Include(e => e.DefaultSalary)
            .Include(e => e.ScheduleRules)
            .Include(e => e.DefaultBonuses)
            .OrderBy(e => e.Name)
            .ToListAsync();

    public async Task<Employee?> GetByIdAsync(int id) =>
        await db.Employees
            .Include(e => e.DefaultShift)
            .Include(e => e.DefaultSalary)
            .Include(e => e.ScheduleRules)
            .Include(e => e.DefaultBonuses)
            .FirstOrDefaultAsync(e => e.Id == id);

    public async Task AddAsync(Employee employee)
    {
        employee.ShopId = shopContext.ShopId;
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
    }

    public async Task UpdateAsync(Employee employee)
    {
        db.Employees.Update(employee);
        await db.SaveChangesAsync();
    }

    public async Task UpdatePreferredShiftsAsync(int employeeId, List<int> shiftIds)
    {
        var emp = await db.Employees.FindAsync(employeeId);
        if (emp is not null)
        {
            emp.PreferredShiftIds = shiftIds;
            await db.SaveChangesAsync();
        }
    }

    // 薪資頁匯入打卡時會用另一個 DbContext 寫入打卡名稱，員工頁的追蹤實體可能是舊值
    public async Task<string?> GetClockNameNoTrackingAsync(int employeeId) =>
        await db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => e.ClockName)
            .FirstOrDefaultAsync();

    public async Task UpdateClockNamesAsync(IReadOnlyDictionary<int, string> clockNames)
    {
        if (clockNames.Count == 0) return;
        var ids = clockNames.Keys.ToList();
        var emps = await db.Employees.Where(e => ids.Contains(e.Id)).ToListAsync();
        foreach (var emp in emps)
            emp.ClockName = clockNames[emp.Id];
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 刪除員工。排班、薪資明細、聯絡方式、排班規則、預設獎金由資料庫連帶刪除；
    /// 沒有外鍵的 LINE 綁定與排班衝突紀錄在這裡一併清掉，避免留下對不到人的資料。
    /// </summary>
    public async Task DeleteAsync(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();

        await db.ScheduleConflicts.Where(c => c.EmployeeId == id).ExecuteDeleteAsync();
        await db.LineFollowers
            .Where(f => f.ShopId == shopContext.ShopId && f.BoundEmployeeId == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.BoundEmployeeId, (int?)null)
                .SetProperty(f => f.IsBindingDisabled, false));

        var e = await db.Employees.FindAsync(id);
        if (e is not null) { db.Employees.Remove(e); await db.SaveChangesAsync(); }

        await tx.CommitAsync();
    }

    /// <summary>員工有哪些月份的排班與薪資紀錄（刪除前提示用，格式 yyyy/MM）</summary>
    public async Task<EmployeeHistory> GetHistoryAsync(int employeeId)
    {
        var scheduleMonths = await db.ScheduleEntries
            .Where(e => e.EmployeeId == employeeId)
            .Select(e => new { e.MonthlySchedule!.Year, e.MonthlySchedule.Month })
            .Distinct()
            .ToListAsync();

        var salaryMonths = await db.SalaryEmployeeRecords
            .Where(r => r.EmployeeId == employeeId)
            .Join(db.SalaryRecords, r => r.SalaryRecordId, s => s.Id, (r, s) => new { s.Year, s.Month })
            .Distinct()
            .ToListAsync();

        static List<string> Fmt(IEnumerable<(int Year, int Month)> ms) =>
            ms.OrderBy(m => m.Year).ThenBy(m => m.Month).Select(m => $"{m.Year}/{m.Month:D2}").ToList();

        return new EmployeeHistory(
            Fmt(scheduleMonths.Select(m => (m.Year, m.Month))),
            Fmt(salaryMonths.Select(m => (m.Year, m.Month))));
    }

    public async Task SetResignDateAsync(int employeeId, DateOnly? resignDate)
    {
        var emp = await db.Employees.FindAsync(employeeId);
        if (emp is null) return;
        emp.ResignDate = resignDate;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 掃描離職日之後是否有排班資料，回傳需要重新編輯的年月清單
    /// </summary>
    public async Task<List<string>> CheckScheduleAfterResignAsync(int employeeId, DateOnly resignDate)
    {
        var dates = await db.ScheduleEntries
            .Where(e => e.EmployeeId == employeeId && e.Date > resignDate)
            .Select(e => e.Date)
            .ToListAsync();

        return dates
            .Select(d => d.ToString("yyyy-MM"))
            .Distinct()
            .OrderBy(s => s)
            .ToList();
    }
}
