using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using ShopManager.Services;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;

namespace ShopManager.ViewModels;

public partial class SalaryViewModel : ObservableObject
{
    private readonly SalaryCalculationService _salaryService;
    private readonly MonthlyScheduleService   _scheduleService;
    private readonly EmployeeService          _employeeService;
    private readonly SalarySettingService     _salarySettingService;
    private readonly ShopContext              _shopContext;
    private readonly IAppSnackbarService      _snackbar;
    private readonly HttpClient               _http;

    private LaborLawSetting? _laborLaw;

    public SalaryViewModel(
        SalaryCalculationService salaryService,
        MonthlyScheduleService   scheduleService,
        EmployeeService          employeeService,
        SalarySettingService     salarySettingService,
        ShopContext              shopContext,
        IAppSnackbarService      snackbar,
        HttpClient               http)
    {
        _salaryService        = salaryService;
        _scheduleService      = scheduleService;
        _employeeService      = employeeService;
        _salarySettingService = salarySettingService;
        _shopContext          = shopContext;
        _snackbar             = snackbar;
        _http                 = http;
    }

    // ── 狀態 ────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CalculateCommand))]
    private bool _isLoading;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _isLoadingHolidays;
    [ObservableProperty] private bool _hasMonthlyEmployees;
    [ObservableProperty] private bool _hasSavedRecord;
    [ObservableProperty] private string _savedLabel = string.Empty;

    public ObservableCollection<SalaryScheduleItem>  AvailableSchedules { get; } = new();
    public ObservableCollection<SalaryHolidayItem>   HolidayItems       { get; } = new();
    public ObservableCollection<EmployeeSalaryItem>  EmployeeItems      { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CalculateCommand))]
    private SalaryScheduleItem? _selectedScheduleItem;

    public bool CanCalculate => SelectedScheduleItem is not null && !IsLoading;

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var schedules = await _scheduleService.GetAllAsync();
            _laborLaw     = await _salarySettingService.GetLaborLawAsync();

            AvailableSchedules.Clear();
            foreach (var s in schedules.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month))
                AvailableSchedules.Add(new SalaryScheduleItem { Schedule = s });

            if (AvailableSchedules.Count > 0)
                SelectedScheduleItem = AvailableSchedules[0];
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedScheduleItemChanged(SalaryScheduleItem? value)
    {
        HolidayItems.Clear();
        EmployeeItems.Clear();
        HasResult         = false;
        HasMonthlyEmployees = false;
        HasSavedRecord    = false;

        if (value is not null)
            _ = LoadHolidaysAndCheckSavedAsync(value.Schedule);
    }

    private async Task LoadHolidaysAndCheckSavedAsync(MonthlySchedule schedule)
    {
        // 檢查是否已有儲存的薪資記錄
        var saved = await _salaryService.GetByScheduleAsync(schedule.Id);
        if (saved is not null)
        {
            HasSavedRecord = true;
            SavedLabel     = $"已有 {saved.UpdatedAt:MM/dd HH:mm} 儲存的記錄";
        }
        else
        {
            HasSavedRecord = false;
            SavedLabel     = string.Empty;
        }

        // 抓取國定假日（給月薪制用）
        await FetchHolidaysAsync(schedule.Year, schedule.Month);
    }

    private async Task FetchHolidaysAsync(int year, int month)
    {
        IsLoadingHolidays = true;
        HolidayItems.Clear();
        try
        {
            var url  = $"https://cdn.jsdelivr.net/gh/ruyut/TaiwanCalendar/data/{year}.json";
            var json = await _http.GetStringAsync(url);
            var all  = JsonSerializer.Deserialize<List<CalendarDayDto>>(json);
            if (all is null) return;

            var prefix = $"{year}{month:D2}";
            var holidays = all
                .Where(d => d.Date.StartsWith(prefix)
                         && d.IsHoliday
                         && !string.IsNullOrEmpty(d.Description)
                         && d.Week != "六" && d.Week != "日")
                .Select(d => new SalaryHolidayItem
                {
                    Date        = new DateOnly(year, month, int.Parse(d.Date[6..])),
                    Description = d.Description ?? string.Empty,
                    IsChecked   = true,
                });

            foreach (var h in holidays)
            {
                h.PropertyChanged += (_, _) =>
                {
                    if (HasResult) _ = RecalculateAsync();
                };
                HolidayItems.Add(h);
            }
        }
        catch { /* 無網路時靜默略過 */ }
        finally
        {
            IsLoadingHolidays = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCalculate))]
    private async Task CalculateAsync()
    {
        if (SelectedScheduleItem is null || _laborLaw is null) return;
        IsLoading = true;
        try
        {
            await RecalculateAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RecalculateAsync()
    {
        if (SelectedScheduleItem is null || _laborLaw is null) return;

        var schedule  = await _scheduleService.GetAsync(
            SelectedScheduleItem.Schedule.Year,
            SelectedScheduleItem.Schedule.Month);
        if (schedule is null) return;

        var employees = await _employeeService.GetAllAsync();

        // 只計算有設定薪資的員工，並依照排班篩選（當月有排班的員工）
        var scheduledEmpIds = schedule.Entries.Select(e => e.EmployeeId).ToHashSet();
        var eligibleEmps = employees
            .Where(e => !e.IsResigned && e.DefaultSalary is not null && scheduledEmpIds.Contains(e.Id))
            .ToList();

        HasMonthlyEmployees = eligibleEmps.Any(e => e.DefaultSalary!.Type == SalaryType.Monthly);

        var holidayDates = HolidayItems
            .Where(h => h.IsChecked)
            .Select(h => h.Date)
            .ToList();

        var record = _salaryService.Calculate(
            schedule, eligibleEmps, _laborLaw, holidayDates, _shopContext.ShopId);

        // 每日明細：依員工 ID 分組，彙整日期 + 工時 + 類型標籤
        var closedSet  = schedule.ClosedDays.ToHashSet();
        var holidaySet = holidayDates.Select(d => d.Day).ToHashSet();
        var dailyByEmp = schedule.Entries
            .Where(e => e.ShiftSetting is not null)
            .GroupBy(e => e.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(e => e.Date)
                       .Select(dg =>
                       {
                           var date  = dg.Key;
                           var hours = dg.Sum(e => e.ShiftSetting!.WorkHours);
                           var empSalary = eligibleEmps.FirstOrDefault(em => em.Id == g.Key)?.DefaultSalary;
                           var tag = empSalary?.Type == SalaryType.Monthly && holidaySet.Contains(date.Day)
                               ? "假日"
                               : closedSet.Contains(date.Day)
                               ? "店休"
                               : "正常";
                           return new SalaryDailyEntry { Date = date, Hours = hours, TypeTag = tag };
                       })
                       .OrderBy(x => x.Date)
                       .ToList());

        // 最低薪資驗證
        foreach (var empRec in record.EmployeeRecords)
        {
            empRec.IsUnderMinWage = empRec.SalaryType switch
            {
                SalaryType.Hourly  => empRec.NormalPay < (decimal)empRec.NormalHours * _laborLaw.HourlyMinimumWage,
                SalaryType.Monthly => empRec.BaseAmount < _laborLaw.MonthlyMinimumWage,
                _                  => false,
            };
        }

        // 保留既有 BonusItems（若已計算過）
        var existingBonus = EmployeeItems.ToDictionary(
            i => i.Employee.Id,
            i => i.BonusItems.ToList());

        EmployeeItems.Clear();
        foreach (var empRec in record.EmployeeRecords)
        {
            var item = new EmployeeSalaryItem
            {
                Employee       = empRec.Employee,
                SalaryType     = empRec.SalaryType,
                NormalHours    = empRec.NormalHours,
                OT1Hours       = empRec.OT1Hours,
                OT2Hours       = empRec.OT2Hours,
                RestDayHours   = empRec.RestDayHours,
                HolidayHours   = empRec.HolidayHours,
                NormalPay      = empRec.NormalPay,
                OT1Pay         = empRec.OT1Pay,
                OT2Pay         = empRec.OT2Pay,
                RestDayPay     = empRec.RestDayPay,
                HolidayPay     = empRec.HolidayPay,
                BaseAmount     = empRec.BaseAmount,
                HourlyRate     = empRec.HourlyRate,
                MonthlyBase    = empRec.MonthlyBase,
                IsUnderMinWage = empRec.IsUnderMinWage,
            };

            // 先帶入模型的預設獎金
            var sourceBonuses = existingBonus.TryGetValue(empRec.Employee.Id, out var prev)
                ? prev
                : empRec.BonusItems.Select(b => new BonusLineItem
                {
                    SelectedPreset = BonusLineItem.Presets.FirstOrDefault(p => p.Type == b.PresetType)
                                     ?? BonusLineItem.Presets[0],
                    CustomLabel = b.Label,
                    Amount      = b.Amount,
                }).ToList();

            foreach (var bonus in sourceBonuses)
            {
                bonus.OnChanged = () => item.RefreshTotals();
                item.BonusItems.Add(bonus);
            }

            if (dailyByEmp.TryGetValue(empRec.Employee.Id, out var entries))
                foreach (var e in entries) item.DailyEntries.Add(e);

            EmployeeItems.Add(item);
        }

        HasResult = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedScheduleItem is null || !HasResult) return;
        IsSaving = true;
        try
        {
            var schedule = await _scheduleService.GetAsync(
                SelectedScheduleItem.Schedule.Year,
                SelectedScheduleItem.Schedule.Month);
            if (schedule is null) return;

            var employees = await _employeeService.GetAllAsync();
            var scheduledEmpIds = schedule.Entries.Select(e => e.EmployeeId).ToHashSet();
            var eligibleEmps = employees
                .Where(e => !e.IsResigned && e.DefaultSalary is not null && scheduledEmpIds.Contains(e.Id))
                .ToList();

            var holidayDates = HolidayItems.Where(h => h.IsChecked).Select(h => h.Date).ToList();

            var record = _salaryService.Calculate(
                schedule, eligibleEmps, _laborLaw!, holidayDates, _shopContext.ShopId);

            // 將 UI 上的 BonusItems 寫回
            var bonusMap = EmployeeItems.ToDictionary(
                i => i.Employee.Id,
                i => i.BonusItems.Select(b => b.ToModel()).ToList());

            foreach (var empRec in record.EmployeeRecords)
            {
                if (bonusMap.TryGetValue(empRec.Employee.Id, out var items))
                    empRec.BonusItems.AddRange(items);
            }

            await _salaryService.SaveAsync(record);
            HasSavedRecord = true;
            SavedLabel     = $"已儲存 {DateTime.Now:MM/dd HH:mm}";
            _snackbar.ShowSuccess("薪資記錄已儲存");
        }
        catch
        {
            _snackbar.ShowError("儲存失敗，請重試");
        }
        finally
        {
            IsSaving = false;
        }
    }
}
