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

        var shiftAliases = EnabledShifts.ToDictionary(s => s.Id, s => s.Alias);
        var entryByEmp = CurrentSchedule.Entries
            .GroupBy(e => e.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(e => e.Date.Day,
                    e => shiftAliases.GetValueOrDefault(e.ShiftSettingId, "?")));

        var rows = ActiveEmployees.Select(emp =>
        {
            var dayShifts = entryByEmp.GetValueOrDefault(emp.Id, new());
            IReadOnlyList<string> cells = Enumerable.Range(1, daysInMonth)
                .Select(d => columns[d - 1].IsClosed ? "休"
                    : dayShifts.TryGetValue(d, out var alias) ? alias : "")
                .ToList();
            return new ExportScheduleData.EmployeeRow(emp.Name, cells);
        }).ToList();

        return new ExportScheduleData
        {
            ShopName = shopName,
            Year = CurrentSchedule.Year,
            Month = CurrentSchedule.Month,
            DaysInMonth = daysInMonth,
            Columns = columns,
            Rows = rows,
        };
    }
}
