namespace ShopManager.Models;

public class SalaryCalculationConfig
{
    public HashSet<DayOfWeek> HolidayDaysOfWeek { get; set; } = new();
    public bool IncludeNationalHolidays { get; set; }
    public List<DailyOverride> DailyOverrides { get; set; } = new();

    public bool IsHoliday(DateOnly date, IEnumerable<DateOnly> nationalHolidays)
    {
        if (HolidayDaysOfWeek.Contains(date.DayOfWeek)) return true;
        if (IncludeNationalHolidays && nationalHolidays.Contains(date)) return true;
        return false;
    }
}

public class DailyOverride
{
    public int EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
}
