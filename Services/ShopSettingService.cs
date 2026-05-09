using Microsoft.EntityFrameworkCore;
using ShopManager.Data;
using ShopManager.Models;

namespace ShopManager.Services;

public class ShopSettingService(AppDbContext db, ShopContext shopContext)
{
    public async Task<ShopSetting?> GetAsync() =>
        await db.ShopSettings.FirstOrDefaultAsync(s => s.ShopId == shopContext.ShopId);

    public async Task SaveAsync(ShopSetting setting)
    {
        var existing = await db.ShopSettings.FirstOrDefaultAsync(s => s.ShopId == shopContext.ShopId);
        if (existing is null)
        {
            setting.ShopId = shopContext.ShopId;
            db.ShopSettings.Add(setting);
        }
        else
        {
            existing.Name = setting.Name;
            existing.Address = setting.Address;
            existing.Phone = setting.Phone;
            existing.LogoPhotoData = setting.LogoPhotoData;
            existing.ContactInfos = setting.ContactInfos;
            existing.WeekStartDay = setting.WeekStartDay;
            existing.ClosedDaysOfWeek = setting.ClosedDaysOfWeek;
            existing.NationalHolidaysOff = setting.NationalHolidaysOff;
            existing.LineChannelAccessToken = setting.LineChannelAccessToken;
            existing.LineWorkerUrl = setting.LineWorkerUrl;
            existing.LineWorkerApiKey = setting.LineWorkerApiKey;
            existing.LineWelcomeMessage = setting.LineWelcomeMessage;
            existing.LineResignMessage = setting.LineResignMessage;
            existing.Notes = setting.Notes;
        }
        await db.SaveChangesAsync();
    }
}
