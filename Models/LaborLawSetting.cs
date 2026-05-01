using System.ComponentModel.DataAnnotations;

namespace ShopManager.Models;

/// <summary>台灣勞基法出勤法規設定</summary>
public class LaborLawSetting
{
    [Key] public int Id { get; set; }

    // ── 時薪制 ──────────────────────────────
    public decimal HourlyMinimumWage { get; set; } = 183m;          // 時薪最低薪資（2024）
    // 時薪加班費率（正常工時後）
    public decimal HourlyOT1Rate { get; set; } = 1.34m;             // 延長工時前2小時 × 4/3
    public decimal HourlyOT2Rate { get; set; } = 1.67m;             // 延長工時第3小時起 × 5/3

    // ── 月薪制 ──────────────────────────────
    public decimal MonthlyMinimumWage { get; set; } = 27470m;       // 月薪最低薪資（2024）
    // 月薪加班費率
    public decimal MonthlyOT1Rate { get; set; } = 1.34m;
    public decimal MonthlyOT2Rate { get; set; } = 1.67m;

    // ── 假日加班 ────────────────────────────
    public decimal HolidayOTRate { get; set; } = 2.0m;              // 假日出勤加倍

    // ── 共用工時設定（不分薪資別）──────────────
    public double DailyNormalHours { get; set; } = 8.0;             // 每日正常工時（加班起算基準）
    public double DailyMaxHours { get; set; } = 12.0;               // 每日最長工時上限（含加班）
    public double WeeklyMaxHours { get; set; } = 40.0;              // 每週最長工時上限

    // ── 休假 ────────────────────────────────
    public int WeeklyRestDays { get; set; } = 2;                     // 每周例休日數
    public double MaxMonthlyOTHours { get; set; } = 46.0;           // 每月加班上限（小時）
    public int MaxConsecutiveWorkDays { get; set; } = 6;             // 最長連續上班日數（勞基法第36條）
}
