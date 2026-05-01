namespace ShopManager.Models;

/// <summary>
/// 班表規則衝突記錄。
/// 每次執行衝突檢查時，先清除同一班表的舊紀錄，再寫入新紀錄。
/// EntryId / EmployeeName / Date / ShiftAlias 為非正規化欄位，方便顯示時不需額外 Join。
/// </summary>
public class ScheduleConflict
{
    public int Id { get; set; }
    public int ScheduleId    { get; set; }
    public int EntryId       { get; set; }
    public int EmployeeId    { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public DateOnly Date       { get; set; }
    public string ShiftAlias   { get; set; } = string.Empty;
    public string Reason       { get; set; } = string.Empty;
}
