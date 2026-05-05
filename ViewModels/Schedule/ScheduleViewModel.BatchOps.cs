using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using System.Collections.ObjectModel;

namespace ShopManager.ViewModels;

public partial class ScheduleViewModel
{
    // ══════════════════════════════════════════
    // 功能三：批次分配
    // ══════════════════════════════════════════
    [ObservableProperty] private bool _isBatchMode;
    [ObservableProperty] private Employee? _batchEmployee;
    [ObservableProperty] private ShiftSetting? _batchShift;
    [ObservableProperty] private DateTime? _batchStartDate = DateTime.Today;
    [ObservableProperty] private DateTime? _batchEndDate   = DateTime.Today.AddDays(6);

    public ObservableCollection<DayOfWeekOption> BatchWeekdayOptions { get; } = new()
    {
        new(DayOfWeek.Monday,    "周一") { IsChecked = true },
        new(DayOfWeek.Tuesday,   "周二") { IsChecked = true },
        new(DayOfWeek.Wednesday, "周三") { IsChecked = true },
        new(DayOfWeek.Thursday,  "周四") { IsChecked = true },
        new(DayOfWeek.Friday,    "周五") { IsChecked = true },
        new(DayOfWeek.Saturday,  "周六"),
        new(DayOfWeek.Sunday,    "周日"),
    };

    [RelayCommand]
    public void StartBatch()
    {
        BatchEmployee  = ActiveEmployees.FirstOrDefault();
        BatchShift     = EnabledShifts.FirstOrDefault();
        BatchStartDate = new DateTime(SelectedYear, SelectedMonth, 1);
        BatchEndDate   = new DateTime(SelectedYear, SelectedMonth,
            DateTime.DaysInMonth(SelectedYear, SelectedMonth));
        IsCreating    = false;
        IsQuickAdding = false;
        IsBatchMode   = true;
    }

    [RelayCommand]
    public void CancelBatch() => IsBatchMode = false;

    [RelayCommand]
    public async Task ConfirmBatchAsync()
    {
        if (BatchEmployee is null || BatchShift is null ||
            BatchStartDate is null || BatchEndDate is null) return;

        var start = DateOnly.FromDateTime(BatchStartDate.Value);
        var end   = DateOnly.FromDateTime(BatchEndDate.Value);
        if (start > end)
        {
            _snackbarService.ShowError("起始日期不能晚於結束日期");
            return;
        }

        var selectedDays = BatchWeekdayOptions
            .Where(o => o.IsChecked)
            .Select(o => o.Day)
            .ToHashSet();

        if (!selectedDays.Any())
        {
            _snackbarService.ShowError("請至少選擇一個星期日");
            return;
        }

        // 按年月分組載入需要的班表
        var scheduleCache = new Dictionary<(int year, int month), MonthlySchedule?>();
        var entriesToAdd  = new List<ScheduleEntry>();

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (!selectedDays.Contains(date.DayOfWeek)) continue;

            var key = (date.Year, date.Month);
            if (!scheduleCache.TryGetValue(key, out var schedule))
            {
                schedule = await _scheduleService.GetAsync(date.Year, date.Month);
                scheduleCache[key] = schedule;
            }
            if (schedule is null) continue;

            if (schedule.ClosedDays.Contains(date.Day)) continue;

            entriesToAdd.Add(new ScheduleEntry
            {
                MonthlyScheduleId = schedule.Id,
                EmployeeId        = BatchEmployee.Id,
                Date              = date,
                ShiftSettingId    = BatchShift.Id,
            });
        }

        var addedEntries = await _entryService.AddEntriesAsync(entriesToAdd);
        IsBatchMode = false;
        await LoadScheduleAsync();
        _snackbarService.ShowSuccess(
            $"已新增 {addedEntries.Count} 筆排班（略過 {entriesToAdd.Count - addedEntries.Count} 筆重複）");

        if (addedEntries.Count > 0)
        {
            var ids = addedEntries.Select(e => e.Id).ToList();
            PushUndo(new UndoAction(
                $"批次新增 {BatchEmployee?.Name ?? "員工"} {addedEntries.Count} 筆排班",
                () => _entryService.RemoveEntriesAsync(ids)));
        }
    }

    // ══════════════════════════════════════════
    // 功能四：複製上週班表
    // ══════════════════════════════════════════
    [RelayCommand]
    public async Task CopyLastWeekAsync()
    {
        if (CurrentSchedule is null) return;

        var weekStart     = GetCurrentWeekStart();
        var prevWeekStart = weekStart.AddDays(-7);
        var prevWeekEnd   = weekStart.AddDays(-1);

        var prevEntries = await _entryService.GetEntriesByDateRangeAsync(prevWeekStart, prevWeekEnd);

        if (!prevEntries.Any())
        {
            _snackbarService.ShowError("上週無任何排班可複製");
            return;
        }

        var scheduleCache = new Dictionary<(int year, int month), MonthlySchedule?>();
        var entriesToAdd  = new List<ScheduleEntry>();

        foreach (var entry in prevEntries)
        {
            var targetDate = entry.Date.AddDays(7);
            var key = (targetDate.Year, targetDate.Month);

            if (!scheduleCache.TryGetValue(key, out var targetSchedule))
            {
                targetSchedule = await _scheduleService.GetAsync(targetDate.Year, targetDate.Month);
                scheduleCache[key] = targetSchedule;
            }
            if (targetSchedule is null) continue;

            entriesToAdd.Add(new ScheduleEntry
            {
                MonthlyScheduleId = targetSchedule.Id,
                EmployeeId        = entry.EmployeeId,
                Date              = targetDate,
                ShiftSettingId    = entry.ShiftSettingId,
            });
        }

        var addedEntries = await _entryService.AddEntriesAsync(entriesToAdd);
        await LoadScheduleAsync();

        if (addedEntries.Count == 0)
            _snackbarService.ShowError("本週已有相同排班，無需複製");
        else
        {
            _snackbarService.ShowSuccess($"已複製 {addedEntries.Count} 筆排班到本週");
            var ids = addedEntries.Select(e => e.Id).ToList();
            PushUndo(new UndoAction($"複製上週 {addedEntries.Count} 筆排班",
                () => _entryService.RemoveEntriesAsync(ids)));
        }
    }
}
