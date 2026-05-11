using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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
    public DbSet<LineFollower> LineFollowers { get; set; }
    public DbSet<SalaryRecord> SalaryRecords { get; set; }
    public DbSet<SalaryEmployeeRecord> SalaryEmployeeRecords { get; set; }
    public DbSet<SalaryBonusItem> SalaryBonusItems { get; set; }

    /// <summary>測試用：覆寫 DB 路徑（null 表示使用預設 BaseDirectory）</summary>
    public static string? OverrideDbPath { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dbPath = OverrideDbPath ?? DefaultDbPath();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }

    public static string DefaultDbPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShopManager");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "shopmanager.db");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ShopSetting
        modelBuilder.Entity<ShopSetting>().Property(e => e.ContactInfos)      .HasConversion(JsonConv<ContactInfo>());
        modelBuilder.Entity<ShopSetting>().Property(e => e.ClosedDaysOfWeek) .HasConversion(JsonConv<int>());
        modelBuilder.Entity<ShopSetting>().Property(e => e.OwnerLineBindings).HasConversion(JsonConv<OwnerLineBinding>());

        // ScheduleRule
        modelBuilder.Entity<ScheduleRule>().Property(e => e.FixedOffDays)         .HasConversion(JsonConv<int>());
        modelBuilder.Entity<ScheduleRule>().Property(e => e.ExcludedShiftIds)     .HasConversion(JsonConv<int>());
        modelBuilder.Entity<ScheduleRule>().Property(e => e.ExcludedColleagueIds) .HasConversion(JsonConv<int>());

        // Employee
        modelBuilder.Entity<Employee>().Property(e => e.ContactInfos)    .HasConversion(JsonConv<ContactInfo>());
        modelBuilder.Entity<Employee>().Property(e => e.PreferredShiftIds).HasConversion(JsonConv<int>());

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

        modelBuilder.Entity<Employee>()
            .HasOne(e => e.HolidaySalary)
            .WithMany()
            .HasForeignKey(e => e.HolidaySalaryId)
            .OnDelete(DeleteBehavior.SetNull);

        // MonthlySchedule
        modelBuilder.Entity<MonthlySchedule>().Property(e => e.ClosedDays)               .HasConversion(JsonConv<int>());
        modelBuilder.Entity<MonthlySchedule>().Property(e => e.ShiftDayConfigs)          .HasConversion(JsonConv<ShiftDayConfig>());
        modelBuilder.Entity<MonthlySchedule>().Property(e => e.ShiftDateOverrides)       .HasConversion(JsonConv<ShiftDateOverride>());
        modelBuilder.Entity<MonthlySchedule>().Property(e => e.StaffingGapDays)          .HasConversion(JsonConv<int>());
        modelBuilder.Entity<MonthlySchedule>().Property(e => e.EmployeeDayOffs)          .HasConversion(JsonConv<EmployeeDayOff>());
        modelBuilder.Entity<MonthlySchedule>().Property(e => e.WorkDayConditionConfigs)  .HasConversion(JsonConv<WorkDayConditionConfig>());
        modelBuilder.Entity<MonthlySchedule>().Property(e => e.EmployeeWorkDays)         .HasConversion(JsonConv<EmployeeWorkDay>());
        modelBuilder.Entity<MonthlySchedule>().Property(e => e.ExcludeFromAutoAssignIds) .HasConversion(JsonConv<int>());

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

        // SalaryRecord（無額外 JSON 欄位）

        // SalaryRecord 關聯（Cascade：班表刪除時一併移除薪資紀錄）
        modelBuilder.Entity<SalaryRecord>()
            .HasMany(r => r.EmployeeRecords)
            .WithOne()
            .HasForeignKey(e => e.SalaryRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SalaryEmployeeRecord>()
            .HasOne(e => e.Employee)
            .WithMany()
            .HasForeignKey(e => e.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SalaryEmployeeRecord>()
            .HasMany(e => e.BonusItems)
            .WithOne()
            .HasForeignKey(b => b.SalaryEmployeeRecordId)
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
        // 1. ScheduleEntry / ScheduleConflict / SalaryRecord（子）→ MonthlySchedule（父）
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
            await SalaryRecords
                .Where(r => monthlyIds.Contains(r.MonthlyScheduleId))
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

    private static ValueConverter<List<T>, string> JsonConv<T>() => new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<List<T>>(v, (JsonSerializerOptions?)null) ?? new List<T>());
}
