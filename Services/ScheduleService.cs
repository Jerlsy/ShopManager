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

    public async Task<ScheduleEntry> AddEntryAsync(ScheduleEntry entry)
    {
        db.ScheduleEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    /// <summary>批次新增多筆排班（自動略過重複），回傳實際新增的項目</summary>
    public async Task<List<ScheduleEntry>> AddEntriesAsync(IEnumerable<ScheduleEntry> entries)
    {
        var list = entries.ToList();
        if (list.Count == 0) return list;

        var scheduleIds = list.Select(e => e.MonthlyScheduleId).Distinct().ToList();
        var existing = await db.ScheduleEntries
            .Where(e => scheduleIds.Contains(e.MonthlyScheduleId))
            .Select(e => new { e.MonthlyScheduleId, e.EmployeeId, e.Date, e.ShiftSettingId })
            .ToListAsync();

        var existingSet = existing
            .Select(e => (e.MonthlyScheduleId, e.EmployeeId, e.Date, e.ShiftSettingId))
            .ToHashSet();

        var added = new List<ScheduleEntry>();
        foreach (var entry in list)
        {
            if (existingSet.Add((entry.MonthlyScheduleId, entry.EmployeeId, entry.Date, entry.ShiftSettingId)))
            {
                db.ScheduleEntries.Add(entry);
                added.Add(entry);
            }
        }

        if (added.Count > 0) await db.SaveChangesAsync();
        return added;
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

    public async Task RemoveEntriesAsync(IEnumerable<int> entryIds)
    {
        var ids = entryIds.ToList();
        var entries = await db.ScheduleEntries.Where(e => ids.Contains(e.Id)).ToListAsync();
        if (entries.Count > 0)
        {
            db.ScheduleEntries.RemoveRange(entries);
            await db.SaveChangesAsync();
        }
    }

    public async Task ClearAllEntriesAsync(int monthlyScheduleId) =>
        await db.ScheduleEntries
            .Where(e => e.MonthlyScheduleId == monthlyScheduleId)
            .ExecuteDeleteAsync();

    public async Task ClearFutureEntriesAsync(int monthlyScheduleId, DateOnly fromDate) =>
        await db.ScheduleEntries
            .Where(e => e.MonthlyScheduleId == monthlyScheduleId && e.Date >= fromDate)
            .ExecuteDeleteAsync();

    /// <summary>查詢指定員工在某日期之後是否還有排班資料，回傳年月清單</summary>
    public async Task<List<string>> GetScheduledMonthsAfterDateAsync(int employeeId, DateOnly date) =>
        await db.ScheduleEntries
            .Where(e => e.EmployeeId == employeeId && e.Date > date)
            .Select(e => e.Date.ToString("yyyy-MM"))
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync();
}
