using Microsoft.EntityFrameworkCore;
using ShopManager.Models;
using System.IO;
using System.Text.Json;

namespace ShopManager.Data;

public class AppDbContext : DbContext
{
    public DbSet<Shop> Shops { get; set; }
    public DbSet<ShopSetting> ShopSettings { get; set; }
    public DbSet<ShiftSetting> ShiftSettings { get; set; }
    public DbSet<LaborLawSetting> LaborLawSettings { get; set; }
    public DbSet<SalarySetting> SalarySettings { get; set; }
    public DbSet<Employee> Employees { get; set; }
    public DbSet<CustomContact> CustomContacts { get; set; }
    public DbSet<ScheduleRule> ScheduleRules { get; set; }
    public DbSet<DefaultBonus> DefaultBonuses { get; set; }
    public DbSet<MonthlySchedule> MonthlySchedules { get; set; }
    public DbSet<ScheduleEntry> ScheduleEntries { get; set; }
    public DbSet<ScheduleConflict> ScheduleConflicts { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shopmanager.db");
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ShopSetting - 序列化 ContactInfos 為 JSON
        modelBuilder.Entity<ShopSetting>()
            .Property(e => e.ContactInfos)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<ContactInfo>>(v, (JsonSerializerOptions?)null) ?? new List<ContactInfo>()
            );

        // ShopSetting - 序列化 ClosedDaysOfWeek 為 JSON
        modelBuilder.Entity<ShopSetting>()
            .Property(e => e.ClosedDaysOfWeek)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new List<int>()
            );

        // ScheduleRule - 序列化 List<int> 欄位為 JSON
        modelBuilder.Entity<ScheduleRule>()
            .Property(e => e.FixedOffDays)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new List<int>()
            );
        modelBuilder.Entity<ScheduleRule>()
            .Property(e => e.ExcludedShiftIds)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new List<int>()
            );
        modelBuilder.Entity<ScheduleRule>()
            .Property(e => e.ExcludedColleagueIds)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new List<int>()
            );

        // Employee - 序列化 ContactInfos / PreferredShiftIds 為 JSON
        modelBuilder.Entity<Employee>()
            .Property(e => e.ContactInfos)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<ContactInfo>>(v, (JsonSerializerOptions?)null) ?? new List<ContactInfo>()
            );
        modelBuilder.Entity<Employee>()
            .Property(e => e.PreferredShiftIds)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new List<int>()
            );

        // Employee 關聯
        modelBuilder.Entity<Employee>()
            .HasOne(e => e.DefaultShift)
            .WithMany()
            .HasForeignKey(e => e.DefaultShiftId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Employee>()
            .HasOne(e => e.DefaultSalary)
            .WithMany()
            .HasForeignKey(e => e.DefaultSalaryId)
            .OnDelete(DeleteBehavior.SetNull);

        // MonthlySchedule - 序列化 ClosedDays / ShiftDayConfigs 為 JSON
        modelBuilder.Entity<MonthlySchedule>()
            .Property(e => e.ClosedDays)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new List<int>()
            );
        modelBuilder.Entity<MonthlySchedule>()
            .Property(e => e.ShiftDayConfigs)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<ShiftDayConfig>>(v, (JsonSerializerOptions?)null) ?? new List<ShiftDayConfig>()
            );

        modelBuilder.Entity<MonthlySchedule>()
            .Property(e => e.ShiftDateOverrides)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<ShiftDateOverride>>(v, (JsonSerializerOptions?)null) ?? new List<ShiftDateOverride>()
            );

        modelBuilder.Entity<MonthlySchedule>()
            .Property(e => e.StaffingGapDays)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new List<int>()
            );

        modelBuilder.Entity<MonthlySchedule>()
            .Property(e => e.EmployeeDayOffs)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<EmployeeDayOff>>(v, (JsonSerializerOptions?)null) ?? new List<EmployeeDayOff>()
            );

        modelBuilder.Entity<MonthlySchedule>()
            .Property(e => e.WorkDayConditionConfigs)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<WorkDayConditionConfig>>(v, (JsonSerializerOptions?)null) ?? new List<WorkDayConditionConfig>()
            );

        modelBuilder.Entity<MonthlySchedule>()
            .Property(e => e.EmployeeWorkDays)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<EmployeeWorkDay>>(v, (JsonSerializerOptions?)null) ?? new List<EmployeeWorkDay>()
            );

        modelBuilder.Entity<MonthlySchedule>()
            .Property(e => e.ExcludeFromAutoAssignIds)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new List<int>()
            );

        // MonthlySchedule - 店鋪+年月唯一索引
        modelBuilder.Entity<MonthlySchedule>()
            .HasIndex(e => new { e.ShopId, e.Year, e.Month })
            .IsUnique();

        // ScheduleConflict 關聯（Cascade：班表刪除時一併移除衝突紀錄）
        modelBuilder.Entity<ScheduleConflict>()
            .HasOne<MonthlySchedule>()
            .WithMany()
            .HasForeignKey(c => c.ScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        // ScheduleEntry 關聯
        modelBuilder.Entity<ScheduleEntry>()
            .HasOne(e => e.MonthlySchedule)
            .WithMany(m => m.Entries)
            .HasForeignKey(e => e.MonthlyScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ScheduleEntry>()
            .HasOne(e => e.Employee)
            .WithMany()
            .HasForeignKey(e => e.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ScheduleEntry>()
            .HasOne(e => e.ShiftSetting)
            .WithMany()
            .HasForeignKey(e => e.ShiftSettingId)
            .OnDelete(DeleteBehavior.Cascade);

        // 種子資料 - 預設勞基法設定
        modelBuilder.Entity<LaborLawSetting>().HasData(new LaborLawSetting
        {
            Id = 1,
            HourlyMinimumWage = 183m,
            MonthlyMinimumWage = 27470m,
            HourlyOT1Rate = 1.34m,
            HourlyOT2Rate = 1.67m,
            MonthlyOT1Rate = 1.34m,
            MonthlyOT2Rate = 1.67m,
            HolidayOTRate = 2.0m,
            DailyNormalHours = 8.0,
            DailyMaxHours = 12.0,
            WeeklyMaxHours = 40.0,
            WeeklyRestDays = 2,
            MaxMonthlyOTHours = 46.0,
            MaxConsecutiveWorkDays = 6
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 關閉店鋪：刪除指定店鋪的所有資料
    //
    // ⚠️  新增任何與 ShopId 相關的資料表時，請在此方法補上對應的刪除邏輯。
    //     刪除順序須遵守 FK 相依性（子資料先於父資料）。
    // ──────────────────────────────────────────────────────────────────────────
    public async Task DeleteShopDataAsync(Guid shopId)
    {
        // 1. ScheduleEntry / ScheduleConflict（子）→ MonthlySchedule（父）
        var monthlyIds = await MonthlySchedules
            .Where(m => m.ShopId == shopId)
            .Select(m => m.Id)
            .ToListAsync();
        if (monthlyIds.Count > 0)
        {
            await ScheduleEntries
                .Where(e => monthlyIds.Contains(e.MonthlyScheduleId))
                .ExecuteDeleteAsync();
            await ScheduleConflicts
                .Where(c => monthlyIds.Contains(c.ScheduleId))
                .ExecuteDeleteAsync();
        }
        await MonthlySchedules.Where(m => m.ShopId == shopId).ExecuteDeleteAsync();

        // 2. Employee 子資料（CustomContact / ScheduleRule / DefaultBonus）→ Employee
        var employeeIds = await Employees
            .Where(e => e.ShopId == shopId)
            .Select(e => e.Id)
            .ToListAsync();
        if (employeeIds.Count > 0)
        {
            await Set<CustomContact>()
                .Where(c => employeeIds.Contains(c.EmployeeId)).ExecuteDeleteAsync();
            await Set<ScheduleRule>()
                .Where(r => employeeIds.Contains(r.EmployeeId)).ExecuteDeleteAsync();
            await Set<DefaultBonus>()
                .Where(b => employeeIds.Contains(b.EmployeeId)).ExecuteDeleteAsync();
        }
        await Employees.Where(e => e.ShopId == shopId).ExecuteDeleteAsync();

        // 3. 其他直屬 ShopId 的資料表
        await ShiftSettings.Where(s => s.ShopId == shopId).ExecuteDeleteAsync();
        await SalarySettings.Where(s => s.ShopId == shopId).ExecuteDeleteAsync();
        await ShopSettings.Where(s => s.ShopId == shopId).ExecuteDeleteAsync();

        // 4. Shop 本體（最後刪除）
        await Shops.Where(s => s.Id == shopId).ExecuteDeleteAsync();
    }
}
