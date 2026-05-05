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

    public async Task<List<LineFollower>> SyncAndGetAllAsync(string workerUrl, string apiKey)
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

        // 更新或新增當前好友
        var now = DateTime.UtcNow;
        foreach (var (userId, displayName, pictureUrl) in workerFollowers)
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
            follower.LastSyncAt = now;
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
