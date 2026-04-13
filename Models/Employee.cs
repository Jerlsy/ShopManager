using System.ComponentModel.DataAnnotations;

namespace ShopManager.Models;

/// <summary>員工資料</summary>
public class Employee
{
    [Key] public int Id { get; set; }
    public Guid ShopId { get; set; }
    [Required] public string Name { get; set; } = string.Empty;         // 姓名
    public string IdNumber { get; set; } = string.Empty;                 // 身分證字號
    public string Address { get; set; } = string.Empty;                  // 聯絡地址
    public string Phone { get; set; } = string.Empty;                    // 聯繫電話

    // 通訊軟體（Line / Messenger 等，限一組）
    public string? MessengerType { get; set; }   // Line / Messenger / WeChat ...
    public string? MessengerValue { get; set; }

    // 自訂聯絡方式（可多組）
    public List<CustomContact> CustomContacts { get; set; } = new();

    // 預設班別（FK）
    public int? DefaultShiftId { get; set; }
    public ShiftSetting? DefaultShift { get; set; }

    // 排班規則
    public List<ScheduleRule> ScheduleRules { get; set; } = new();

    // 薪資類型（FK）
    public int? DefaultSalaryId { get; set; }
    public SalarySetting? DefaultSalary { get; set; }

    // 預設獎金
    public List<DefaultBonus> DefaultBonuses { get; set; } = new();

    public DateOnly HireDate { get; set; }                               // 到職日
    public DateOnly? ResignDate { get; set; }                            // 離職日（null = 在職）

    public bool IsResigned => ResignDate.HasValue && ResignDate.Value <= DateOnly.FromDateTime(DateTime.Today);
}

/// <summary>自訂聯絡方式</summary>
public class CustomContact
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Label { get; set; } = string.Empty;   // 使用者自訂名稱
    public string Value { get; set; } = string.Empty;   // 使用者自訂值
}

/// <summary>排班規則</summary>
public class ScheduleRule
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public ScheduleRuleType Type { get; set; }

    // 固定休假：DayOfWeek 清單（0=Sunday ... 6=Saturday）
    public List<int> FixedOffDays { get; set; } = new();

    // 排除班別：ShiftSetting.Id 清單
    public List<int> ExcludedShiftIds { get; set; } = new();

    // 不與共事：Employee.Id 清單
    public List<int> ExcludedColleagueIds { get; set; } = new();
}

public enum ScheduleRuleType
{
    FixedOff = 0,       // 固定休假
    ExcludeShift = 1,   // 排除班別
    NotWith = 2         // 不與共事
}

/// <summary>預設獎金</summary>
public class DefaultBonus
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string Label { get; set; } = string.Empty;   // 獎金名稱
    public decimal Amount { get; set; }                 // 金額
    public string Description { get; set; } = string.Empty;
}
