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
    /// <summary>LINE 推播收件人：已綁定 LINE 的員工 + 店主帳號。</summary>
    public List<PushRecipient> PushRecipients { get; init; } = new();
    public string? LineChannelAccessToken { get; init; }
    public string? LineWorkerUrl { get; init; }
    public string? LineWorkerApiKey { get; init; }

    public record DayColumn(int Day, string DayOfWeekLabel, bool IsClosed, string? HolidayName);

    /// <summary>ShiftIds[i] 對應 Columns[i]：null=未排, int=班別ID</summary>
    public record EmployeeRow(string Name, IReadOnlyList<int?> ShiftIds);

    public record ShiftLegendItem(int Id, string Alias, string ColorHex, string TimeRange);

    /// <summary>ShiftIds[i] 對應 Columns[i]，供個人班表文字訊息使用；業主帳號為 null。</summary>
    public record PushRecipient(string UserId, string DisplayName, string? PictureUrl, bool IsOwner,
        IReadOnlyList<int?>? ShiftIds = null);
}
