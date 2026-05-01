using System.ComponentModel.DataAnnotations;

namespace ShopManager.Models;

/// <summary>預設薪資設定</summary>
public class SalarySetting
{
    [Key] public int Id { get; set; }
    public Guid ShopId { get; set; }
    [Required] public string Alias { get; set; } = string.Empty;    // 薪資別名
    public string Description { get; set; } = string.Empty;         // 描述
    public SalaryType Type { get; set; } = SalaryType.Hourly;       // 類型

    // ── 時薪制 ──────────────────────────────
    public decimal? HourlyRate { get; set; }                         // 時薪

    // ── 月薪制 ──────────────────────────────
    public decimal? MonthlyBase { get; set; }                        // 底薪

    // ── 合同制 ──────────────────────────────
    public decimal? ContractAmount { get; set; }                     // 合同金額
    public string ContractCycle { get; set; } = string.Empty;       // 計薪週期（月/案件）

    // ── 共用設定 ────────────────────────────
    public decimal? OT1Rate { get; set; }       // 延長工時費率1（null = 採用法規預設）
    public decimal? OT2Rate { get; set; }       // 延長工時費率2
    public decimal? HolidayRate { get; set; }   // 假日加班費率
    public double? DailyMaxHours { get; set; }  // 每日正常工時上限
    public double? WeeklyMaxHours { get; set; } // 每周正常工時上限
}

public enum SalaryType
{
    Hourly = 0,     // 時薪
    Monthly = 1,    // 月薪
    Contract = 2    // 合同
}
