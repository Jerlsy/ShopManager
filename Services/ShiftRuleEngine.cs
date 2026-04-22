using ShopManager.Models;

namespace ShopManager.Services;

// ── Evaluation context ────────────────────────────────────────────────────

/// <summary>
/// 排班規則評估所需的靜態快照，每次拖放操作建立一次，各規則共享。
/// ExcludeEntryId：班表間移動時，排除來源班次本身的重疊/重複計算。
/// LaborLaw：勞基法設定，用於每日工時上限等系統規則。
/// </summary>
public record ShiftValidationContext(
    Employee Employee,
    DateOnly Date,
    ShiftSetting TargetShift,
    MonthlySchedule Schedule,
    IEnumerable<Employee> ActiveEmployees,
    int ExcludeEntryId = -1,
    LaborLawSetting? LaborLaw = null);

// ── Result ────────────────────────────────────────────────────────────────

/// <summary>單一規則或整體評估的結果：是否禁止，以及禁止原因。</summary>
public record ShiftValidationResult(bool IsBlocked, string Reason)
{
    public static readonly ShiftValidationResult Allow = new(false, string.Empty);
    public static ShiftValidationResult Block(string reason) => new(true, reason);
}

// ── Rule contract ─────────────────────────────────────────────────────────

/// <summary>
/// 排班規則介面。實作此介面即可插入 ShiftRuleEngine 的評估管線。
/// 返回 ShiftValidationResult.Allow 表示此規則通過，繼續往下評估。
/// 返回 ShiftValidationResult.Block(...) 表示此規則命中，立即禁止。
/// </summary>
public interface IShiftRule
{
    ShiftValidationResult Evaluate(ShiftValidationContext ctx);
}

// ── Engine ────────────────────────────────────────────────────────────────

/// <summary>
/// 評估管線：依序執行所有規則，第一個命中即回傳，未命中則允許。
/// 新增規則只需實作 IShiftRule 並加入 Rules 清單。
/// </summary>
public static class ShiftRuleEngine
{
    /// <summary>評估管線順序：先個人規則，再互動規則，最後系統規則。</summary>
    private static readonly IReadOnlyList<IShiftRule> Rules =
    [
        new FixedOffRule(),           // 員工固定休假日
        new ExcludeShiftRule(),       // 員工排除的班別
        new NotWithRule(),            // 員工不與某同事共班（正向）
        new ColleagueNotWithRule(),   // 已排班同事不與此員工共班（反向）
        new TimeOverlapRule(),        // 當天時間重疊
        new DailyMaxHoursRule(),      // 薪資別勞基法每日工時上限
    ];

    public static ShiftValidationResult Evaluate(ShiftValidationContext ctx)
    {
        foreach (var rule in Rules)
        {
            var result = rule.Evaluate(ctx);
            if (result.IsBlocked) return result;
        }
        return ShiftValidationResult.Allow;
    }

    public static string DayText(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday    => "一",
        DayOfWeek.Tuesday   => "二",
        DayOfWeek.Wednesday => "三",
        DayOfWeek.Thursday  => "四",
        DayOfWeek.Friday    => "五",
        DayOfWeek.Saturday  => "六",
        DayOfWeek.Sunday    => "日",
        _                   => ""
    };
}

// ── Concrete rules ────────────────────────────────────────────────────────

/// <summary>員工設定的固定休假日（ScheduleRuleType.FixedOff）</summary>
public sealed class FixedOffRule : IShiftRule
{
    public ShiftValidationResult Evaluate(ShiftValidationContext ctx)
    {
        foreach (var rule in ctx.Employee.ScheduleRules.Where(r => r.Type == ScheduleRuleType.FixedOff))
        {
            if (rule.FixedOffDays.Contains((int)ctx.Date.DayOfWeek))
                return ShiftValidationResult.Block(
                    $"固定休假日（週{ShiftRuleEngine.DayText(ctx.Date.DayOfWeek)}）");
        }
        return ShiftValidationResult.Allow;
    }
}

/// <summary>員工排除的班別類型（ScheduleRuleType.ExcludeShift）</summary>
public sealed class ExcludeShiftRule : IShiftRule
{
    public ShiftValidationResult Evaluate(ShiftValidationContext ctx)
    {
        foreach (var rule in ctx.Employee.ScheduleRules.Where(r => r.Type == ScheduleRuleType.ExcludeShift))
        {
            if (rule.ExcludedShiftIds.Contains(ctx.TargetShift.Id))
                return ShiftValidationResult.Block($"已排除「{ctx.TargetShift.Alias}」班別");
        }
        return ShiftValidationResult.Allow;
    }
}

/// <summary>
/// 員工設定不與某同事共班（正向）（ScheduleRuleType.NotWith）
/// 被拖曳的員工本身設定了不想與已排班的某人同班。
/// </summary>
public sealed class NotWithRule : IShiftRule
{
    public ShiftValidationResult Evaluate(ShiftValidationContext ctx)
    {
        var blockEntries = ctx.Schedule.Entries
            .Where(e => e.Date == ctx.Date && e.ShiftSettingId == ctx.TargetShift.Id)
            .ToList();

        foreach (var rule in ctx.Employee.ScheduleRules.Where(r => r.Type == ScheduleRuleType.NotWith))
        {
            var conflict = blockEntries.FirstOrDefault(e => rule.ExcludedColleagueIds.Contains(e.EmployeeId));
            if (conflict is null) continue;

            var colleague = ctx.ActiveEmployees.FirstOrDefault(e => e.Id == conflict.EmployeeId);
            return ShiftValidationResult.Block($"不與「{colleague?.Name ?? "某員工"}」同班");
        }
        return ShiftValidationResult.Allow;
    }
}

/// <summary>
/// 已排班同事設定不與此員工共班（反向，雙向 NotWith 保護）
/// A 設定不與 B 共班，當 B 被拖入有 A 的班次時同樣禁止。
/// </summary>
public sealed class ColleagueNotWithRule : IShiftRule
{
    public ShiftValidationResult Evaluate(ShiftValidationContext ctx)
    {
        var blockEntries = ctx.Schedule.Entries
            .Where(e => e.Date == ctx.Date && e.ShiftSettingId == ctx.TargetShift.Id);

        foreach (var existing in blockEntries)
        {
            var colleague = ctx.ActiveEmployees.FirstOrDefault(e => e.Id == existing.EmployeeId);
            if (colleague is null) continue;

            foreach (var rule in colleague.ScheduleRules.Where(r => r.Type == ScheduleRuleType.NotWith))
            {
                if (rule.ExcludedColleagueIds.Contains(ctx.Employee.Id))
                    return ShiftValidationResult.Block($"「{colleague.Name}」設定不與此員工同班");
            }
        }
        return ShiftValidationResult.Allow;
    }
}

/// <summary>
/// 當天同員工已有時間重疊的班次（系統規則，不需 ScheduleRule 設定）
/// 移動班次時，來源班次本身透過 ExcludeEntryId 排除，避免誤判。
/// </summary>
public sealed class TimeOverlapRule : IShiftRule
{
    public ShiftValidationResult Evaluate(ShiftValidationContext ctx)
    {
        var overlapping = ctx.Schedule.Entries
            .Where(e => e.Date == ctx.Date &&
                        e.EmployeeId == ctx.Employee.Id &&
                        e.ShiftSettingId != ctx.TargetShift.Id &&
                        e.Id != ctx.ExcludeEntryId &&
                        e.ShiftSetting is not null)
            .FirstOrDefault(e =>
                ctx.TargetShift.StartTime < e.ShiftSetting!.EndTime &&
                e.ShiftSetting.StartTime  < ctx.TargetShift.EndTime);

        if (overlapping is null) return ShiftValidationResult.Allow;

        return ShiftValidationResult.Block(
            $"與「{overlapping.ShiftSetting!.Alias}」班時間重疊" +
            $"（{overlapping.ShiftSetting.StartTime:HH\\:mm}–{overlapping.ShiftSetting.EndTime:HH\\:mm}）");
    }
}

/// <summary>
/// 薪資別勞基法每日工時上限（系統規則）
/// 當日所有班次工時加總（含目標班次）不可超過上限。
/// 上限來源優先順序：薪資設定自訂 DailyMaxHours → 勞基法依薪資類型對應值。
/// 合同制或無薪資設定時略過此規則。
/// </summary>
public sealed class DailyMaxHoursRule : IShiftRule
{
    public ShiftValidationResult Evaluate(ShiftValidationContext ctx)
    {
        if (ctx.LaborLaw is null) return ShiftValidationResult.Allow;

        var salary = ctx.Employee.DefaultSalary;
        double dailyMax;

        if (salary?.DailyMaxHours is not null)
        {
            dailyMax = salary.DailyMaxHours.Value;
        }
        else if (salary?.Type == SalaryType.Hourly)
        {
            dailyMax = ctx.LaborLaw.HourlyDailyMaxHours;
        }
        else if (salary?.Type == SalaryType.Monthly)
        {
            dailyMax = ctx.LaborLaw.MonthlyDailyMaxHours;
        }
        else
        {
            return ShiftValidationResult.Allow; // 合同制或無薪資設定，不限制
        }

        // 計算當日已排工時（移動時排除來源班次）
        var existingHours = ctx.Schedule.Entries
            .Where(e => e.Date == ctx.Date &&
                        e.EmployeeId == ctx.Employee.Id &&
                        e.Id != ctx.ExcludeEntryId &&
                        e.ShiftSetting is not null)
            .Sum(e => e.ShiftSetting!.WorkHours);

        var totalHours = existingHours + ctx.TargetShift.WorkHours;

        if (totalHours <= dailyMax) return ShiftValidationResult.Allow;

        return ShiftValidationResult.Block(
            $"當日工時將達 {totalHours:F1} 小時，超過每日上限 {dailyMax:F0} 小時");
    }
}
