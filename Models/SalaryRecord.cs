using System.ComponentModel.DataAnnotations;

namespace ShopManager.Models;

/// <summary>月份薪資計算單（一張班表對應一份薪資記錄）</summary>
public class SalaryRecord
{
    [Key] public int Id { get; set; }
    public Guid ShopId { get; set; }
    public int MonthlyScheduleId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    /// <summary>月薪制員工適用：被標記為假日的日期清單（JSON）</summary>
    public List<DateOnly> HolidayDates { get; set; } = new();
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
    public decimal HourlyRate { get; set; }
    public decimal MonthlyBase { get; set; }
    public decimal ContractAmount { get; set; }

    // 工時明細（小時）
    public double NormalHours { get; set; }
    public double OT1Hours { get; set; }
    public double OT2Hours { get; set; }
    public double RestDayHours { get; set; }    // 店休日出勤（實際工時）
    public double HolidayHours { get; set; }   // 國定假日出勤（月薪制）

    // 薪資明細
    public decimal NormalPay { get; set; }
    public decimal OT1Pay { get; set; }
    public decimal OT2Pay { get; set; }
    public decimal RestDayPay { get; set; }
    public decimal HolidayPay { get; set; }
    public decimal BaseAmount { get; set; }    // 底薪小計（不含額外項目）

    public List<SalaryBonusItem> BonusItems { get; set; } = new();

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
    public decimal Amount { get; set; }          // 正值=獎金，負值=扣款
    public BonusPresetType PresetType { get; set; } = BonusPresetType.Custom;
}

public enum BonusPresetType
{
    Custom           = 0,
    PerfectAttendance = 1,  // 全勤獎金
    Performance      = 2,   // 績效獎金
    Project          = 3,   // 專案獎金
    Transportation   = 4,   // 交通補貼
    Meal             = 5,   // 餐飲補貼
    Holiday          = 6,   // 節日獎金
    YearEnd          = 7,   // 年終獎金
    Deduction        = 8,   // 扣款
}
