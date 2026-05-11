using System.ComponentModel.DataAnnotations;

namespace ShopManager.Models;

/// <summary>月份薪資計算單（一個年月對應一份薪資記錄）</summary>
public class SalaryRecord
{
    [Key] public int Id { get; set; }
    public Guid ShopId { get; set; }
    public int MonthlyScheduleId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public List<SalaryEmployeeRecord> EmployeeRecords { get; set; } = new();
}

/// <summary>每位員工的薪資明細快照</summary>
public class SalaryEmployeeRecord
{
    [Key] public int Id { get; set; }
    public int SalaryRecordId { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    // 計算當下的薪資設定快照
    public SalaryType SalaryType { get; set; }
    public decimal HourlyRate { get; set; }          // 平日時薪 snapshot
    public decimal HolidayHourlyRate { get; set; }   // 假日時薪 snapshot
    public decimal MonthlyBase { get; set; }

    // 工時明細（小時）
    public double WeekdayHours { get; set; }
    public double HolidayHours { get; set; }
    public double OT1Hours { get; set; }
    public double OT2Hours { get; set; }

    // 薪資明細
    public decimal WeekdayPay { get; set; }
    public decimal HolidayPay { get; set; }
    public decimal OT1Pay { get; set; }
    public decimal OT2Pay { get; set; }
    public decimal OverridePay { get; set; }     // 額外設定金額合計
    public decimal BaseAmount { get; set; }      // 薪資小計（不含 BonusItems）

    public List<SalaryBonusItem> BonusItems { get; set; } = new();

    // 支薪狀態
    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal TotalAmount => BaseAmount + BonusItems.Sum(b => b.Amount);

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsUnderMinWage { get; set; }
}

/// <summary>額外薪資項目（獎金或扣款）</summary>
public class SalaryBonusItem
{
    [Key] public int Id { get; set; }
    public int SalaryEmployeeRecordId { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public BonusPresetType PresetType { get; set; } = BonusPresetType.Custom;
}

public enum BonusPresetType
{
    Custom            = 0,
    PerfectAttendance = 1,
    Performance       = 2,
    Project           = 3,
    Transportation    = 4,
    Meal              = 5,
    Holiday           = 6,
    YearEnd           = 7,
    Deduction         = 8,
}
