using Microsoft.EntityFrameworkCore;
using ShopManager.Data;
using ShopManager.Models;

namespace ShopManager.Services;

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

    public async Task DeleteAsync(int id)
    {
        var e = await db.Employees.FindAsync(id);
        if (e is not null) { db.Employees.Remove(e); await db.SaveChangesAsync(); }
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
