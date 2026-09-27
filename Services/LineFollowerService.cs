using Microsoft.EntityFrameworkCore;
using ShopManager.Data;
using ShopManager.Models;

namespace ShopManager.Services;

public class LineFollowerService(AppDbContext db, ShopContext shopContext, LineService lineService)
{
    public async Task<LineFollower?> GetByEmployeeIdAsync(int employeeId) =>
        await db.LineFollowers
            .FirstOrDefaultAsync(f => f.ShopId == shopContext.ShopId && f.BoundEmployeeId == employeeId);

    public async Task<List<LineFollower>> GetAllAsync() =>
        await db.LineFollowers
            .Where(f => f.ShopId == shopContext.ShopId)
            .OrderBy(f => f.DisplayName)
            .ToListAsync();

    public async Task<List<LineFollower>> SyncAndGetAllAsync(string workerUrl, string apiKey, string? token = null)
    {
        var workerFollowers = await lineService.GetFollowersFromWorkerAsync(workerUrl, apiKey);
        var returnedIds = new HashSet<string>(workerFollowers.Select(f => f.UserId));

        var existing = await db.LineFollowers
            .Where(f => f.ShopId == shopContext.ShopId)
            .ToListAsync();

        // 移除已解除追蹤且無綁定的記錄
        var toRemove = existing.Where(f => !returnedIds.Contains(f.UserId) && f.BoundEmployeeId == null).ToList();
        db.LineFollowers.RemoveRange(toRemove);
        foreach (var r in toRemove) existing.Remove(r);

        // 更新或新增當前好友／群組／多人聊天室
        var now = DateTime.UtcNow;
        foreach (var (userId, displayName, pictureUrl, targetType) in workerFollowers)
        {
            var follower = existing.FirstOrDefault(f => f.UserId == userId);
            if (follower == null)
            {
                follower = new LineFollower { ShopId = shopContext.ShopId, UserId = userId };
                db.LineFollowers.Add(follower);
                existing.Add(follower);
            }
            follower.DisplayName = displayName;
            follower.PictureUrl = pictureUrl;
            follower.TargetType = targetType;
            follower.LastSyncAt = now;
        }

        // Worker 回傳的 displayName 可能因 re-follow 而遺失，用 LINE Profile API 補齊——
        // 這支 API 只認得個人 userId，群組/多人聊天室的名稱由 Worker 端自己查（見 Cloudflare Worker 教學），
        // 這裡不用管，也不能拿群組 id 去查會直接失敗。
        if (!string.IsNullOrEmpty(token))
        {
            var needProfile = existing
                .Where(f => f.TargetType == LineTargetType.User && returnedIds.Contains(f.UserId) &&
                            (string.IsNullOrWhiteSpace(f.DisplayName) || f.DisplayName == "未知"))
                .ToList();

            foreach (var follower in needProfile)
            {
                var (name, pic) = await lineService.GetProfileAsync(token, follower.UserId);
                follower.DisplayName = name;
                if (pic is not null) follower.PictureUrl = pic;
            }
        }

        await db.SaveChangesAsync();
        return await GetAllAsync();
    }

    public async Task BindAsync(string userId, int employeeId)
    {
        // 解除該員工舊的綁定
        var oldBinding = await db.LineFollowers
            .FirstOrDefaultAsync(f => f.ShopId == shopContext.ShopId && f.BoundEmployeeId == employeeId);
        if (oldBinding != null)
            oldBinding.BoundEmployeeId = null;

        // 建立新綁定
        var follower = await db.LineFollowers
            .FirstOrDefaultAsync(f => f.ShopId == shopContext.ShopId && f.UserId == userId);
        if (follower != null)
        {
            follower.BoundEmployeeId = employeeId;
            follower.IsBindingDisabled = false;
        }

        var employee = await db.Employees.FindAsync(employeeId);
        if (employee != null) employee.LineUserId = userId;

        await db.SaveChangesAsync();
    }

    public async Task UnbindAsync(int employeeId)
    {
        var follower = await db.LineFollowers
            .FirstOrDefaultAsync(f => f.ShopId == shopContext.ShopId && f.BoundEmployeeId == employeeId);
        if (follower != null)
            follower.BoundEmployeeId = null;

        var employee = await db.Employees.FindAsync(employeeId);
        if (employee != null) employee.LineUserId = null;

        await db.SaveChangesAsync();
    }

    /// <summary>員工離職時停用綁定（保留連結記錄，不再推播）</summary>
    public async Task DisableBindingAsync(int employeeId)
    {
        var follower = await db.LineFollowers
            .FirstOrDefaultAsync(f => f.ShopId == shopContext.ShopId && f.BoundEmployeeId == employeeId);
        if (follower != null)
            follower.IsBindingDisabled = true;
        await db.SaveChangesAsync();
    }
}
