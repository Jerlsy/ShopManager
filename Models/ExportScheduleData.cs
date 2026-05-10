namespace ShopManager.Models;

public class ExportScheduleData
{
    public string ShopName { get; init; } = string.Empty;
    public int Year { get; init; }
    public int Month { get; init; }
    public int DaysInMonth { get; init; }
    public List<DayColumn> Columns { get; init; } = new();
    public List<EmployeeRow> Rows { get; init; } = new();

    public record DayColumn(int Day, string DayOfWeekLabel, bool IsClosed, string? HolidayName);
    public record EmployeeRow(string Name, IReadOnlyList<string> Cells);
}
