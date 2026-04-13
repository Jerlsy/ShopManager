using Microsoft.EntityFrameworkCore;
using ShopManager.Data;
using ShopManager.Models;

namespace ShopManager.Services;

public class ScheduleService(AppDbContext db)
{
    public async Task<List<ScheduleEntry>> GetEntriesByScheduleAsync(int monthlyScheduleId) =>
        await db.ScheduleEntries
            .Include(e => e.Employee)
            .Include(e => e.ShiftSetting)
            .Where(e => e.MonthlyScheduleId == monthlyScheduleId)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.ShiftSettingId)
            .ToListAsync();

    public async Task AddEntryAsync(ScheduleEntry entry)
    {
        db.ScheduleEntries.Add(entry);
        await db.SaveChangesAsync();
    }

    public async Task RemoveEntryAsync(int entryId)
    {
        var entry = await db.ScheduleEntries.FindAsync(entryId);
        if (entry is not null)
        {
            db.ScheduleEntries.Remove(entry);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>查詢指定員工在某日期之後是否還有排班資料，回傳年月清單</summary>
    public async Task<List<string>> GetScheduledMonthsAfterDateAsync(int employeeId, DateOnly date) =>
        await db.ScheduleEntries
            .Where(e => e.EmployeeId == employeeId && e.Date > date)
            .Select(e => e.Date.ToString("yyyy-MM"))
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync();
}
