using System.ComponentModel.DataAnnotations;

namespace ShopManager.Models;

/// <summary>月班表容器，一個年月對應一張班表</summary>
public class MonthlySchedule
{
    [Key] public int Id { get; set; }
    public Guid ShopId { get; set; }

    public int Year { get; set; }
    public int Month { get; set; }

    /// <summary>該月的店休日清單（JSON 序列化，存日期的 Day 值）</summary>
    public List<int> ClosedDays { get; set; } = new();

    /// <summary>一周起始日（建立時從系統設定帶入，0=Sunday, 1=Monday）</summary>
    public int WeekStartDay { get; set; } = 1;

    /// <summary>班表狀態</summary>
    public ScheduleStatus Status { get; set; } = ScheduleStatus.Draft;

    /// <summary>套用班別設定（哪些班別在哪幾天出現）</summary>
    public List<ShiftDayConfig> ShiftDayConfigs { get; set; } = new();

    /// <summary>單日班別覆寫（優先於 ShiftDayConfigs，空 ShiftIds = 當日無班別顯示）</summary>
    public List<ShiftDateOverride> ShiftDateOverrides { get; set; } = new();

    /// <summary>自動排班後人力不足的日期清單（Day 值）</summary>
    public List<int> StaffingGapDays { get; set; } = new();

    /// <summary>員工個人休息日（JSON 序列化，自動排班時跳過對應日期）</summary>
    public List<EmployeeDayOff> EmployeeDayOffs { get; set; } = new();

    /// <summary>上班日人力條件（自動排班設定，供下次自動排班時還原）</summary>
    public List<WorkDayConditionConfig> WorkDayConditionConfigs { get; set; } = new();

    /// <summary>員工強制上班日（只在指定日期排班）</summary>
    public List<EmployeeWorkDay> EmployeeWorkDays { get; set; } = new();

    /// <summary>排除自動排班的員工 ID 清單</summary>
    public List<int> ExcludeFromAutoAssignIds { get; set; } = new();

    /// <summary>該月的排班記錄</summary>
    public List<ScheduleEntry> Entries { get; set; } = new();
}

/// <summary>班別-星期對應設定（建立班表時指定）</summary>
public class ShiftDayConfig
{
    public int ShiftId { get; set; }
    /// <summary>套用的星期幾清單（0=Sunday, 1=Monday, …）</summary>
    public List<int> DaysOfWeek { get; set; } = new();
}

/// <summary>單日班別覆寫（優先於 ShiftDayConfigs 生效）</summary>
public class ShiftDateOverride
{
    public int Day { get; set; }
    public List<int> ShiftIds { get; set; } = new();
}

public enum ScheduleStatus
{
    Draft = 0,      // 草稿
    Published = 1   // 已發布
}

/// <summary>員工個人休息日設定</summary>
public class EmployeeDayOff
{
    public int EmployeeId { get; set; }
    public List<int> Days { get; set; } = new();
}

/// <summary>員工強制上班日設定（自動排班只排這幾天）</summary>
public class EmployeeWorkDay
{
    public int EmployeeId { get; set; }
    public List<int> Days { get; set; } = new();
}

/// <summary>自動排班：上班日人力條件（儲存供下次建立班表時帶入）</summary>
public class WorkDayConditionConfig
{
    public int MinPerDay { get; set; } = 1;
    public int MaxPerShift { get; set; } = 1;
    /// <summary>套用的星期幾（0=Sunday … 6=Saturday）</summary>
    public List<int> DaysOfWeek { get; set; } = new();
}
