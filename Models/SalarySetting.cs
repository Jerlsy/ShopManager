using System.ComponentModel.DataAnnotations;

namespace ShopManager.Models;

/// <summary>台灣勞基法出勤法規設定</summary>
public class LaborLawSetting
{
    [Key] public int Id { get; set; }

    // ── 時薪制 ──────────────────────────────
    public decimal HourlyMinimumWage { get; set; } = 183m;          // 時薪最低薪資（2024）
    public double HourlyDailyMaxHours { get; set; } = 8.0;          // 時薪制每日正常工時上限
    public double HourlyWeeklyMaxHours { get; set; } = 40.0;        // 時薪制每周正常工時上限

    // 時薪加班費率（正常工時後）
    public decimal HourlyOT1Rate { get; set; } = 1.34m;             // 延長工時前2小時 × 4/3
    public decimal HourlyOT2Rate { get; set; } = 1.67m;             // 延長工時第3小時起 × 5/3

    // ── 月薪制 ──────────────────────────────
    public decimal MonthlyMinimumWage { get; set; } = 27470m;       // 月薪最低薪資（2024）
    public double MonthlyDailyMaxHours { get; set; } = 8.0;         // 月薪制每日正常工時上限
    public double MonthlyWeeklyMaxHours { get; set; } = 40.0;       // 月薪制每周正常工時上限

    // 月薪加班費率
    public decimal MonthlyOT1Rate { get; set; } = 1.34m;
    public decimal MonthlyOT2Rate { get; set; } = 1.67m;

    // ── 假日加班 ────────────────────────────
    public decimal HolidayOTRate { get; set; } = 2.0m;              // 假日出勤加倍

    // ── 休假 ────────────────────────────────
    public int WeeklyRestDays { get; set; } = 2;                     // 每周例休日數
    public double MaxMonthlyOTHours { get; set; } = 46.0;           // 每月加班上限（小時）
}

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
