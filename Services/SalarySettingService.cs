using Microsoft.EntityFrameworkCore;
using ShopManager.Data;
using ShopManager.Models;

namespace ShopManager.Services;

public class SalarySettingService(AppDbContext db, ShopContext shopContext)
{
    public async Task<LaborLawSetting?> GetLaborLawAsync() =>
        await db.LaborLawSettings.FirstOrDefaultAsync();

    public async Task SaveLaborLawAsync(LaborLawSetting setting)
    {
        var existing = await db.LaborLawSettings.FirstOrDefaultAsync();
        if (existing is null) db.LaborLawSettings.Add(setting);
        else db.Entry(existing).CurrentValues.SetValues(setting);
        await db.SaveChangesAsync();
    }

    public async Task<List<SalarySetting>> GetAllAsync() =>
        await db.SalarySettings
            .Where(s => s.ShopId == shopContext.ShopId)
            .OrderBy(s => s.Alias)
            .ToListAsync();

    public async Task AddAsync(SalarySetting salary)
    {
        salary.ShopId = shopContext.ShopId;
        db.SalarySettings.Add(salary);
        await db.SaveChangesAsync();
    }

    public async Task UpdateAsync(SalarySetting salary)
    {
        db.SalarySettings.Update(salary);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var s = await db.SalarySettings.FindAsync(id);
        if (s is not null) { db.SalarySettings.Remove(s); await db.SaveChangesAsync(); }
    }
}
