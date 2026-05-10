using ShopManager.Models;

namespace ShopManager.ViewModels;

public partial class ScheduleViewModel
{
    public async Task<ExportScheduleData?> BuildExportDataAsync()
    {
        if (CurrentSchedule is null) return null;

        var setting = await _shopSettingService.GetAsync();
        var shopName = setting?.Name ?? string.Empty;

        var daysInMonth = DateTime.DaysInMonth(CurrentSchedule.Year, CurrentSchedule.Month);

        var columns = Enumerable.Range(1, daysInMonth).Select(d =>
        {
            var date = new DateOnly(CurrentSchedule.Year, CurrentSchedule.Month, d);
            return new ExportScheduleData.DayColumn(
                d,
                GetDayOfWeekText(date.DayOfWeek),
                CurrentSchedule.ClosedDays.Contains(d),
                _monthHolidays.GetValueOrDefault(d));
        }).ToList();

        // 每位員工每天的班別 ID（null = 未排）
        var entryByEmp = CurrentSchedule.Entries
            .GroupBy(e => e.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(e => e.Date.Day, e => e.ShiftSettingId));

        var rows = ActiveEmployees.Select(emp =>
        {
            var dayShifts = entryByEmp.GetValueOrDefault(emp.Id, new());
            IReadOnlyList<int?> shiftIds = Enumerable.Range(1, daysInMonth)
                .Select(d => dayShifts.TryGetValue(d, out var id) ? (int?)id : null)
                .ToList();
            return new ExportScheduleData.EmployeeRow(emp.Name, shiftIds);
        }).ToList();

        // 圖例：只列出本月實際出現的班別，按 EnabledShifts 原始順序
        var usedShiftIds = CurrentSchedule.Entries
            .Select(e => e.ShiftSettingId)
            .ToHashSet();

        var legend = EnabledShifts
            .Where(s => usedShiftIds.Contains(s.Id))
            .Select(s => new ExportScheduleData.ShiftLegendItem(
                s.Id,
                s.Alias,
                s.Color,
                $"{s.StartTime:HH\\:mm}–{s.EndTime:HH\\:mm}"))
            .ToList();

        // 推播收件人：從 DB 重新取得最新員工資料（避免快照缺少新綁定的 LineUserId）
        var freshEmployees = await _employeeService.GetAllAsync();
        var freshById      = freshEmployees.ToDictionary(e => e.Id);
        var pushRecipients = ActiveEmployees
            .Select(e => freshById.TryGetValue(e.Id, out var fresh) ? fresh : e)
            .Where(e => !string.IsNullOrEmpty(e.LineUserId))
            .Select(e =>
            {
                var dayMap = entryByEmp.GetValueOrDefault(e.Id, new());
                IReadOnlyList<int?> shiftIds = Enumerable.Range(1, daysInMonth)
                    .Select(d => dayMap.TryGetValue(d, out var sid) ? (int?)sid : null)
                    .ToList();
                return new ExportScheduleData.PushRecipient(e.LineUserId!, e.Name, null, false, shiftIds);
            })
            .Concat((setting?.OwnerLineBindings ?? new())
                .Select(o => new ExportScheduleData.PushRecipient(o.UserId, o.DisplayName, o.PictureUrl, true)))
            .ToList();

        return new ExportScheduleData
        {
            ShopName = shopName,
            Year = CurrentSchedule.Year,
            Month = CurrentSchedule.Month,
            DaysInMonth = daysInMonth,
            Columns = columns,
            Rows = rows,
            ShiftLegend = legend,
            PushRecipients = pushRecipients,
            LineChannelAccessToken = setting?.LineChannelAccessToken,
            LineWorkerUrl = setting?.LineWorkerUrl,
            LineWorkerApiKey = setting?.LineWorkerApiKey,
        };
    }
}
