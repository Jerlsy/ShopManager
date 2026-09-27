using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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
    // 備份/還原時當獨立清單處理（見 ShopDataPortabilityService），這裡忽略序列化避免混淆
    [JsonIgnore] public List<SalaryEmployeeRecord> EmployeeRecords { get; set; } = new();
}

/// <summary>每位員工的薪資明細快照</summary>
public class SalaryEmployeeRecord
{
    [Key] public int Id { get; set; }
    public int SalaryRecordId { get; set; }
    public int EmployeeId { get; set; }
    [JsonIgnore] public Employee Employee { get; set; } = null!;

    // 計算當下的薪資設定快照
    public SalaryType SalaryType { get; set; }
    public decimal HourlyRate { get; set; }          // 平日時薪 snapshot
    public decimal HolidayHourlyRate { get; set; }   // 假日時薪 snapshot
    public decimal MonthlyBase { get; set; }
    public decimal OT1Rate { get; set; }             // 加班倍率（第2小時起）snapshot
    public decimal OT2Rate { get; set; }             // 加班倍率（第3小時起）snapshot

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

    // 打卡對照，全月合計（ClockedHours 為 null＝該次計算未匯入打卡資料）
    public double  ScheduledHours { get; set; }
    public double? ClockedHours   { get; set; }

    // 打卡對照，依平日／假日拆分（供卡片並排顯示「排班工時」vs「打卡紀錄」）
    public double  WeekdayScheduledHours { get; set; }
    public double  HolidayScheduledHours { get; set; }
    public double? WeekdayClockedHours   { get; set; }
    public double? HolidayClockedHours   { get; set; }

    public List<AttendanceIssue>  AttendanceIssues { get; set; } = new();
    /// <summary>每日排班／打卡對照，供「每日出勤明細」視窗顯示；隨記錄存檔，重新載入也看得到</summary>
    public List<SalaryDailyEntry> DailyEntries     { get; set; } = new();

    [JsonIgnore] public List<SalaryBonusItem> BonusItems { get; set; } = new();

    // 支薪狀態
    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal TotalAmount => BaseAmount + BonusItems.Sum(b => b.Amount);

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsUnderMinWage { get; set; }
}

/// <summary>
/// 單日排班／打卡對照（顯示＋存檔共用）：Hours 是排班工時，ClockedHours 是當天打卡工時
/// （null＝當天沒有打卡紀錄，例如未匯入打卡或當天本來就沒排班也沒打卡）。
/// IssueLabel 帶出勤差異的簡短說明（例如「遲到 40 分」「有排班未打卡」），沒有差異則為 null。
/// </summary>
public class SalaryDailyEntry
{
    public DateOnly Date    { get; init; }
    public double   Hours   { get; init; }
    public string   TypeTag { get; init; } = string.Empty;   // 平日 / 假日 / 替代
    public decimal? OverrideAmount { get; init; }

    public DateTime? ClockIn       { get; init; }
    public DateTime? ClockOut      { get; init; }
    public double?   ClockedHours  { get; init; }
    public string?   IssueLabel    { get; init; }
    /// <summary>搭配 IssueLabel 決定標籤顏色（遲到早退／缺打卡／未排班各自一色，一眼分辨嚴重程度）</summary>
    public AttendanceIssueType? IssueType { get; init; }
    /// <summary>
    /// 標籤顏色用（XAML DataTrigger 綁這個字串，不直接綁 nullable enum）。
    /// IssueType 是後來才加的欄位：之前版本存進 DB 的每日明細只有 IssueLabel 文字、沒有 IssueType，
    /// 薪資頁載入已存月份時直接讀 DB，不會重算——所以缺值時要從文字反推，否則舊資料永遠是預設色。
    /// </summary>
    public string IssueTypeName => (IssueType ?? InferIssueType(IssueLabel))?.ToString() ?? string.Empty;

    private static AttendanceIssueType? InferIssueType(string? label) => label switch
    {
        null or ""                                      => null,
        _ when label.Contains("遲到") || label.Contains("早退") => AttendanceIssueType.LateOrEarly,
        "有排班未打卡"                                   => AttendanceIssueType.NoPunch,
        "未排班出勤"                                     => AttendanceIssueType.Unscheduled,
        "打卡不完整" or "未排班打卡"                      => AttendanceIssueType.HalfPunch,
        _                                               => null,
    };

    public string DayLabel => Date.DayOfWeek switch
    {
        DayOfWeek.Monday    => "一",
        DayOfWeek.Tuesday   => "二",
        DayOfWeek.Wednesday => "三",
        DayOfWeek.Thursday  => "四",
        DayOfWeek.Friday    => "五",
        DayOfWeek.Saturday  => "六",
        DayOfWeek.Sunday    => "日",
        _ => ""
    };
    public string Label => $"{Date.Month:D2}/{Date.Day:D2}（{DayLabel}）";
    public bool HasClock => ClockIn.HasValue || ClockOut.HasValue;
    public string ClockRangeText => ClockIn.HasValue && ClockOut.HasValue
        ? $"{ClockIn:HH:mm}–{ClockOut:HH:mm}"
        : ClockIn.HasValue ? $"{ClockIn:HH:mm}–？"
        : ClockOut.HasValue ? $"？–{ClockOut:HH:mm}"
        : "—";

    /// <summary>
    /// 「打卡」欄一行顯示用：時段＋工時併成一句（例如「13:27–19:21（5.9 hr）」），
    /// 不再跟「排班」欄一樣分兩行——分兩行時兩欄行數不一致，卡片看起來像沒對齊。
    /// </summary>
    public string ClockSummaryText => !HasClock
        ? "無打卡紀錄"
        : ClockedHours.HasValue ? $"{ClockRangeText}（{ClockedHours:0.#} hr）" : ClockRangeText;

    /// <summary>「排班」欄一行顯示用：工時＋指定金額（如有）併成一句，跟整列改一行的排版一致</summary>
    public string HoursSummaryText => OverrideAmount.HasValue
        ? $"{Hours:0.#} hr（指定 {OverrideAmount:N0} 元）"
        : $"{Hours:0.#} hr";
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
    AttendanceAdjust  = 9,   // 出勤差異接受後自動產生；重新計算薪資時會被移除重建
}
