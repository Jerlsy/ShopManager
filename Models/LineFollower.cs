using System.ComponentModel.DataAnnotations;

namespace ShopManager.Models;

public class LineFollower
{
    [Key] public int Id { get; set; }
    public Guid ShopId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? PictureUrl { get; set; }
    public int? BoundEmployeeId { get; set; }
    public bool IsBindingDisabled { get; set; }
    public DateTime LastSyncAt { get; set; }
}
