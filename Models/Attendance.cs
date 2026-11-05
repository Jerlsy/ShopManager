namespace ShopManager.Models;

/// <summary>POS 匯出的一筆打卡（上班或下班）</summary>
public record ClockPunch(DateTime Time, bool IsClockIn);

/// <summary>打卡檔中的一位人員（以打卡名稱識別）</summary>
public class ClockPerson
{
    public string Name { get; init; } = string.Empty;
    public List<ClockPunch> Punches { get; } = new();
}

/// <summary>某員工某天彙整後的上下班時間（缺一邊即為只打一半卡）</summary>
public class DayPunch
{
    public DateTime? In  { get; init; }
    public DateTime? Out { get; init; }
    public bool IsComplete => In.HasValue && Out.HasValue;
    public double Hours => IsComplete ? (Out!.Value - In!.Value).TotalHours : 0;
}

/// <summary>單日工時換算成的薪資（接受差異時直接加回，免重新讀取薪資設定）</summary>
public class DayPay
{
    public double  WeekdayHours { get; set; }
    public double  HolidayHours { get; set; }
    public double  OT1Hours     { get; set; }
    public double  OT2Hours     { get; set; }
    public decimal WeekdayPay   { get; set; }
    public decimal HolidayPay   { get; set; }
    public decimal OT1Pay       { get; set; }
    public decimal OT2Pay       { get; set; }
}

/// <summary>
/// 某員工某天的計薪費率快照：差異選「指定工時」時，接受當下用它換算任意時數的當日薪資，
/// 不必再讀薪資設定（跟 IfCounted 預先算好的道理相同，只是時數由業主決定）。
/// </summary>
public class DayRates
{
    public SalaryType SalaryType { get; set; }
    public bool    IsHoliday         { get; set; }
    /// <summary>時薪制：當天適用的時薪（假日用假日方案）；月薪制：底薪 ÷ 240 的換算時薪</summary>
    public decimal HourlyRate        { get; set; }
    public decimal OT1Rate           { get; set; }
    public decimal OT2Rate           { get; set; }
    /// <summary>月薪制假日加班倍率</summary>
    public decimal HolidayMultiplier { get; set; }
    public double  DailyNormalHours  { get; set; }
}

public enum AttendanceIssueType
{
    LateOrEarly = 0,   // 遲到／早退超過寬限
    NoPunch     = 1,   // 有排班、沒打卡
    Unscheduled = 2,   // 沒排班、有打卡
    HalfPunch   = 3,   // 只打一半卡
}

public enum AttendanceDecision
{
    None    = 0,
    Ignore  = 1,   // 忽略（遲到早退：照班表計薪，不扣錢）
    Count   = 2,   // 計入工時
    Exclude = 3,   // 不計
    Deduct  = 4,   // 扣固定金額（不計工時；遲到早退則照班表計薪再扣）
    Add     = 5,   // 加固定金額（同上）
    SetHours = 6,  // 指定工時：當天改以業主輸入的時數計薪（取代原本計入的部分）
}

/// <summary>待業主確認的出勤差異（隨薪資記錄存檔，未接受前可關閉程式後繼續處理）</summary>
public class AttendanceIssue
{
    public DateOnly Date { get; set; }
    public AttendanceIssueType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ShortLabel { get; set; } = string.Empty;   // 寫入額外項目用，例如「遲到 40 分」
    /// <summary>選「計入」時要加回的工時與薪資；null 表示此筆無法計入（例如沒排班又只打一半卡）</summary>
    public DayPay? IfCounted { get; set; }
    public AttendanceDecision Decision { get; set; }
    public decimal Amount { get; set; }   // 一律正數，扣或加由 Decision 決定

    /// <summary>當天費率快照；null＝舊版資料，不提供「指定工時」</summary>
    public DayRates? Rates { get; set; }
    /// <summary>計算時當天已計入的薪資（遲到早退照班表計入的那份）；null＝當天原本沒有計入</summary>
    public DayPay? Baseline { get; set; }
    /// <summary>選「指定工時」時輸入的時數</summary>
    public double? SetHours { get; set; }
}
