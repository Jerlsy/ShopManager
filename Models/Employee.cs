using System.ComponentModel.DataAnnotations;

namespace ShopManager.Models;

/// <summary>員工資料</summary>
public class Employee
{
    [Key] public int Id { get; set; }
    public Guid ShopId { get; set; }
    [Required] public string Name { get; set; } = string.Empty;
    public string? EnglishName { get; set; }
    public string IdNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateOnly? BirthDate { get; set; }
    public byte[]? AvatarPhotoData { get; set; }
    public DateOnly? InterviewDate { get; set; }

    // 聯繫方式（JSON，與店舖設定相同結構）
    public List<ContactInfo> ContactInfos { get; set; } = new();

    // 偏好班別 ID 清單（空 = 不限）
    public List<int> PreferredShiftIds { get; set; } = new();

    // 通訊軟體（保留向下相容）
    public string? MessengerType { get; set; }
    public string? MessengerValue { get; set; }

    // 自訂聯絡方式（保留向下相容）
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

    public string ColorHex { get; set; } = string.Empty;                // 識別色（Hex）

    public bool IsResigned => ResignDate.HasValue && ResignDate.Value <= DateOnly.FromDateTime(DateTime.Today);

    public string PrimaryContact =>
        ContactInfos.Count > 0 ? $"{ContactInfos[0].Type}：{ContactInfos[0].Value}" : string.Empty;

    public string ShiftPreferenceLabel =>
        PreferredShiftIds.Count == 0 ? "班別不限" : $"偏好 {PreferredShiftIds.Count} 個班別";
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
