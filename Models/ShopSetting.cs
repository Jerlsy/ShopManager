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

    // ── LINE 推播設定 ─────────────────────────────
    public string? LineChannelAccessToken { get; set; }
    public string? LineWorkerUrl { get; set; }
    public string? LineWorkerApiKey { get; set; }
    public string? LineWelcomeMessage { get; set; }
    public string? LineResignMessage { get; set; }
    public List<OwnerLineBinding> OwnerLineBindings { get; set; } = new();

    /// <summary>
    /// 明確綁定要推播的群組／多人聊天室（沿用 OwnerLineBinding 形狀：UserId 存 groupId/roomId）。
    /// 機器人被邀進群組只是「候選名單」（見 LineFollower.TargetType），這裡才是真的會收到推播的清單，
    /// 邏輯與 OwnerLineBindings 一致——不是同步到的都推，要手動選過。
    /// </summary>
    public List<OwnerLineBinding> GroupLineBindings { get; set; } = new();

    // ── Google Drive 備份設定 ────────────────────
    // 注意：OAuth refresh token 不存在這裡（DPAPI 加密後存本機檔案），
    // 因為 DPAPI 綁定「本機＋本 Windows 使用者」，跟著 DB 同步到別台機器也解不開。
    /// <summary>已連結的 Google 帳號（僅顯示用）</summary>
    public string? GoogleAccountEmail { get; set; }

    /// <summary>上次成功備份／還原時，雲端檔案的 modifiedTime（用於判斷雲端是否較新）</summary>
    public DateTimeOffset? GoogleDriveLastSyncedRemoteModifiedTime { get; set; }

    // ── Gmail轉Line推播規則 ────────────────────────────
    // 規則本身只存在本機 DB；啟用中的規則會另外序列化上傳到 Drive 固定檔名的 JSON，
    // 供 Google Apps Script 讀取（詳見 GoogleDriveSyncService）。
    public List<GmailForwardRule> GmailForwardRules { get; set; } = new();

    // ── 備註（RTF Base64）────────────────────────
    public string? Notes { get; set; }
}

/// <summary>Gmail轉Line推播規則：符合寄件者／主旨條件的信件會被 Apps Script 推播給指定業主的 LINE</summary>
public class GmailForwardRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? SenderContains { get; set; }
    public string? SubjectContains { get; set; }
    /// <summary>推播對象（可複選），對應 <see cref="OwnerLineBinding.UserId"/>；顯示名稱一律從綁定清單即時查，不另存一份避免不同步</summary>
    public List<string> OwnerLineUserIds { get; set; } = new();

    /// <summary>
    /// 內容擷取欄位：列出要抓的欄位名稱（存入金額、授權金額…），推播時只帶「欄位：值」。
    /// 留空＝推播自動清理後的全文。同一個寄件者的通知信欄位名稱固定，比任何自動判斷都可靠。
    /// </summary>
    public List<string> ContentFields { get; set; } = new();

    public bool IsEnabled { get; set; } = true;
}

/// <summary>業主 LINE 帳號綁定（可綁多位管理者）</summary>
public class OwnerLineBinding
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? PictureUrl { get; set; }
}

/// <summary>聯絡方式（Email、FB、IG、Line 等）</summary>
public class ContactInfo
{
    public string Type { get; set; } = string.Empty;   // Email / Facebook / IG / Line / Other
    public string Label { get; set; } = string.Empty;  // 顯示名稱
    public string Value { get; set; } = string.Empty;  // 帳號/網址/電話
}
