using System.ComponentModel.DataAnnotations;

namespace ShopManager.Models;

/// <summary>店鋪基本設定 + 行事曆設定</summary>
public class ShopSetting
{
    [Key] public int Id { get; set; }
    public Guid ShopId { get; set; }

    // ── 店鋪資訊 ──────────────────────────────
    [Required] public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public byte[]? LogoPhotoData { get; set; }
    public List<ContactInfo> ContactInfos { get; set; } = new();

    // ── 行事曆設定 ────────────────────────────
    /// <summary>一周起始日：0=Sunday, 1=Monday</summary>
    public int WeekStartDay { get; set; } = 1; // 預設周一

    /// <summary>每周固定店休日（DayOfWeek 值清單，0=Sunday...6=Saturday）</summary>
    public List<int> ClosedDaysOfWeek { get; set; } = new();

    /// <summary>國定假日是否休假</summary>
    public bool NationalHolidaysOff { get; set; } = true;
}

/// <summary>聯絡方式（Email、FB、IG、Line 等）</summary>
public class ContactInfo
{
    public string Type { get; set; } = string.Empty;   // Email / Facebook / IG / Line / Other
    public string Label { get; set; } = string.Empty;  // 顯示名稱
    public string Value { get; set; } = string.Empty;  // 帳號/網址/電話
}
