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
    [ObservableProperty] private string _skippedNote = string.Empty;

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
        var mappable = _activeEmployees.Where(e => e.DefaultSalary is not null).OrderBy(e => e.Name).ToList();
        var noSalary = new List<string>();

        foreach (var person in inMonth)
        {
            if (byClockName.TryGetValue(person.Name, out var emp))
            {
                if (emp.DefaultSalary is null) { noSalary.Add(emp.Name); continue; }
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
        SkippedNote    = noSalary.Count > 0
            ? $"以下員工未設定薪資方案，打卡紀錄不列入計算：{string.Join("、", noSalary)}"
            : string.Empty;
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
        SkippedNote    = string.Empty;
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
