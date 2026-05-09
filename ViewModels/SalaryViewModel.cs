using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using ShopManager.Models;
using ShopManager.Services;
using ShopManager.Views.Dialogs;
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
    private readonly ShopSettingService       _shopSettingService;
    private readonly ShopContext              _shopContext;
    private readonly IAppSnackbarService      _snackbar;
    private readonly HttpClient               _http;

    private LaborLawSetting? _laborLaw;
    private List<int> _shopClosedDaysOfWeek = new();
    private SalaryCalculationConfig? _lastConfig;

    public SalaryViewModel(
        SalaryCalculationService salaryService,
        MonthlyScheduleService   scheduleService,
        EmployeeService          employeeService,
        SalarySettingService     salarySettingService,
        ShopSettingService       shopSettingService,
        ShopContext              shopContext,
        IAppSnackbarService      snackbar,
        HttpClient               http)
    {
        _salaryService        = salaryService;
        _scheduleService      = scheduleService;
        _employeeService      = employeeService;
        _salarySettingService = salarySettingService;
        _shopSettingService   = shopSettingService;
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
    [ObservableProperty] private bool _hasSavedRecord;
    [ObservableProperty] private string _savedLabel = string.Empty;

    public ObservableCollection<SalaryScheduleItem> AvailableSchedules { get; } = new();
    public ObservableCollection<EmployeeSalaryItem> EmployeeItems      { get; } = new();

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
            var shopSetting = await _shopSettingService.GetAsync();
            _shopClosedDaysOfWeek = shopSetting?.ClosedDaysOfWeek ?? new();

            AvailableSchedules.Clear();
            foreach (var s in schedules.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month))
                AvailableSchedules.Add(new SalaryScheduleItem { Schedule = s });

            if (AvailableSchedules.Count > 0)
                SelectedScheduleItem = AvailableSchedules[0];
        }
        finally { IsLoading = false; }
    }

    partial void OnSelectedScheduleItemChanged(SalaryScheduleItem? value)
    {
        EmployeeItems.Clear();
        HasResult      = false;
        HasSavedRecord = false;

        if (value is not null)
            _ = CheckSavedAsync(value.Schedule);
    }

    private async Task CheckSavedAsync(MonthlySchedule schedule)
    {
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
    }

    [RelayCommand(CanExecute = nameof(CanCalculate))]
    private async Task CalculateAsync()
    {
        if (SelectedScheduleItem is null || _laborLaw is null) return;

        // 取得班表與員工
        var schedule = await _scheduleService.GetAsync(
            SelectedScheduleItem.Schedule.Year,
            SelectedScheduleItem.Schedule.Month);
        if (schedule is null) return;

        var employees = await _employeeService.GetAllAsync();
        var scheduledEmpIds = schedule.Entries.Select(e => e.EmployeeId).ToHashSet();
        var eligibleEmps = employees
            .Where(e => !e.IsResigned && e.DefaultSalary is not null && scheduledEmpIds.Contains(e.Id))
            .ToList();

        // 取得國定假日清單（提供給 Config Dialog 顯示用）
        var nationalHolidays = await FetchNationalHolidaysAsync(schedule.Year, schedule.Month);

        // 顯示計算設定對話框（預填上次的設定）
        var configVm = new SalaryCalculationConfigViewModel(
            schedule, eligibleEmps, _shopClosedDaysOfWeek, _lastConfig);

        var result = await DialogHost.Show(
            new SalaryCalculationConfigDialog(configVm), "RootDialog");

        if (result is not SalaryCalculationConfig config) return;

        _lastConfig = config;   // 記住本次設定，下次開啟時預填

        IsLoading = true;
        try
        {
            await DoCalculateAsync(schedule, eligibleEmps, config, nationalHolidays);
            await SaveAsync();  // 計算完成後自動儲存
        }
        finally { IsLoading = false; }
    }

    private async Task DoCalculateAsync(
        MonthlySchedule schedule,
        List<Employee> eligibleEmps,
        SalaryCalculationConfig config,
        List<DateOnly> nationalHolidays)
    {
        if (_laborLaw is null) return;

        var record = _salaryService.Calculate(
            schedule, eligibleEmps, _laborLaw, config, nationalHolidays, _shopContext.ShopId);

        // 最低薪資驗證
        foreach (var empRec in record.EmployeeRecords)
        {
            empRec.IsUnderMinWage = empRec.SalaryType switch
            {
                SalaryType.Hourly  => empRec.WeekdayPay + empRec.HolidayPay
                                      < (decimal)(empRec.WeekdayHours + empRec.HolidayHours) * _laborLaw.HourlyMinimumWage,
                SalaryType.Monthly => empRec.BaseAmount < _laborLaw.MonthlyMinimumWage,
                _ => false,
            };
        }

        // 每日明細標籤
        var dailyByEmp = schedule.Entries
            .Where(e => e.ShiftSetting is not null)
            .GroupBy(e => e.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(e => e.Date)
                       .Select(dg =>
                       {
                           var date    = dg.Key;
                           var hours   = dg.Sum(e => e.ShiftSetting!.WorkHours);
                           var over    = config.DailyOverrides
                               .FirstOrDefault(o => o.EmployeeId == g.Key && o.Date == date);
                           var tag     = over is not null
                               ? "替代"
                               : config.IsHoliday(date, nationalHolidays) ? "假日" : "平日";
                           return new SalaryDailyEntry
                           {
                               Date           = date,
                               Hours          = hours,
                               TypeTag        = tag,
                               OverrideAmount = over?.Amount,
                           };
                       })
                       .OrderBy(x => x.Date)
                       .ToList());

        // 保留既有 BonusItems
        var existingBonus = EmployeeItems.ToDictionary(
            i => i.Employee.Id,
            i => i.BonusItems.ToList());

        EmployeeItems.Clear();
        foreach (var empRec in record.EmployeeRecords)
        {
            var item = new EmployeeSalaryItem
            {
                Employee         = empRec.Employee,
                SalaryType       = empRec.SalaryType,
                WeekdayHours     = empRec.WeekdayHours,
                HolidayHours     = empRec.HolidayHours,
                OT1Hours         = empRec.OT1Hours,
                OT2Hours         = empRec.OT2Hours,
                WeekdayPay       = empRec.WeekdayPay,
                HolidayPay       = empRec.HolidayPay,
                OT1Pay           = empRec.OT1Pay,
                OT2Pay           = empRec.OT2Pay,
                OverridePay      = empRec.OverridePay,
                BaseAmount       = empRec.BaseAmount,
                HourlyRate       = empRec.HourlyRate,
                HolidayHourlyRate = empRec.HolidayHourlyRate,
                MonthlyBase      = empRec.MonthlyBase,
                IsUnderMinWage   = empRec.IsUnderMinWage,
            };

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

    private async Task<List<DateOnly>> FetchNationalHolidaysAsync(int year, int month)
    {
        try
        {
            var url  = $"https://cdn.jsdelivr.net/gh/ruyut/TaiwanCalendar/data/{year}.json";
            var json = await _http.GetStringAsync(url);
            var all  = JsonSerializer.Deserialize<List<CalendarDayDto>>(json);
            if (all is null) return new();

            var prefix = $"{year}{month:D2}";
            return all
                .Where(d => d.Date.StartsWith(prefix)
                         && d.IsHoliday
                         && !string.IsNullOrEmpty(d.Description)
                         && d.Week != "六" && d.Week != "日")
                .Select(d => new DateOnly(year, month, int.Parse(d.Date[6..])))
                .ToList();
        }
        catch { return new(); }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedScheduleItem is null || !HasResult) return;
        IsSaving = true;
        try
        {
            // 直接從 UI 狀態建立記錄（不重算，確保儲存的就是畫面顯示的結果）
            var record = new SalaryRecord
            {
                ShopId            = _shopContext.ShopId,
                MonthlyScheduleId = SelectedScheduleItem.Schedule.Id,
                Year              = SelectedScheduleItem.Schedule.Year,
                Month             = SelectedScheduleItem.Schedule.Month,
                UpdatedAt         = DateTime.Now,
            };

            foreach (var ui in EmployeeItems)
            {
                var empRec = new SalaryEmployeeRecord
                {
                    EmployeeId        = ui.Employee.Id,
                    Employee          = ui.Employee,
                    SalaryType        = ui.SalaryType,
                    HourlyRate        = ui.HourlyRate,
                    HolidayHourlyRate = ui.HolidayHourlyRate,
                    MonthlyBase       = ui.MonthlyBase,
                    WeekdayHours      = ui.WeekdayHours,
                    HolidayHours      = ui.HolidayHours,
                    OT1Hours          = ui.OT1Hours,
                    OT2Hours          = ui.OT2Hours,
                    WeekdayPay        = ui.WeekdayPay,
                    HolidayPay        = ui.HolidayPay,
                    OT1Pay            = ui.OT1Pay,
                    OT2Pay            = ui.OT2Pay,
                    OverridePay       = ui.OverridePay,
                    BaseAmount        = ui.BaseAmount,
                    BonusItems        = ui.BonusItems.Select(b => b.ToModel()).ToList(),
                };
                record.EmployeeRecords.Add(empRec);
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
        finally { IsSaving = false; }
    }
}
