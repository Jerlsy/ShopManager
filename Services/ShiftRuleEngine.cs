using ShopManager.Models;

namespace ShopManager.Services;

// ── Evaluation context ────────────────────────────────────────────────────

/// <summary>
/// O(1) 查詢索引，由 AutoScheduleService 在排班開始時建立並逐筆維護。
/// 拖放操作（無索引）仍回退到 LINQ 掃描，行為不變。
/// </summary>
public sealed class EntryIndex
{
    // (date, shiftId) → employeeIds
    private readonly Dictionary<(DateOnly, int), List<int>> _byDateShift = new();
    // date → employeeIds（已去重）
    private readonly Dictionary<DateOnly, HashSet<int>> _byDate = new();
    // employeeId → worked dates
    private readonly Dictionary<int, HashSet<DateOnly>> _byEmployee = new();
    // (employeeId, date) → entries（時間重疊 / 工時加總用）
    private readonly Dictionary<(int, DateOnly), List<ScheduleEntry>> _byEmpDate = new();
    // (employeeId, shiftId, date) → bool（快速重複偵測）
    private readonly HashSet<(int, int, DateOnly)> _exists = new();

    public void Add(ScheduleEntry e)
    {
        var ds  = (e.Date, e.ShiftSettingId);
        var ed  = (e.EmployeeId, e.Date);

        if (!_byDateShift.TryGetValue(ds, out var list)) _byDateShift[ds]  = list = new();
        list.Add(e.EmployeeId);

        if (!_byDate.TryGetValue(e.Date, out var set)) _byDate[e.Date]     = set  = new();
        set.Add(e.EmployeeId);

        if (!_byEmployee.TryGetValue(e.EmployeeId, out var dates)) _byEmployee[e.EmployeeId] = dates = new();
        dates.Add(e.Date);

        if (!_byEmpDate.TryGetValue(ed, out var el)) _byEmpDate[ed]        = el   = new();
        el.Add(e);

        _exists.Add((e.EmployeeId, e.ShiftSettingId, e.Date));
    }

    public bool AlreadyAssigned(int empId, int shiftId, DateOnly date) =>
        _exists.Contains((empId, shiftId, date));

    public IReadOnlyList<int> EmpsOnShift(DateOnly date, int shiftId) =>
        _byDateShift.TryGetValue((date, shiftId), out var l) ? l : Array.Empty<int>();

    public IReadOnlyCollection<int> EmpsOnDay(DateOnly date) =>
        _byDate.TryGetValue(date, out var s) ? s : Array.Empty<int>();

    public int CountOnShift(DateOnly date, int shiftId) =>
        _byDateShift.TryGetValue((date, shiftId), out var l) ? l.Count : 0;

    public IReadOnlyList<ScheduleEntry> EntriesForEmpOnDay(int empId, DateOnly date) =>
        _byEmpDate.TryGetValue((empId, date), out var l) ? l : Array.Empty<ScheduleEntry>();

    public IReadOnlyCollection<DateOnly> WorkedDates(int empId) =>
        _byEmployee.TryGetValue(empId, out var s) ? s : Array.Empty<DateOnly>();
}

/// <summary>
/// 排班規則評估所需的靜態快照，每次拖放操作建立一次，各規則共享。
/// ExcludeEntryId：班表間移動時，排除來源班次本身的重疊/重複計算。
/// LaborLaw：勞基法設定，用於每日工時上限等系統規則。
/// Index：自動排班時由 AutoScheduleService 提供，加速規則查詢（可為 null）。
/// </summary>
public record ShiftValidationContext(
    Employee Employee,
    DateOnly Date,
    ShiftSetting TargetShift,
    MonthlySchedule Schedule,
    IEnumerable<Employee> ActiveEmployees,
    int ExcludeEntryId = -1,
    LaborLawSetting? LaborLaw = null,
    IReadOnlyDictionary<int, ShiftSetting>? ShiftLookup = null,
    EntryIndex? Index = null);

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
/// 評估管線：依序執行所有規則，第一個命中即回傳 Block，未命中則允許。
/// 新增規則：實作 IShiftRule，按下方優先序插入 Rules 清單。
///
/// ── 規則優先序 ──────────────────────────────────────────────
///  群組 A  個人設定（優先封鎖，不需看其他人）
///    #1  FixedOffRule             — 員工固定休假日（週幾不排班）
///    #2  ExcludeShiftRule         — 員工排除的班別類型
///
///  群組 B  互動設定（涉及與同事配對）
///    #3  NotWithRule              — 員工本人設定不與某同事同班（正向）
///    #4  ColleagueNotWithRule     — 目標格同事設定不與此員工同班（反向，雙向保護）
///    #5  NotWithDayRule           — 員工本人設定不與某同事同天（正向，跨班別）
///    #6  ColleagueNotWithDayRule  — 目標日同事設定不與此員工同天（反向，雙向保護）
///
///  群組 C  系統規則（自動計算，不需設定）
///    #7  TimeOverlapRule          — 同員工當天班次時間重疊
///    #8  DailyMaxHoursRule        — 薪資別勞基法每日工時上限
///    #9  ConsecutiveDaysRule      — 超過勞基法最長連續上班日數（第36條，預設6天）
/// ────────────────────────────────────────────────────────────
/// </summary>
public static class ShiftRuleEngine
{
    private static readonly IReadOnlyList<IShiftRule> Rules =
    [
        // 群組 A：個人設定
        new FixedOffRule(),           // #1 固定休假日
        new ExcludeShiftRule(),       // #2 排除班別
        // 群組 B：互動設定
        new NotWithRule(),               // #3 不與同事同班（正向）
        new ColleagueNotWithRule(),      // #4 不與此員工同班（反向）
        new NotWithDayRule(),            // #5 不與同事同天（正向）
        new ColleagueNotWithDayRule(),   // #6 不與此員工同天（反向）
        // 群組 C：系統規則
        new TimeOverlapRule(),           // #7 時間重疊
        new DailyMaxHoursRule(),         // #8 每日工時上限
        new ConsecutiveDaysRule(),       // #9 最長連續上班日數
        new WeeklyMaxHoursRule(),        // #10 每週工時上限
    ];

    // 複製模式：只檢查群組 C（不限共事人，只限本人排班衝突）
    private static readonly IReadOnlyList<IShiftRule> CopyRules =
    [
        new TimeOverlapRule(),           // #7 時間重疊
        new DailyMaxHoursRule(),         // #8 每日工時上限
        new ConsecutiveDaysRule(),       // #9 最長連續上班日數
        new WeeklyMaxHoursRule(),        // #10 每週工時上限
    ];

    /// <summary>移動模式：全部規則 #1-#10</summary>
    public static ShiftValidationResult Evaluate(ShiftValidationContext ctx) =>
        Evaluate(ctx, Rules);

    /// <summary>複製模式：僅群組 C（#7-#10）</summary>
    public static ShiftValidationResult EvaluateForCopy(ShiftValidationContext ctx) =>
        Evaluate(ctx, CopyRules);

    private static ShiftValidationResult Evaluate(ShiftValidationContext ctx, IReadOnlyList<IShiftRule> rules)
    {
        foreach (var rule in rules)
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

/// <summary>員工設定不與某同事同班（正向）（ScheduleRuleType.NotWith）</summary>
public sealed class NotWithRule : IShiftRule
{
    public ShiftValidationResult Evaluate(ShiftValidationContext ctx)
    {
        var notWithRules = ctx.Employee.ScheduleRules.Where(r => r.Type == ScheduleRuleType.NotWith).ToList();
        if (notWithRules.Count == 0) return ShiftValidationResult.Allow;

        IEnumerable<int> empIds = ctx.Index is not null
            ? ctx.Index.EmpsOnShift(ctx.Date, ctx.TargetShift.Id)
            : ctx.Schedule.Entries
                .Where(e => e.Date == ctx.Date && e.ShiftSettingId == ctx.TargetShift.Id)
                .Select(e => e.EmployeeId);

        var empIdSet = empIds as IReadOnlyCollection<int> ?? empIds.ToList();

        foreach (var rule in notWithRules)
        {
            var conflictId = empIdSet.FirstOrDefault(id => rule.ExcludedColleagueIds.Contains(id));
            if (conflictId == 0) continue;

            var colleague = ctx.ActiveEmployees.FirstOrDefault(e => e.Id == conflictId);
            return ShiftValidationResult.Block($"不與「{colleague?.Name ?? "某員工"}」同班");
        }
        return ShiftValidationResult.Allow;
    }
}

/// <summary>
/// 已排班同事設定不與此員工同班（反向，雙向 NotWith 保護）
/// A 設定不與 B 同班，當 B 被拖入有 A 的班次時同樣禁止。
/// </summary>
public sealed class ColleagueNotWithRule : IShiftRule
{
    public ShiftValidationResult Evaluate(ShiftValidationContext ctx)
    {
        IEnumerable<int> empIds = ctx.Index is not null
            ? ctx.Index.EmpsOnShift(ctx.Date, ctx.TargetShift.Id)
            : ctx.Schedule.Entries
                .Where(e => e.Date == ctx.Date && e.ShiftSettingId == ctx.TargetShift.Id)
                .Select(e => e.EmployeeId);

        foreach (var empId in empIds)
        {
            var colleague = ctx.ActiveEmployees.FirstOrDefault(e => e.Id == empId);
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
        IEnumerable<ScheduleEntry> entries = ctx.Index is not null
            ? ctx.Index.EntriesForEmpOnDay(ctx.Employee.Id, ctx.Date)
            : ctx.Schedule.Entries.Where(e => e.Date == ctx.Date && e.EmployeeId == ctx.Employee.Id);

        foreach (var e in entries)
        {
            if (e.ShiftSettingId == ctx.TargetShift.Id || e.Id == ctx.ExcludeEntryId) continue;

            var eShift = e.ShiftSetting ?? ctx.ShiftLookup?.GetValueOrDefault(e.ShiftSettingId);
            if (eShift is null) continue;

            if (ctx.TargetShift.StartTime < eShift.EndTime &&
                eShift.StartTime          < ctx.TargetShift.EndTime)
            {
                return ShiftValidationResult.Block(
                    $"與「{eShift.Alias}」班時間重疊" +
                    $"（{eShift.StartTime:HH\\:mm}–{eShift.EndTime:HH\\:mm}）");
            }
        }
        return ShiftValidationResult.Allow;
    }
}

/// <summary>
/// 每日工時上限（系統規則，不分薪資別）
/// 當日所有班次工時加總（含目標班次）不可超過上限。
/// 上限來源優先順序：薪資設定自訂 DailyMaxHours → 勞基法共用 DailyMaxHours。
/// </summary>
public sealed class DailyMaxHoursRule : IShiftRule
{
    public ShiftValidationResult Evaluate(ShiftValidationContext ctx)
    {
        if (ctx.LaborLaw is null) return ShiftValidationResult.Allow;

        var dailyMax = ctx.Employee.DefaultSalary?.DailyMaxHours ?? ctx.LaborLaw.DailyMaxHours;
        if (dailyMax <= 0) return ShiftValidationResult.Allow;

        IEnumerable<ScheduleEntry> entries = ctx.Index is not null
            ? ctx.Index.EntriesForEmpOnDay(ctx.Employee.Id, ctx.Date)
            : ctx.Schedule.Entries.Where(e => e.Date == ctx.Date && e.EmployeeId == ctx.Employee.Id);

        var existingHours = entries
            .Where(e => e.Id != ctx.ExcludeEntryId)
            .Sum(e =>
            {
                if (e.ShiftSetting is not null) return e.ShiftSetting.WorkHours;
                if (ctx.ShiftLookup is not null && ctx.ShiftLookup.TryGetValue(e.ShiftSettingId, out var s)) return s.WorkHours;
                return 0.0;
            });

        var totalHours = existingHours + ctx.TargetShift.WorkHours;
        if (totalHours <= dailyMax) return ShiftValidationResult.Allow;

        return ShiftValidationResult.Block(
            $"當日工時將達 {totalHours:F1} 小時，超過每日上限 {dailyMax:F0} 小時");
    }
}

/// <summary>員工設定不與某同事同天（正向）（ScheduleRuleType.NotWithDay）
/// 檢查範圍：當天所有班次，不限同一班別。
/// </summary>
public sealed class NotWithDayRule : IShiftRule
{
    public ShiftValidationResult Evaluate(ShiftValidationContext ctx)
    {
        var notWithDayRules = ctx.Employee.ScheduleRules.Where(r => r.Type == ScheduleRuleType.NotWithDay).ToList();
        if (notWithDayRules.Count == 0) return ShiftValidationResult.Allow;

        IEnumerable<int> dayEmpIds = ctx.Index is not null
            ? ctx.Index.EmpsOnDay(ctx.Date).Where(id => id != ctx.Employee.Id)
            : ctx.Schedule.Entries
                .Where(e => e.Date == ctx.Date && e.EmployeeId != ctx.Employee.Id)
                .Select(e => e.EmployeeId).Distinct();

        var empIdSet = dayEmpIds as IReadOnlyCollection<int> ?? dayEmpIds.ToList();

        foreach (var rule in notWithDayRules)
        {
            var conflictId = empIdSet.FirstOrDefault(id => rule.ExcludedColleagueIds.Contains(id));
            if (conflictId == 0) continue;

            var colleague = ctx.ActiveEmployees.FirstOrDefault(e => e.Id == conflictId);
            return ShiftValidationResult.Block($"不與「{colleague?.Name ?? "某員工"}」同天");
        }
        return ShiftValidationResult.Allow;
    }
}

/// <summary>
/// 已排班同事設定不與此員工同天（反向，雙向 NotWithDay 保護）
/// 檢查當天所有班次中是否有同事設定了不與被拖曳員工同天。
/// </summary>
public sealed class ColleagueNotWithDayRule : IShiftRule
{
    public ShiftValidationResult Evaluate(ShiftValidationContext ctx)
    {
        IEnumerable<int> dayEmpIds = ctx.Index is not null
            ? ctx.Index.EmpsOnDay(ctx.Date).Where(id => id != ctx.Employee.Id)
            : ctx.Schedule.Entries
                .Where(e => e.Date == ctx.Date && e.EmployeeId != ctx.Employee.Id)
                .Select(e => e.EmployeeId).Distinct();

        foreach (var empId in dayEmpIds)
        {
            var colleague = ctx.ActiveEmployees.FirstOrDefault(e => e.Id == empId);
            if (colleague is null) continue;

            foreach (var rule in colleague.ScheduleRules.Where(r => r.Type == ScheduleRuleType.NotWithDay))
            {
                if (rule.ExcludedColleagueIds.Contains(ctx.Employee.Id))
                    return ShiftValidationResult.Block($"「{colleague.Name}」設定不與此員工同天");
            }
        }
        return ShiftValidationResult.Allow;
    }
}

/// <summary>
/// 勞基法第36條：不得超過最長連續上班日數（預設6天）。
/// 以目標日為中心，計算加入後的連續上班天數（含目標日）。
/// 已知限制：僅看當月班表，不跨月偵測月初/月末連續。
/// </summary>
public sealed class ConsecutiveDaysRule : IShiftRule
{
    public ShiftValidationResult Evaluate(ShiftValidationContext ctx)
    {
        if (ctx.LaborLaw is null) return ShiftValidationResult.Allow;

        int maxDays = ctx.LaborLaw.MaxConsecutiveWorkDays;
        if (maxDays <= 0) return ShiftValidationResult.Allow;

        // 使用索引的 HashSet（O(1)），或從 Entries 建立一次（O(n)，拖放情境）
        IReadOnlyCollection<DateOnly> workedDates = ctx.Index is not null
            ? ctx.Index.WorkedDates(ctx.Employee.Id)
            : ctx.Schedule.Entries
                .Where(e => e.EmployeeId == ctx.Employee.Id && e.Date != ctx.Date)
                .Select(e => e.Date)
                .ToHashSet();

        int before = 0;
        var check = ctx.Date.AddDays(-1);
        while (workedDates.Contains(check)) { before++; check = check.AddDays(-1); }

        int after = 0;
        check = ctx.Date.AddDays(1);
        while (workedDates.Contains(check)) { after++; check = check.AddDays(1); }

        int consecutive = before + 1 + after;
        if (consecutive <= maxDays) return ShiftValidationResult.Allow;

        return ShiftValidationResult.Block(
            $"加入後將連續上班 {consecutive} 天，超過法定上限 {maxDays} 天");
    }
}

/// <summary>
/// 每週工時上限（系統規則，不分薪資別）
/// 目標週所有班次工時加總（含目標班次）不可超過上限。
/// 週起始日依班表設定（WeekStartDay）。
/// 已知限制：僅看當月班表，不跨月偵測跨週工時。
/// </summary>
public sealed class WeeklyMaxHoursRule : IShiftRule
{
    public ShiftValidationResult Evaluate(ShiftValidationContext ctx)
    {
        if (ctx.LaborLaw is null) return ShiftValidationResult.Allow;

        var weeklyMax = ctx.Employee.DefaultSalary?.WeeklyMaxHours ?? ctx.LaborLaw.WeeklyMaxHours;
        if (weeklyMax <= 0) return ShiftValidationResult.Allow;

        var weekStart = ctx.Date;
        while ((int)weekStart.DayOfWeek != ctx.Schedule.WeekStartDay)
            weekStart = weekStart.AddDays(-1);
        var weekEnd = weekStart.AddDays(7);

        // 若有索引，逐日查 EntriesForEmpOnDay（均為 O(1)）；否則線性掃描（拖放情境）
        IEnumerable<ScheduleEntry> weekEntries;
        if (ctx.Index is not null)
        {
            var list = new List<ScheduleEntry>();
            for (var d = weekStart; d < weekEnd; d = d.AddDays(1))
                list.AddRange(ctx.Index.EntriesForEmpOnDay(ctx.Employee.Id, d));
            weekEntries = list;
        }
        else
        {
            weekEntries = ctx.Schedule.Entries
                .Where(e => e.EmployeeId == ctx.Employee.Id &&
                            e.Date >= weekStart && e.Date < weekEnd);
        }

        var existingHours = weekEntries
            .Where(e => e.Id != ctx.ExcludeEntryId)
            .Sum(e =>
            {
                if (e.ShiftSetting is not null) return e.ShiftSetting.WorkHours;
                if (ctx.ShiftLookup is not null && ctx.ShiftLookup.TryGetValue(e.ShiftSettingId, out var s)) return s.WorkHours;
                return 0.0;
            });

        var totalHours = existingHours + ctx.TargetShift.WorkHours;
        if (totalHours <= weeklyMax) return ShiftValidationResult.Allow;

        return ShiftValidationResult.Block(
            $"本週工時將達 {totalHours:F1} 小時，超過每週上限 {weeklyMax:F0} 小時");
    }
}
