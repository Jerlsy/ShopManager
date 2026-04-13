using System.ComponentModel.DataAnnotations;

namespace ShopManager.Models;

/// <summary>預設班別設定</summary>
public class ShiftSetting
{
    [Key] public int Id { get; set; }
    public Guid ShopId { get; set; }
    [Required] public string Alias { get; set; } = string.Empty;   // 班別別名
    public TimeOnly StartTime { get; set; }                         // 上班時間
    public TimeOnly EndTime { get; set; }                           // 下班時間
    public bool IsEnabled { get; set; } = true;                     // 啟用/停用
    public string Color { get; set; } = "#4A90D9";                  // 代表色（hex）

    // 計算工時（小時）
    public double WorkHours =>
        EndTime > StartTime
            ? (EndTime - StartTime).TotalHours
            : (EndTime.AddHours(24) - StartTime).TotalHours; // 跨日班別
}
