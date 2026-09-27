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
}
