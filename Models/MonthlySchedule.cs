using System.ComponentModel.DataAnnotations;

namespace ShopManager.Models;

/// <summary>月班表容器，一個年月對應一張班表</summary>
public class MonthlySchedule
{
    [Key] public int Id { get; set; }
    public Guid ShopId { get; set; }

    public int Year { get; set; }
    public int Month { get; set; }

    /// <summary>該月的店休日清單（JSON 序列化，存日期的 Day 值）</summary>
    public List<int> ClosedDays { get; set; } = new();

    /// <summary>一周起始日（建立時從系統設定帶入，0=Sunday, 1=Monday）</summary>
    public int WeekStartDay { get; set; } = 1;

    /// <summary>班表狀態</summary>
    public ScheduleStatus Status { get; set; } = ScheduleStatus.Draft;

    /// <summary>該月的排班記錄</summary>
    public List<ScheduleEntry> Entries { get; set; } = new();
}

public enum ScheduleStatus
{
    Draft = 0,      // 草稿
    Published = 1   // 已發布
}
