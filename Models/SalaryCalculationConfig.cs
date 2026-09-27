namespace ShopManager.Models;

public class SalaryCalculationConfig
{
    public HashSet<DayOfWeek> HolidayDaysOfWeek { get; set; } = new();
    public bool IncludeNationalHolidays { get; set; }
    public List<DailyOverride> DailyOverrides { get; set; } = new();

    public int LateGraceMinutes       { get; set; } = 10;
    public int EarlyLeaveGraceMinutes { get; set; } = 10;

    /// <summary>員工 Id → 當月每日打卡；null＝未匯入打卡資料，完全照班表計算</summary>
    public Dictionary<int, Dictionary<DateOnly, DayPunch>>? Attendance { get; set; }

    /// <summary>本次匯入時業主手動對應的打卡名稱（員工 Id → 打卡名稱），計算後寫回員工資料</summary>
    public Dictionary<int, string> ClockNameMappings { get; set; } = new();

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
