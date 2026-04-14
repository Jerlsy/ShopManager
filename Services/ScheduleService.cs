using Microsoft.EntityFrameworkCore;
using ShopManager.Data;
using ShopManager.Models;

namespace ShopManager.Services;

public class ScheduleService(AppDbContext db, ShopContext shopContext)
{
    public async Task<List<ScheduleEntry>> GetEntriesByScheduleAsync(int monthlyScheduleId) =>
        await db.ScheduleEntries
            .Include(e => e.Employee)
            .Include(e => e.ShiftSetting)
            .Where(e => e.MonthlyScheduleId == monthlyScheduleId)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.ShiftSettingId)
            .ToListAsync();

    /// <summary>跨月份查詢指定店鋪某日期區間內的所有排班</summary>
    public async Task<List<ScheduleEntry>> GetEntriesByDateRangeAsync(DateOnly start, DateOnly end) =>
        await db.ScheduleEntries
            .Include(e => e.Employee)
            .Include(e => e.ShiftSetting)
            .Include(e => e.MonthlySchedule)
            .Where(e => e.MonthlySchedule!.ShopId == shopContext.ShopId
                     && e.Date >= start && e.Date <= end)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.ShiftSettingId)
            .ToListAsync();

    public async Task AddEntryAsync(ScheduleEntry entry)
    {
        db.ScheduleEntries.Add(entry);
        await db.SaveChangesAsync();
    }

    /// <summary>批次新增多筆排班（自動略過重複）</summary>
    public async Task<int> AddEntriesAsync(IEnumerable<ScheduleEntry> entries)
    {
        int count = 0;
        foreach (var entry in entries)
        {
            var exists = await db.ScheduleEntries.AnyAsync(e =>
                e.MonthlyScheduleId == entry.MonthlyScheduleId &&
                e.EmployeeId == entry.EmployeeId &&
                e.Date == entry.Date &&
                e.ShiftSettingId == entry.ShiftSettingId);
            if (!exists)
            {
                db.ScheduleEntries.Add(entry);
                count++;
            }
        }
        if (count > 0) await db.SaveChangesAsync();
        return count;
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

    /// <summary>更新排班的班別與備註</summary>
    public async Task UpdateEntryAsync(int entryId, int newShiftSettingId, string note)
    {
        var entry = await db.ScheduleEntries.FindAsync(entryId);
        if (entry is not null)
        {
            entry.ShiftSettingId = newShiftSettingId;
            entry.Note = note;
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
