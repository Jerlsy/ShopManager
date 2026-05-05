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
        List<DateOnly> holidayDates,
        Guid shopId)
    {
        var record = new SalaryRecord
        {
            ShopId             = shopId,
            MonthlyScheduleId  = schedule.Id,
            Year               = schedule.Year,
            Month              = schedule.Month,
            HolidayDates       = holidayDates,
            UpdatedAt          = DateTime.Now,
        };

        var closedSet  = schedule.ClosedDays.ToHashSet();
        var holidaySet = holidayDates.Select(d => d.Day).ToHashSet();

        foreach (var emp in employees)
        {
            var salary = emp.DefaultSalary;
            if (salary is null) continue;

            // 固定休假的星期幾（0=Sunday … 6=Saturday）
            var fixedOffDows = emp.ScheduleRules
                .Where(r => r.Type == ScheduleRuleType.FixedOff)
                .SelectMany(r => r.FixedOffDays)
                .ToHashSet();

            // 取得該員工當月所有排班，依日期分組
            var entriesByDay = schedule.Entries
                .Where(e => e.EmployeeId == emp.Id && e.ShiftSetting is not null)
                .GroupBy(e => e.Date.Day)
                .ToDictionary(g => g.Key, g => g.Sum(e => e.ShiftSetting!.WorkHours));

            double normalHours  = 0, ot1Hours = 0, ot2Hours = 0;
            double restDayHours = 0, holidayHours = 0;

            foreach (var (day, hours) in entriesByDay)
            {
                if (hours <= 0) continue;

                bool isHoliday  = salary.Type == SalaryType.Monthly && holidaySet.Contains(day);
                bool isRestDay  = closedSet.Contains(day);
                var  date       = new DateOnly(schedule.Year, schedule.Month, day);
                bool isFixedOff = fixedOffDows.Contains((int)date.DayOfWeek);

                if (isHoliday)
                {
                    // 國定假日（月薪制已勾選）：全部計假日
                    holidayHours += hours;
                }
                else if (isRestDay)
                {
                    // 店休日出勤：前2hr×OT1，超過×OT2
                    restDayHours += hours;
                }
                else
                {
                    // 一般工作日（含固定休假日有排班 → 不加成，正常分層）
                    double dailyNormal = laborLaw.DailyNormalHours;
                    normalHours += Math.Min(hours, dailyNormal);
                    double ot = Math.Max(hours - dailyNormal, 0);
                    ot1Hours  += Math.Min(ot, 2);
                    ot2Hours  += Math.Max(ot - 2, 0);
                }
            }

            var empRecord = BuildRecord(emp, salary, laborLaw,
                normalHours, ot1Hours, ot2Hours, restDayHours, holidayHours);

            // 從員工預設獎金帶入
            foreach (var bonus in emp.DefaultBonuses)
                empRecord.BonusItems.Add(new SalaryBonusItem
                {
                    Label     = bonus.Label,
                    Amount    = bonus.Amount,
                    PresetType = BonusPresetType.Custom,
                });

            record.EmployeeRecords.Add(empRecord);
        }

        return record;
    }

    private static SalaryEmployeeRecord BuildRecord(
        Employee emp, SalarySetting salary, LaborLawSetting law,
        double normalHours, double ot1Hours, double ot2Hours,
        double restDayHours, double holidayHours)
    {
        decimal ot1Rate     = salary.OT1Rate     ?? law.HourlyOT1Rate;
        decimal ot2Rate     = salary.OT2Rate     ?? law.HourlyOT2Rate;
        decimal holidayRate = salary.HolidayRate ?? law.HolidayOTRate;

        decimal normalPay = 0, ot1Pay = 0, ot2Pay = 0, restPay = 0, holidayPay = 0;

        switch (salary.Type)
        {
            case SalaryType.Hourly:
            {
                decimal rate = salary.HourlyRate ?? 0;
                normalPay  = rate * (decimal)normalHours;
                ot1Pay     = rate * ot1Rate     * (decimal)ot1Hours;
                ot2Pay     = rate * ot2Rate     * (decimal)ot2Hours;
                // 店休日：前2hr×OT1，超過×OT2
                double rd1 = Math.Min(restDayHours, 2);
                double rd2 = Math.Max(restDayHours - 2, 0);
                restPay    = rate * ot1Rate * (decimal)rd1
                           + rate * ot2Rate * (decimal)rd2;
                break;
            }
            case SalaryType.Monthly:
            {
                decimal monthlyBase = salary.MonthlyBase ?? 0;
                decimal hourlyEquiv = monthlyBase / 240m;
                normalPay   = monthlyBase;   // 底薪固定
                ot1Pay      = hourlyEquiv * ot1Rate     * (decimal)ot1Hours;
                ot2Pay      = hourlyEquiv * ot2Rate     * (decimal)ot2Hours;
                double rd1  = Math.Min(restDayHours, 2);
                double rd2  = Math.Max(restDayHours - 2, 0);
                restPay     = hourlyEquiv * ot1Rate * (decimal)rd1
                            + hourlyEquiv * ot2Rate * (decimal)rd2;
                holidayPay  = hourlyEquiv * holidayRate * (decimal)holidayHours;
                break;
            }
            case SalaryType.Contract:
                normalPay = salary.ContractAmount ?? 0;
                break;
        }

        decimal baseAmount = normalPay + ot1Pay + ot2Pay + restPay + holidayPay;

        return new SalaryEmployeeRecord
        {
            EmployeeId     = emp.Id,
            Employee       = emp,
            SalaryType     = salary.Type,
            HourlyRate     = salary.HourlyRate     ?? 0,
            MonthlyBase    = salary.MonthlyBase    ?? 0,
            ContractAmount = salary.ContractAmount ?? 0,
            NormalHours    = Math.Round(normalHours,  2),
            OT1Hours       = Math.Round(ot1Hours,     2),
            OT2Hours       = Math.Round(ot2Hours,     2),
            RestDayHours   = Math.Round(restDayHours, 2),
            HolidayHours   = Math.Round(holidayHours, 2),
            NormalPay      = Math.Round(normalPay,   0),
            OT1Pay         = Math.Round(ot1Pay,      0),
            OT2Pay         = Math.Round(ot2Pay,      0),
            RestDayPay     = Math.Round(restPay,     0),
            HolidayPay     = Math.Round(holidayPay,  0),
            BaseAmount     = Math.Round(baseAmount,  0),
        };
    }

    // ── 儲存 ────────────────────────────────────────────────────────────
    public async Task<SalaryRecord> SaveAsync(SalaryRecord record)
    {
        var existing = await db.SalaryRecords
            .FirstOrDefaultAsync(r => r.MonthlyScheduleId == record.MonthlyScheduleId);

        if (existing is not null)
        {
            // 刪除舊紀錄，重新寫入（確保 snapshot 資料最新）
            db.SalaryRecords.Remove(existing);
            await db.SaveChangesAsync();
        }

        record.UpdatedAt = DateTime.Now;
        db.SalaryRecords.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    // ── 更新單筆員工的額外項目 ───────────────────────────────────────────
    public async Task UpdateBonusItemsAsync(int employeeRecordId, List<SalaryBonusItem> items)
    {
        var old = db.SalaryBonusItems.Where(b => b.SalaryEmployeeRecordId == employeeRecordId);
        db.SalaryBonusItems.RemoveRange(old);

        foreach (var item in items)
        {
            item.Id = 0;
            item.SalaryEmployeeRecordId = employeeRecordId;
            db.SalaryBonusItems.Add(item);
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int recordId)
    {
        await db.SalaryRecords.Where(r => r.Id == recordId).ExecuteDeleteAsync();
    }
}
