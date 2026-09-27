using System.ComponentModel.DataAnnotations;

namespace ShopManager.Models;

/// <summary>推播對象種類：個人好友、群組、多人聊天室（後兩者沒有 Profile API，名稱來自 Worker 回報）</summary>
public enum LineTargetType { User = 0, Group = 1, Room = 2 }

public class LineFollower
{
    [Key] public int Id { get; set; }
    public Guid ShopId { get; set; }

    /// <summary>LINE 推播用的 to 值：User 是 userId，Group/Room 則是 groupId/roomId</summary>
    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string? PictureUrl { get; set; }
    public LineTargetType TargetType { get; set; } = LineTargetType.User;

    /// <summary>只有 User 才有意義；Group/Room 不能綁定員工</summary>
    public int? BoundEmployeeId { get; set; }
    public bool IsBindingDisabled { get; set; }
    public DateTime LastSyncAt { get; set; }
}
