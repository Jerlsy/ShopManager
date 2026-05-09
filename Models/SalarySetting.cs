using System.ComponentModel.DataAnnotations;

namespace ShopManager.Models;

/// <summary>預設薪資設定</summary>
public class SalarySetting
{
    [Key] public int Id { get; set; }
    public Guid ShopId { get; set; }
    [Required] public string Alias { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SalaryType Type { get; set; } = SalaryType.Hourly;

    // ── 時薪制 ──────────────────────────────
    public decimal? HourlyRate { get; set; }        // 平日時薪

    // ── 月薪制 ──────────────────────────────
    public decimal? MonthlyBase { get; set; }        // 底薪

    // ── 舊欄位相容（DB 有 NOT NULL 約束，保留避免寫入失敗）──────
    public decimal? ContractAmount { get; set; }
    public string ContractCycle { get; set; } = string.Empty;

    // ── 共用設定 ────────────────────────────
    public decimal? OT1Rate { get; set; }
    public decimal? OT2Rate { get; set; }
    public decimal? HolidayRate { get; set; }
    public double? DailyMaxHours { get; set; }
    public double? WeeklyMaxHours { get; set; }
}

public enum SalaryType
{
    Hourly = 0,
    Monthly = 1,
}
