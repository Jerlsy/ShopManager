using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using System.Collections.ObjectModel;

namespace ShopManager.ViewModels;

public partial class SalaryCalculationConfigViewModel : ObservableObject
{
    private readonly Dictionary<int, List<DateOnly>> _employeeDates;
    private readonly MonthlySchedule _schedule;
    private readonly List<Employee> _activeEmployees;

    public List<WeekdayCheckItem> WeekdayItems { get; }
    public List<Employee> ScheduledEmployees { get; }

    [ObservableProperty] private bool _includeNationalHolidays = true;

    public ObservableCollection<DailyOverrideItem> Overrides { get; } = new();

    // ── 打卡資料 ─────────────────────────────────────────────
    [ObservableProperty] private int _lateGraceMinutes = 10;
    [ObservableProperty] private int _earlyLeaveGraceMinutes = 10;

    [ObservableProperty] private bool _hasAttendance;
    [ObservableProperty] private string _importFileName = string.Empty;
    [ObservableProperty] private string _importSummary = string.Empty;
    [ObservableProperty] private string _importError = string.Empty;

    /// <summary>打卡檔裡找不到對應員工的名字，讓業主手動指定</summary>
    public ObservableCollection<ClockNameMappingItem> UnmatchedNames { get; } = new();

    // 已自動對應到員工的打卡（員工 Id → 打卡）
    private Dictionary<int, List<ClockPunch>> _matchedPunches = new();

    public SalaryCalculationConfigViewModel(
        MonthlySchedule schedule,
        List<Employee> employees,
        List<Employee> activeEmployees,
        List<int> shopClosedDaysOfWeek,
        SalaryCalculationConfig? lastConfig = null)
    {
        _schedule        = schedule;
        _activeEmployees = activeEmployees;

        // 假日星期選項（不含店休日），預填上次勾選
        WeekdayItems = new List<WeekdayCheckItem>
        {
            new() { Day = DayOfWeek.Monday,    Label = "週一", IsShopClosed = shopClosedDaysOfWeek.Contains(1) },
            new() { Day = DayOfWeek.Tuesday,   Label = "週二", IsShopClosed = shopClosedDaysOfWeek.Contains(2) },
            new() { Day = DayOfWeek.Wednesday, Label = "週三", IsShopClosed = shopClosedDaysOfWeek.Contains(3) },
            new() { Day = DayOfWeek.Thursday,  Label = "週四", IsShopClosed = shopClosedDaysOfWeek.Contains(4) },
            new() { Day = DayOfWeek.Friday,    Label = "週五", IsShopClosed = shopClosedDaysOfWeek.Contains(5) },
            new() { Day = DayOfWeek.Saturday,  Label = "週六", IsShopClosed = shopClosedDaysOfWeek.Contains(6) },
            new() { Day = DayOfWeek.Sunday,    Label = "週日", IsShopClosed = shopClosedDaysOfWeek.Contains(0) },
        };

        if (lastConfig is not null)
        {
            foreach (var item in WeekdayItems)
                item.IsChecked = lastConfig.HolidayDaysOfWeek.Contains(item.Day);
            IncludeNationalHolidays = lastConfig.IncludeNationalHolidays;
            LateGraceMinutes        = lastConfig.LateGraceMinutes;
            EarlyLeaveGraceMinutes  = lastConfig.EarlyLeaveGraceMinutes;
        }

        // 各員工有排班的日期清單
        _employeeDates = schedule.Entries
            .Where(e => e.ShiftSetting is not null)
            .GroupBy(e => e.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.Date).Distinct().OrderBy(d => d).ToList());

        ScheduledEmployees = employees
            .Where(e => _employeeDates.ContainsKey(e.Id))
            .OrderBy(e => e.Name)
            .ToList();
    }

    [RelayCommand]
    private void AddOverride()
    {
        Overrides.Add(new DailyOverrideItem(ScheduledEmployees, _employeeDates));
    }

    [RelayCommand]
    private void RemoveOverride(DailyOverrideItem item)
    {
        Overrides.Remove(item);
    }

    [RelayCommand]
    private void ImportAttendance()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "選擇 POS 匯出的打卡紀錄",
            Filter = "Excel 檔案 (*.xlsx)|*.xlsx",
        };
        if (dlg.ShowDialog() != true) return;

        ClearAttendance();

        List<ClockPerson> people;
        try
        {
            people = Services.AttendanceImportService.Parse(dlg.FileName);
        }
        catch (Exception ex)
        {
            ImportError = $"無法讀取檔案：{ex.Message}";
            return;
        }

        // 只取班表當月的打卡
        foreach (var p in people)
            p.Punches.RemoveAll(x => x.Time.Year != _schedule.Year || x.Time.Month != _schedule.Month);
        var inMonth = people.Where(p => p.Punches.Count > 0).ToList();

        if (inMonth.Count == 0)
        {
            ImportError = $"檔案中沒有 {_schedule.Year} 年 {_schedule.Month} 月的打卡紀錄";
            return;
        }

        var byClockName = _activeEmployees
            .GroupBy(e => e.EffectiveClockName)
            .ToDictionary(g => g.Key, g => g.First());
        // 未設定薪資的員工也照常對應：是否缺薪資設定統一在按「確定並計算」時檢查（不匯入打卡也要檢查）
        var mappable = _activeEmployees.OrderBy(e => e.Name).ToList();

        foreach (var person in inMonth)
        {
            if (byClockName.TryGetValue(person.Name, out var emp))
            {
                if (!_matchedPunches.TryGetValue(emp.Id, out var list))
                    _matchedPunches[emp.Id] = list = new();
                list.AddRange(person.Punches);
            }
            else
            {
                UnmatchedNames.Add(new ClockNameMappingItem
                {
                    ClockName = person.Name,
                    DayCount  = person.Punches.Select(x => DateOnly.FromDateTime(x.Time)).Distinct().Count(),
                    Options   = mappable,
                    Punches   = person.Punches,
                });
            }
        }

        ImportFileName = System.IO.Path.GetFileName(dlg.FileName);
        ImportSummary  = $"已對應 {_matchedPunches.Count} 位員工"
                       + (UnmatchedNames.Count > 0 ? $"，{UnmatchedNames.Count} 個名字待指定" : string.Empty);
        HasAttendance  = true;
    }

    [RelayCommand]
    private void ClearAttendance()
    {
        _matchedPunches = new();
        UnmatchedNames.Clear();
        HasAttendance  = false;
        ImportFileName = string.Empty;
        ImportSummary  = string.Empty;
        ImportError    = string.Empty;
    }

    // ── 按「確定並計算」時的薪資設定檢查 ─────────────────────
    private const string DefaultConfirmText = "確定並計算";

    [ObservableProperty] private string _validationMessage = string.Empty;
    [ObservableProperty] private string _confirmText = DefaultConfirmText;

    // 已提醒過、業主選擇略過的「未設定薪資」名單；名單變了要重新提醒
    private string? _acknowledgedNoSalaryKey;

    /// <summary>
    /// 本次會進入計算的員工（當月有排班，或匯入的打卡對應到的人）都要有薪資方案；
    /// 有勾選任何假日（含國定假日）時，時薪制員工還要有假日薪資方案。
    /// 缺假日薪資一律擋下；缺薪資方案提醒一次，再按一次則略過這些人照常計算。
    /// </summary>
    public bool Validate()
    {
        var attendanceIds = new HashSet<int>(_matchedPunches.Keys);
        foreach (var m in UnmatchedNames)
            if (m.SelectedEmployee is not null) attendanceIds.Add(m.SelectedEmployee.Id);

        var participants = _activeEmployees
            .Where(e => _employeeDates.ContainsKey(e.Id) || (HasAttendance && attendanceIds.Contains(e.Id)))
            .OrderBy(e => e.Name)
            .ToList();

        bool anyHoliday = IncludeNationalHolidays || WeekdayItems.Any(w => w.IsChecked && !w.IsShopClosed);
        var noHolidaySalary = anyHoliday
            ? participants.Where(e => e.DefaultSalary?.Type == SalaryType.Hourly && e.HolidaySalaryId is null).ToList()
            : new List<Employee>();

        if (noHolidaySalary.Count > 0)
        {
            _acknowledgedNoSalaryKey = null;
            ConfirmText       = DefaultConfirmText;
            ValidationMessage = "已勾選假日，但以下時薪制員工尚未設定假日薪資，請先至員工管理設定後再計算："
                              + string.Join("、", noHolidaySalary.Select(e => e.Name));
            return false;
        }

        var noSalary = participants.Where(e => e.DefaultSalary is null).ToList();
        if (noSalary.Count > 0)
        {
            var key = string.Join(",", noSalary.Select(e => e.Id));
            if (_acknowledgedNoSalaryKey == key) return true;

            _acknowledgedNoSalaryKey = key;
            ConfirmText       = "略過並計算";
            ValidationMessage = "以下員工尚未設定薪資方案，不會列入本次薪資計算："
                              + string.Join("、", noSalary.Select(e => e.Name))
                              + "。請先至員工管理設定，或按「略過並計算」繼續。";
            return false;
        }

        ValidationMessage = string.Empty;
        ConfirmText       = DefaultConfirmText;
        return true;
    }

    private Dictionary<int, Dictionary<DateOnly, DayPunch>>? BuildAttendance()
    {
        if (!HasAttendance) return null;

        var all = _matchedPunches.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        foreach (var m in UnmatchedNames.Where(m => m.SelectedEmployee is not null))
        {
            if (!all.TryGetValue(m.SelectedEmployee!.Id, out var list))
                all[m.SelectedEmployee.Id] = list = new();
            list.AddRange(m.Punches);
        }
        return all.ToDictionary(kv => kv.Key, kv => Services.AttendanceImportService.BuildDays(kv.Value));
    }

    public SalaryCalculationConfig BuildConfig() => new()
    {
        LateGraceMinutes       = Math.Max(0, LateGraceMinutes),
        EarlyLeaveGraceMinutes = Math.Max(0, EarlyLeaveGraceMinutes),
        Attendance             = BuildAttendance(),
        ClockNameMappings      = UnmatchedNames
            .Where(m => m.SelectedEmployee is not null)
            .GroupBy(m => m.SelectedEmployee!.Id)
            .ToDictionary(g => g.Key, g => g.First().ClockName),
        HolidayDaysOfWeek = WeekdayItems
            .Where(w => w.IsChecked && !w.IsShopClosed)
            .Select(w => w.Day)
            .ToHashSet(),
        IncludeNationalHolidays = IncludeNationalHolidays,
        DailyOverrides = Overrides
            .Where(o => o.SelectedEmployee is not null && o.SelectedDate.HasValue)
            .Select(o => new DailyOverride
            {
                EmployeeId = o.SelectedEmployee!.Id,
                Date       = o.SelectedDate!.Value,
                Amount     = o.Amount,
            })
            .ToList(),
    };
}

public partial class ClockNameMappingItem : ObservableObject
{
    public string ClockName { get; init; } = string.Empty;
    public int DayCount { get; init; }
    public List<Employee> Options { get; init; } = new();
    public List<ClockPunch> Punches { get; init; } = new();

    [ObservableProperty] private Employee? _selectedEmployee;

    [RelayCommand] private void Skip() => SelectedEmployee = null;
}

public partial class WeekdayCheckItem : ObservableObject
{
    public DayOfWeek Day { get; init; }
    public string Label { get; init; } = string.Empty;
    public bool IsShopClosed { get; init; }
    [ObservableProperty] private bool _isChecked;
}

public partial class DailyOverrideItem : ObservableObject
{
    private readonly Dictionary<int, List<DateOnly>> _employeeDates;

    public List<Employee> AllEmployees { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableDates))]
    private Employee? _selectedEmployee;

    public List<DateOnly> AvailableDates =>
        SelectedEmployee is not null && _employeeDates.TryGetValue(SelectedEmployee.Id, out var dates)
            ? dates
            : new List<DateOnly>();

    [ObservableProperty] private DateOnly? _selectedDate;
    [ObservableProperty] private decimal _amount;

    public DailyOverrideItem(List<Employee> employees, Dictionary<int, List<DateOnly>> employeeDates)
    {
        AllEmployees    = employees;
        _employeeDates  = employeeDates;
    }
}
