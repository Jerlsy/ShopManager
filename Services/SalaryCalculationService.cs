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
        await db.SalaryRecords
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

            // 依日期分組取得工時
            var entriesByDate = schedule.Entries
                .Where(e => e.EmployeeId == emp.Id && e.ShiftSetting is not null)
                .GroupBy(e => e.Date)
                .ToDictionary(g => g.Key, g => g.Sum(e => e.ShiftSetting!.WorkHours));

            double weekdayHours = 0, holidayHours = 0, ot1Hours = 0, ot2Hours = 0;
            decimal weekdayPay = 0, holidayPay = 0, ot1Pay = 0, ot2Pay = 0, overridePay = 0;

            foreach (var (date, hours) in entriesByDate)
            {
                if (hours <= 0) continue;

                // 額外設定優先：強制以指定金額取代當日薪資
                var over = config.DailyOverrides
                    .FirstOrDefault(o => o.EmployeeId == emp.Id && o.Date == date);
                if (over is not null)
                {
                    overridePay += over.Amount;
                    continue;
                }

                bool isHoliday = config.IsHoliday(date, nationalHolidays);

                if (salary.Type == SalaryType.Hourly)
                {
                    if (isHoliday)
                    {
                        // 假日：使用員工的假日薪資方案費率（無設定則回退到平日費率）
                        decimal hRate = emp.HolidaySalary?.HourlyRate ?? salary.HourlyRate ?? 0;
                        holidayPay   += hRate * (decimal)hours;
                        holidayHours += hours;
                    }
                    else
                    {
                        // 平日：OT 分層計算
                        decimal wRate      = salary.HourlyRate ?? 0;
                        decimal ot1Rate    = salary.OT1Rate ?? laborLaw.HourlyOT1Rate;
                        decimal ot2Rate    = salary.OT2Rate ?? laborLaw.HourlyOT2Rate;
                        double dailyNormal = laborLaw.DailyNormalHours;
                        double normal      = Math.Min(hours, dailyNormal);
                        double ot          = Math.Max(hours - dailyNormal, 0);
                        double ot1         = Math.Min(ot, 2);
                        double ot2         = Math.Max(ot - 2, 0);

                        weekdayPay   += wRate * (decimal)normal
                                      + wRate * ot1Rate * (decimal)ot1
                                      + wRate * ot2Rate * (decimal)ot2;
                        weekdayHours += hours;
                        ot1Hours     += ot1;
                        ot2Hours     += ot2;
                    }
                }
                else // Monthly
                {
                    decimal monthlyBase  = salary.MonthlyBase ?? 0;
                    decimal hourlyEquiv  = monthlyBase > 0 ? monthlyBase / 240m : 0;
                    decimal ot1Rate      = salary.OT1Rate ?? laborLaw.MonthlyOT1Rate;
                    decimal ot2Rate      = salary.OT2Rate ?? laborLaw.MonthlyOT2Rate;
                    decimal holRate      = salary.HolidayRate ?? laborLaw.HolidayOTRate;
                    double  dailyNormal  = laborLaw.DailyNormalHours;

                    if (isHoliday)
                    {
                        // 假日：補貼
                        holidayPay   += hourlyEquiv * holRate * (decimal)hours;
                        holidayHours += hours;
                    }
                    else
                    {
                        // 平日：OT 補貼（底薪固定，不在此累加）
                        double ot  = Math.Max(hours - dailyNormal, 0);
                        double ot1 = Math.Min(ot, 2);
                        double ot2 = Math.Max(ot - 2, 0);
                        ot1Pay     += hourlyEquiv * ot1Rate * (decimal)ot1;
                        ot2Pay     += hourlyEquiv * ot2Rate * (decimal)ot2;
                        weekdayHours += hours;
                        ot1Hours   += ot1;
                        ot2Hours   += ot2;
                    }
                }
            }

            // 月薪底薪固定
            if (salary.Type == SalaryType.Monthly)
                weekdayPay = salary.MonthlyBase ?? 0;

            decimal baseAmount = weekdayPay + holidayPay + ot1Pay + ot2Pay + overridePay;

            var empRecord = new SalaryEmployeeRecord
            {
                EmployeeId       = emp.Id,
                Employee         = emp,
                SalaryType       = salary.Type,
                HourlyRate       = salary.HourlyRate        ?? 0,
                HolidayHourlyRate = emp.HolidaySalary?.HourlyRate ?? salary.HourlyRate ?? 0,
                MonthlyBase      = salary.MonthlyBase       ?? 0,
                WeekdayHours     = Math.Round(weekdayHours,  2),
                HolidayHours     = Math.Round(holidayHours,  2),
                OT1Hours         = Math.Round(ot1Hours,      2),
                OT2Hours         = Math.Round(ot2Hours,      2),
                WeekdayPay       = Math.Round(weekdayPay,    0),
                HolidayPay       = Math.Round(holidayPay,    0),
                OT1Pay           = Math.Round(ot1Pay,        0),
                OT2Pay           = Math.Round(ot2Pay,        0),
                OverridePay      = Math.Round(overridePay,   0),
                BaseAmount       = Math.Round(baseAmount,    0),
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
