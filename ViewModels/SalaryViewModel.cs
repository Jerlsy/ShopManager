using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using ShopManager.Models;
using ShopManager.Services;
using ShopManager.Views.Dialogs;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

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
    private readonly LineService              _lineService;
    private readonly BankCodeService          _bankCodeService;

    private LaborLawSetting? _laborLaw;
    private List<int> _shopClosedDaysOfWeek = new();
    private SalaryCalculationConfig? _lastConfig;
    private CancellationTokenSource? _autoSaveCts;

    public event EventHandler<PayrollRecordWindowData>? OpenPayrollRecordRequested;

    public SalaryViewModel(
        SalaryCalculationService salaryService,
        MonthlyScheduleService   scheduleService,
        EmployeeService          employeeService,
        SalarySettingService     salarySettingService,
        ShopSettingService       shopSettingService,
        ShopContext              shopContext,
        IAppSnackbarService      snackbar,
        HttpClient               http,
        LineService              lineService,
        BankCodeService          bankCodeService)
    {
        _salaryService        = salaryService;
        _scheduleService      = scheduleService;
        _employeeService      = employeeService;
        _salarySettingService = salarySettingService;
        _shopSettingService   = shopSettingService;
        _shopContext          = shopContext;
        _snackbar             = snackbar;
        _http                 = http;
        _lineService          = lineService;
        _bankCodeService      = bankCodeService;
    }

    // ── 狀態 ────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CalculateCommand))]
    private bool _isLoading;

    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _hasSavedRecord;
    [ObservableProperty] private string _savedLabel = string.Empty;
    [ObservableProperty] private decimal _totalPersonnelCost;

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
            _ = LoadSavedAsync(value.Schedule);
    }

    private async Task LoadSavedAsync(MonthlySchedule schedule)
    {
        var saved = await _salaryService.GetByScheduleAsync(schedule.Id);
        if (saved is null) return;

        IsLoading = true;
        try
        {
            EmployeeItems.Clear();
            foreach (var empRec in saved.EmployeeRecords)
            {
                if (empRec.Employee is null) continue;

                var item = new EmployeeSalaryItem
                {
                    Employee          = empRec.Employee,
                    SalaryType        = empRec.SalaryType,
                    WeekdayHours      = empRec.WeekdayHours,
                    HolidayHours      = empRec.HolidayHours,
                    OT1Hours          = empRec.OT1Hours,
                    OT2Hours          = empRec.OT2Hours,
                    WeekdayPay        = empRec.WeekdayPay,
                    HolidayPay        = empRec.HolidayPay,
                    OT1Pay            = empRec.OT1Pay,
                    OT2Pay            = empRec.OT2Pay,
                    OverridePay       = empRec.OverridePay,
                    BaseAmount        = empRec.BaseAmount,
                    HourlyRate        = empRec.HourlyRate,
                    HolidayHourlyRate = empRec.HolidayHourlyRate,
                    MonthlyBase       = empRec.MonthlyBase,
                };

                item.OnGlobalChanged = () => { RefreshTotalCost(); ScheduleAutoSave(); };

                foreach (var b in empRec.BonusItems)
                {
                    var bonus = new BonusLineItem
                    {
                        SelectedPreset = BonusLineItem.Presets.FirstOrDefault(p => p.Type == b.PresetType)
                                         ?? BonusLineItem.Presets[0],
                        CustomLabel = b.Label,
                        Amount      = b.Amount,
                    };
                    bonus.OnChanged = () => { item.RefreshTotals(); RefreshTotalCost(); ScheduleAutoSave(); };
                    item.BonusItems.Add(bonus);
                }

                EmployeeItems.Add(item);
            }

            HasResult      = true;
            HasSavedRecord = true;
            SavedLabel     = $"已儲存 {saved.UpdatedAt:MM/dd HH:mm}";
            RefreshTotalCost();
        }
        finally { IsLoading = false; }
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

            item.OnGlobalChanged = () => { RefreshTotalCost(); ScheduleAutoSave(); };

            foreach (var bonus in sourceBonuses)
            {
                bonus.OnChanged = () => { item.RefreshTotals(); RefreshTotalCost(); ScheduleAutoSave(); };
                item.BonusItems.Add(bonus);
            }

            if (dailyByEmp.TryGetValue(empRec.Employee.Id, out var entries))
                foreach (var e in entries) item.DailyEntries.Add(e);

            EmployeeItems.Add(item);
        }

        HasResult = true;
        RefreshTotalCost();
    }

    private void RefreshTotalCost() =>
        TotalPersonnelCost = EmployeeItems.Sum(e => e.GrandTotal);

    private void ScheduleAutoSave()
    {
        if (!HasResult || IsLoading) return;
        _autoSaveCts?.Cancel();
        _autoSaveCts = new CancellationTokenSource();
        var local = _autoSaveCts;
        Task.Delay(800).ContinueWith(_ =>
        {
            if (!local.IsCancellationRequested)
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() => SaveAsync());
        });
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
        if (SelectedScheduleItem is null || !HasResult || IsSaving) return;
        IsSaving = true;
        try
        {
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
                record.EmployeeRecords.Add(new SalaryEmployeeRecord
                {
                    EmployeeId        = ui.Employee.Id,
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
                });
            }

            await _salaryService.SaveAsync(record);
            HasSavedRecord = true;
        }
        catch { /* auto-save：靜默失敗，不打擾使用者 */ }
        finally { IsSaving = false; }
    }

    [RelayCommand]
    private async Task OpenPayrollRecordAsync()
    {
        if (SelectedScheduleItem is null || !HasResult) return;

        var record = await _salaryService.GetByScheduleAsync(SelectedScheduleItem.Schedule.Id);
        if (record is null) return;

        // 雙重保險：用 NoTracking 重抓員工資料，覆寫 record 內可能 stale 的 Employee 參照
        var freshEmployees = await _employeeService.GetAllNoTrackingAsync();
        var empMap = freshEmployees.ToDictionary(e => e.Id);
        foreach (var er in record.EmployeeRecords)
            if (empMap.TryGetValue(er.EmployeeId, out var fresh))
                er.Employee = fresh;

        var banks       = await _bankCodeService.GetAllAsync();
        var shopSetting = await _shopSettingService.GetAsync();

        var data = new PayrollRecordWindowData
        {
            Record = record,
            BankCodes = banks,
            ShopName = shopSetting?.Name ?? string.Empty,
            UpdatePaymentStatus = (id, paid) => _salaryService.SetPaymentStatusAsync(id, paid),
            SendLineFlexMessage = async (userId, altText, contents) =>
            {
                var token = shopSetting?.LineChannelAccessToken;
                if (string.IsNullOrEmpty(token)) return false;
                return await _lineService.PushFlexMessageAsync(token, userId, altText, contents);
            },
        };

        OpenPayrollRecordRequested?.Invoke(this, data);
    }
}
