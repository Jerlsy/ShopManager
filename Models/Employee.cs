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
    public DateOnly? BirthDate { get; set; }
    public byte[]? AvatarPhotoData { get; set; }
    public DateOnly? InterviewDate { get; set; }

    // 聯繫方式（JSON，與店舖設定相同結構）
    public List<ContactInfo> ContactInfos { get; set; } = new();

    // 偏好班別 ID 清單（空 = 不限）
    public List<int> PreferredShiftIds { get; set; } = new();

    // 自訂聯絡方式（保留舊資料相容；新增功能請用 ContactInfos）
    public List<CustomContact> CustomContacts { get; set; } = new();

    // 預設班別（FK）
    public int? DefaultShiftId { get; set; }
    public ShiftSetting? DefaultShift { get; set; }

    // 排班規則
    public List<ScheduleRule> ScheduleRules { get; set; } = new();

    // 薪資類型（FK）
    public int? DefaultSalaryId { get; set; }
    public SalarySetting? DefaultSalary { get; set; }

    // 時薪制假日薪資方案（FK，僅 Hourly 員工使用）
    public int? HolidaySalaryId { get; set; }
    public SalarySetting? HolidaySalary { get; set; }

    // 預設獎金
    public List<DefaultBonus> DefaultBonuses { get; set; } = new();

    public DateOnly HireDate { get; set; }                               // 到職日
    public DateOnly? ResignDate { get; set; }                            // 離職日（null = 在職）

    public string ColorHex { get; set; } = string.Empty;                // 識別色（Hex）
    public string? LineUserId { get; set; }                             // LINE 推播綁定 userId

    // 薪資戶
    public string? BankCode { get; set; }                               // 銀行代碼（例：822）
    public string? BankAccount { get; set; }                            // 帳號（純數字）
    public string? BankAccountName { get; set; }                        // 戶名

    public bool IsResigned => ResignDate.HasValue && ResignDate.Value <= DateOnly.FromDateTime(DateTime.Today);

    public bool IsBirthdayThisMonth =>
        BirthDate.HasValue && BirthDate.Value.Month == DateTime.Today.Month;

    public bool HasPrimaryContact => ContactInfos.Count > 0;

    public bool HasBirthDate => BirthDate.HasValue;

    public string HireDateLabel => $"到職 {HireDate:yyyy/MM}";

    public string BirthDateLabel => BirthDate.HasValue ? $"{BirthDate.Value:MM/dd}" : string.Empty;

    public string PrimaryContact =>
        ContactInfos.Count > 0 ? $"{ContactInfos[0].Type}：{ContactInfos[0].Value}" : string.Empty;

    public string ShiftPreferenceLabel =>
        PreferredShiftIds.Count == 0 ? "班別不限" : $"偏好 {PreferredShiftIds.Count} 個班別";

    public string DisplayInitial =>
        !string.IsNullOrEmpty(EnglishName) ? EnglishName[0].ToString() :
        Name.Length > 0 ? Name[0].ToString() : "?";
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

    // 不與同班 / 不與同天：Employee.Id 清單
    public List<int> ExcludedColleagueIds { get; set; } = new();
}

public enum ScheduleRuleType
{
    FixedOff = 0,       // 固定休假
    ExcludeShift = 1,   // 排除班別
    NotWith = 2,        // 不與同班
    NotWithDay = 3      // 不與同天
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
