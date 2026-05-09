using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using System.Collections.ObjectModel;

namespace ShopManager.ViewModels;

public partial class SalaryCalculationConfigViewModel : ObservableObject
{
    private readonly Dictionary<int, List<DateOnly>> _employeeDates;

    public List<WeekdayCheckItem> WeekdayItems { get; }
    public List<Employee> ScheduledEmployees { get; }

    [ObservableProperty] private bool _includeNationalHolidays = true;

    public ObservableCollection<DailyOverrideItem> Overrides { get; } = new();

    public SalaryCalculationConfigViewModel(
        MonthlySchedule schedule,
        List<Employee> employees,
        List<int> shopClosedDaysOfWeek,
        SalaryCalculationConfig? lastConfig = null)
    {
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

    public SalaryCalculationConfig BuildConfig() => new()
    {
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
