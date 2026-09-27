using Microsoft.EntityFrameworkCore;
using ShopManager.Data;
using ShopManager.Models;

namespace ShopManager.Services;

public class SalaryCalculationService(AppDbContext db)
{
    // ── 查詢 ────────────────────────────────────────────────────────────
    public async Task<List<SalaryRecord>> GetAllAsync(Guid shopId) =>
        await db.SalaryRecords
            .Where(r => r.ShopId == shopId)
            .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
            .ToListAsync();

    public async Task<SalaryRecord?> GetByScheduleAsync(int monthlyScheduleId) =>
        // AsNoTracking 確保不返回 DbContext 識別映射內的快取實體
        // （避免員工資料更新後仍顯示舊銀行帳號等問題）
        await db.SalaryRecords
            .AsNoTracking()
            .Include(r => r.EmployeeRecords)
                .ThenInclude(e => e.Employee)
            .Include(r => r.EmployeeRecords)
                .ThenInclude(e => e.BonusItems)
            .FirstOrDefaultAsync(r => r.MonthlyScheduleId == monthlyScheduleId);

    // ── 計算（不寫入 DB）────────────────────────────────────────────────
    public SalaryRecord Calculate(
        MonthlySchedule schedule,
        List<Employee> employees,
        LaborLawSetting laborLaw,
        SalaryCalculationConfig config,
        List<DateOnly> nationalHolidays,
        Guid shopId)
    {
        var record = new SalaryRecord
        {
            ShopId            = shopId,
            MonthlyScheduleId = schedule.Id,
            Year              = schedule.Year,
            Month             = schedule.Month,
            UpdatedAt         = DateTime.Now,
        };

        foreach (var emp in employees)
        {
            var salary = emp.DefaultSalary;
            if (salary is null) continue;

            var shiftsByDate = schedule.Entries
                .Where(e => e.EmployeeId == emp.Id && e.ShiftSetting is not null)
                .GroupBy(e => e.Date)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ShiftSetting!).ToList());

            // 加班倍率快照（時薪制、月薪制各自的預設值不同；員工個別設定優先）
            var (ot1Rate, ot2Rate) = salary.Type == SalaryType.Hourly
                ? (salary.OT1Rate ?? laborLaw.HourlyOT1Rate,  salary.OT2Rate ?? laborLaw.HourlyOT2Rate)
                : (salary.OT1Rate ?? laborLaw.MonthlyOT1Rate, salary.OT2Rate ?? laborLaw.MonthlyOT2Rate);

            // 有匯入打卡資料時，這位員工沒有任何打卡也要比對（每個排班日都會列為「有排班沒打卡」）
            Dictionary<DateOnly, DayPunch>? punches = null;
            if (config.Attendance is not null)
                punches = config.Attendance.GetValueOrDefault(emp.Id)?
                    .Where(kv => kv.Key.Year == schedule.Year && kv.Key.Month == schedule.Month)
                    .ToDictionary(kv => kv.Key, kv => kv.Value)
                    ?? new();

            var total  = new DayPay();
            var issues = new List<AttendanceIssue>();
            var daily  = new List<SalaryDailyEntry>();
            decimal overridePay    = 0;
            double  scheduledHours = 0;
            double  weekdayScheduledHours = 0, holidayScheduledHours = 0;
            double? weekdayClockedHours = punches is not null ? 0 : null;
            double? holidayClockedHours = punches is not null ? 0 : null;

            foreach (var (date, shifts) in shiftsByDate.OrderBy(kv => kv.Key))
            {
                var hours = shifts.Sum(s => s.WorkHours);
                if (hours <= 0) continue;
                scheduledHours += hours;
                bool isHoliday = config.IsHoliday(date, nationalHolidays);
                if (isHoliday) holidayScheduledHours += hours; else weekdayScheduledHours += hours;

                // 額外設定優先：強制以指定金額取代當日薪資（業主已明確指定，不再比對打卡）
                var over = config.DailyOverrides
                    .FirstOrDefault(o => o.EmployeeId == emp.Id && o.Date == date);
                if (over is not null)
                {
                    overridePay += over.Amount;
                    daily.Add(new SalaryDailyEntry { Date = date, Hours = hours, TypeTag = "替代", OverrideAmount = over.Amount });
                    continue;
                }

                var dayPay = ComputeDay(emp, salary, laborLaw, hours, isHoliday);
                var typeTag = isHoliday ? "假日" : "平日";

                if (punches is null)
                {
                    Accumulate(total, dayPay);
                    daily.Add(new SalaryDailyEntry { Date = date, Hours = hours, TypeTag = typeTag });
                    continue;
                }

                var start      = shifts.Min(s => date.ToDateTime(s.StartTime));
                var end        = shifts.Max(s => s.EndTime > s.StartTime
                                     ? date.ToDateTime(s.EndTime)
                                     : date.AddDays(1).ToDateTime(s.EndTime));
                var shiftLabel = $"{start:HH:mm}–{end:HH:mm}";
                string? issueLabel = null;
                DateTime? clockIn = null, clockOut = null;
                double? dayClockedHours = null;

                if (!punches.TryGetValue(date, out var p))
                {
                    issues.Add(new AttendanceIssue
                    {
                        Date = date, Type = AttendanceIssueType.NoPunch, IfCounted = dayPay,
                        Description = $"有排班 {shiftLabel}，沒有打卡紀錄",
                        ShortLabel  = "有排班未打卡",
                    });
                    issueLabel = "有排班未打卡";
                }
                else if (!p.IsComplete)
                {
                    issues.Add(new AttendanceIssue
                    {
                        Date = date, Type = AttendanceIssueType.HalfPunch, IfCounted = dayPay,
                        Description = $"有排班 {shiftLabel}，{HalfPunchText(p)}",
                        ShortLabel  = "打卡不完整",
                    });
                    issueLabel = "打卡不完整";
                    clockIn = p.In; clockOut = p.Out;
                }
                else
                {
                    Accumulate(total, dayPay);
                    clockIn = p.In; clockOut = p.Out;
                    dayClockedHours = Math.Round(p.Hours, 2);
                    if (isHoliday) holidayClockedHours += dayClockedHours.Value; else weekdayClockedHours += dayClockedHours.Value;

                    // 早到、晚走不另計；只有遲到或早退超過寬限才列出
                    var late  = (int)(p.In!.Value  - start).TotalMinutes;
                    var early = (int)(end - p.Out!.Value).TotalMinutes;
                    var parts = new List<string>();
                    if (late  > config.LateGraceMinutes)       parts.Add($"遲到 {late} 分");
                    if (early > config.EarlyLeaveGraceMinutes) parts.Add($"早退 {early} 分");
                    if (parts.Count > 0)
                    {
                        issues.Add(new AttendanceIssue
                        {
                            Date = date, Type = AttendanceIssueType.LateOrEarly,
                            Description = $"排班 {shiftLabel}，打卡 {p.In:HH:mm}–{p.Out:HH:mm}（{string.Join("、", parts)}）",
                            ShortLabel  = string.Join("、", parts),
                        });
                        issueLabel = string.Join("、", parts);
                    }
                }

                daily.Add(new SalaryDailyEntry
                {
                    Date = date, Hours = hours, TypeTag = typeTag,
                    ClockIn = clockIn, ClockOut = clockOut, ClockedHours = dayClockedHours,
                    IssueLabel = issueLabel,
                });
            }

            double? clockedHours = null;
            if (punches is not null)
            {
                foreach (var (date, p) in punches.Where(kv => !shiftsByDate.ContainsKey(kv.Key)))
                {
                    bool isHoliday = config.IsHoliday(date, nationalHolidays);
                    if (p.IsComplete)
                    {
                        var hrs = Math.Round(p.Hours, 2);
                        issues.Add(new AttendanceIssue
                        {
                            Date = date, Type = AttendanceIssueType.Unscheduled,
                            IfCounted   = ComputeDay(emp, salary, laborLaw, hrs, isHoliday),
                            Description = $"沒有排班，打卡 {p.In:HH:mm}–{p.Out:HH:mm}（{hrs:0.##} 小時）",
                            ShortLabel  = "未排班出勤",
                        });
                        if (isHoliday) holidayClockedHours += hrs; else weekdayClockedHours += hrs;
                        daily.Add(new SalaryDailyEntry
                        {
                            Date = date, Hours = 0, TypeTag = isHoliday ? "假日" : "平日",
                            ClockIn = p.In, ClockOut = p.Out, ClockedHours = hrs, IssueLabel = "未排班出勤",
                        });
                    }
                    else
                    {
                        issues.Add(new AttendanceIssue
                        {
                            Date = date, Type = AttendanceIssueType.HalfPunch,
                            Description = $"沒有排班，{HalfPunchText(p)}",
                            ShortLabel  = "未排班打卡",
                        });
                        daily.Add(new SalaryDailyEntry
                        {
                            Date = date, Hours = 0, TypeTag = isHoliday ? "假日" : "平日",
                            ClockIn = p.In, ClockOut = p.Out, IssueLabel = "未排班打卡",
                        });
                    }
                }
                clockedHours = Math.Round(punches.Values.Sum(p => p.Hours), 2);
            }

            // 月薪底薪固定
            if (salary.Type == SalaryType.Monthly)
                total.WeekdayPay = salary.MonthlyBase ?? 0;

            decimal baseAmount = total.WeekdayPay + total.HolidayPay + total.OT1Pay + total.OT2Pay + overridePay;

            var empRecord = new SalaryEmployeeRecord
            {
                EmployeeId       = emp.Id,
                Employee         = emp,
                SalaryType       = salary.Type,
                HourlyRate       = salary.HourlyRate        ?? 0,
                HolidayHourlyRate = emp.HolidaySalary?.HourlyRate ?? salary.HourlyRate ?? 0,
                MonthlyBase      = salary.MonthlyBase       ?? 0,
                OT1Rate          = ot1Rate,
                OT2Rate          = ot2Rate,
                WeekdayHours     = Math.Round(total.WeekdayHours, 2),
                HolidayHours     = Math.Round(total.HolidayHours, 2),
                OT1Hours         = Math.Round(total.OT1Hours,     2),
                OT2Hours         = Math.Round(total.OT2Hours,     2),
                WeekdayPay       = Math.Round(total.WeekdayPay,   0),
                HolidayPay       = Math.Round(total.HolidayPay,   0),
                OT1Pay           = Math.Round(total.OT1Pay,       0),
                OT2Pay           = Math.Round(total.OT2Pay,       0),
                OverridePay      = Math.Round(overridePay,        0),
                BaseAmount       = Math.Round(baseAmount,         0),
                ScheduledHours   = Math.Round(scheduledHours,     2),
                ClockedHours     = clockedHours,
                WeekdayScheduledHours = Math.Round(weekdayScheduledHours, 2),
                HolidayScheduledHours = Math.Round(holidayScheduledHours, 2),
                WeekdayClockedHours   = weekdayClockedHours.HasValue ? Math.Round(weekdayClockedHours.Value, 2) : null,
                HolidayClockedHours   = holidayClockedHours.HasValue ? Math.Round(holidayClockedHours.Value, 2) : null,
                AttendanceIssues = issues.OrderBy(i => i.Date).ToList(),
                DailyEntries     = daily.OrderBy(d => d.Date).ToList(),
            };

            // 帶入員工預設獎金
            foreach (var bonus in emp.DefaultBonuses)
                empRecord.BonusItems.Add(new SalaryBonusItem
                {
                    Label      = bonus.Label,
                    Amount     = bonus.Amount,
                    PresetType = BonusPresetType.Custom,
                });

            record.EmployeeRecords.Add(empRecord);
        }

        return record;
    }

    /// <summary>單日工時 → 薪資（平日含加班分段；月薪制平日只算加班補貼，底薪另計）</summary>
    public static DayPay ComputeDay(Employee emp, SalarySetting salary, LaborLawSetting laborLaw, double hours, bool isHoliday)
    {
        var d = new DayPay();
        double dailyNormal = laborLaw.DailyNormalHours;

        if (salary.Type == SalaryType.Hourly)
        {
            if (isHoliday)
            {
                // 假日：使用員工的假日薪資方案費率（無設定則回退到平日費率）
                decimal hRate  = emp.HolidaySalary?.HourlyRate ?? salary.HourlyRate ?? 0;
                d.HolidayPay   = hRate * (decimal)hours;
                d.HolidayHours = hours;
            }
            else
            {
                decimal wRate   = salary.HourlyRate ?? 0;
                decimal ot1Rate = salary.OT1Rate ?? laborLaw.HourlyOT1Rate;
                decimal ot2Rate = salary.OT2Rate ?? laborLaw.HourlyOT2Rate;
                double normal   = Math.Min(hours, dailyNormal);
                double ot       = Math.Max(hours - dailyNormal, 0);
                double ot1      = Math.Min(ot, 2);
                double ot2      = Math.Max(ot - 2, 0);

                d.WeekdayPay   = wRate * (decimal)normal
                               + wRate * ot1Rate * (decimal)ot1
                               + wRate * ot2Rate * (decimal)ot2;
                d.WeekdayHours = hours;
                d.OT1Hours     = ot1;
                d.OT2Hours     = ot2;
            }
        }
        else // Monthly
        {
            decimal monthlyBase = salary.MonthlyBase ?? 0;
            decimal hourlyEquiv = monthlyBase > 0 ? monthlyBase / 240m : 0;
            decimal ot1Rate     = salary.OT1Rate ?? laborLaw.MonthlyOT1Rate;
            decimal ot2Rate     = salary.OT2Rate ?? laborLaw.MonthlyOT2Rate;
            decimal holRate     = salary.HolidayRate ?? laborLaw.HolidayOTRate;

            if (isHoliday)
            {
                d.HolidayPay   = hourlyEquiv * holRate * (decimal)hours;
                d.HolidayHours = hours;
            }
            else
            {
                double ot  = Math.Max(hours - dailyNormal, 0);
                double ot1 = Math.Min(ot, 2);
                double ot2 = Math.Max(ot - 2, 0);
                d.OT1Pay       = hourlyEquiv * ot1Rate * (decimal)ot1;
                d.OT2Pay       = hourlyEquiv * ot2Rate * (decimal)ot2;
                d.WeekdayHours = hours;
                d.OT1Hours     = ot1;
                d.OT2Hours     = ot2;
            }
        }
        return d;
    }

    private static void Accumulate(DayPay total, DayPay day)
    {
        total.WeekdayHours += day.WeekdayHours;
        total.HolidayHours += day.HolidayHours;
        total.OT1Hours     += day.OT1Hours;
        total.OT2Hours     += day.OT2Hours;
        total.WeekdayPay   += day.WeekdayPay;
        total.HolidayPay   += day.HolidayPay;
        total.OT1Pay       += day.OT1Pay;
        total.OT2Pay       += day.OT2Pay;
    }

    private static string HalfPunchText(DayPunch p) => p.In.HasValue
        ? $"只有上班打卡 {p.In:HH:mm}，無下班紀錄"
        : $"只有下班打卡 {p.Out:HH:mm}，無上班紀錄";

    // ── 儲存 ────────────────────────────────────────────────────────────
    public async Task<SalaryRecord> SaveAsync(SalaryRecord record)
    {
        var existing = await db.SalaryRecords
            .FirstOrDefaultAsync(r => r.MonthlyScheduleId == record.MonthlyScheduleId);

        if (existing is not null)
        {
            db.SalaryRecords.Remove(existing);
            await db.SaveChangesAsync();
        }

        record.UpdatedAt = DateTime.Now;
        db.SalaryRecords.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    public async Task SetPaymentStatusAsync(int empRecordId, bool isPaid)
    {
        var rec = await db.SalaryEmployeeRecords.FindAsync(empRecordId);
        if (rec is null) return;
        rec.IsPaid  = isPaid;
        rec.PaidAt  = isPaid ? DateTime.Now : null;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int recordId) =>
        await db.SalaryRecords.Where(r => r.Id == recordId).ExecuteDeleteAsync();
}
