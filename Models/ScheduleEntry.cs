using System.ComponentModel.DataAnnotations;

namespace ShopManager.Models;

/// <summary>排班記錄（一位員工 × 一天 × 一個班別）</summary>
public class ScheduleEntry
{
    [Key] public int Id { get; set; }

    public int MonthlyScheduleId { get; set; }
    public MonthlySchedule? MonthlySchedule { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public DateOnly Date { get; set; }

    public int ShiftSettingId { get; set; }
    public ShiftSetting? ShiftSetting { get; set; }

    public string Note { get; set; } = string.Empty;
}
