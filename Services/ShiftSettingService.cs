using Microsoft.EntityFrameworkCore;
using ShopManager.Data;
using ShopManager.Models;

namespace ShopManager.Services;

public class ShiftSettingService(AppDbContext db, ShopContext shopContext)
{
    public async Task<List<ShiftSetting>> GetAllAsync() =>
        await db.ShiftSettings
            .Where(s => s.ShopId == shopContext.ShopId)
            .OrderBy(s => s.StartTime)
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
        db.ShiftSettings.Update(shift);
        await db.SaveChangesAsync();
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
