using Microsoft.EntityFrameworkCore;
using ShopManager.Data;
using ShopManager.Models;

namespace ShopManager.Services;

public class ShiftSettingService(AppDbContext db, ShopContext shopContext)
{
    public async Task<List<ShiftSetting>> GetAllAsync() =>
        await db.ShiftSettings
            .AsNoTracking()
            .Where(s => s.ShopId == shopContext.ShopId)
            .OrderBy(s => s.Alias)
            .ThenBy(s => s.StartTime)
            .ToListAsync();

    public async Task<ShiftSetting?> GetByIdAsync(int id) =>
        await db.ShiftSettings.FindAsync(id);

    public async Task AddAsync(ShiftSetting shift)
    {
        shift.ShopId = shopContext.ShopId;
        db.ShiftSettings.Add(shift);
        await db.SaveChangesAsync();
    }

    public async Task UpdateAsync(ShiftSetting shift)
    {
        // AsNoTracking() 讓 GetAllAsync 回傳新實例，但舊實例可能還在追蹤中；
        // 先 Detach 避免同 PK 兩個實例同時被追蹤。
        var tracked = db.ChangeTracker.Entries<ShiftSetting>()
            .FirstOrDefault(e => e.Entity.Id == shift.Id);
        if (tracked is not null)
            tracked.State = EntityState.Detached;

        db.ShiftSettings.Update(shift);
        await db.SaveChangesAsync();
    }

    public async Task<int> GetEntryCountAsync(int shiftId)
    {
        var today = DateTime.Today;
        return await db.ScheduleEntries
            .Where(e => e.ShiftSettingId == shiftId)
            .Join(db.MonthlySchedules,
                e => e.MonthlyScheduleId,
                m => m.Id,
                (e, m) => m)
            .CountAsync(m => m.Year > today.Year || (m.Year == today.Year && m.Month >= today.Month));
    }

    public async Task<List<int>> GetAffectedScheduleIdsAsync(int shiftId)
    {
        var today = DateTime.Today;
        return await db.ScheduleEntries
            .Where(e => e.ShiftSettingId == shiftId)
            .Join(db.MonthlySchedules,
                e => e.MonthlyScheduleId,
                m => m.Id,
                (e, m) => m)
            .Where(m => m.Year > today.Year || (m.Year == today.Year && m.Month >= today.Month))
            .Select(m => m.Id)
            .Distinct()
            .ToListAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var shift = await db.ShiftSettings.FindAsync(id);
        if (shift is not null)
        {
            db.ShiftSettings.Remove(shift);
            await db.SaveChangesAsync();
        }
    }
}
