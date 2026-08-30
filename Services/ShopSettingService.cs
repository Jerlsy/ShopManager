using Microsoft.EntityFrameworkCore;
using ShopManager.Data;
using ShopManager.Models;

namespace ShopManager.Services;

public class ShopSettingService(AppDbContext db, ShopContext shopContext)
{
    // AsNoTracking：本服務的 DbContext 隨頁面（如排班頁）長期存活，若用追蹤查詢，
    // 在設定頁（另一個 DbContext）改了業主 LINE 綁定/Token 後，這裡會從 EF identity map
    // 回傳舊的已追蹤實體而非 DB 最新值。唯讀讀取一律繞過追蹤以確保拿到最新資料。
    public async Task<ShopSetting?> GetAsync() =>
        await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ShopId == shopContext.ShopId);

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
            existing.OwnerLineBindings = setting.OwnerLineBindings;
            existing.GoogleAccountEmail = setting.GoogleAccountEmail;
            existing.GoogleDriveLastSyncedRemoteModifiedTime = setting.GoogleDriveLastSyncedRemoteModifiedTime;
            existing.GmailForwardRules = setting.GmailForwardRules;
            existing.Notes = setting.Notes;
        }
        await db.SaveChangesAsync();
    }
}
