namespace ShopManager.Models;

public class ExportScheduleData
{
    public string ShopName { get; init; } = string.Empty;
    public int Year { get; init; }
    public int Month { get; init; }
    public int DaysInMonth { get; init; }
    public List<DayColumn> Columns { get; init; } = new();
    public List<EmployeeRow> Rows { get; init; } = new();
    /// <summary>只包含本月實際有排班紀錄的班別，供圖例使用。</summary>
    public List<ShiftLegendItem> ShiftLegend { get; init; } = new();

    public record DayColumn(int Day, string DayOfWeekLabel, bool IsClosed, string? HolidayName);

    /// <summary>
    /// ShiftIds[i] 對應 Columns[i]：
    ///   null = 未排班（空格）
    ///   int  = 班別 ID（查 ShiftLegend 取顏色）
    ///   -1   = 店休（IsClosed 已在 Column 標記，用於防呆）
    /// </summary>
    public record EmployeeRow(string Name, IReadOnlyList<int?> ShiftIds);

    public record ShiftLegendItem(int Id, string Alias, string ColorHex, string TimeRange);
}
